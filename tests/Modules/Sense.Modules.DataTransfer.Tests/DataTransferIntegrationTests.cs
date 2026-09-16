using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.DataTransfer.Api;
using Sense.Modules.DataTransfer.Application;
using Sense.Modules.DataTransfer.Application.ImportJobs;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Modules.DataTransfer.Infrastructure.Persistence;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application.CustomFields;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.DataTransfer.Tests;

/// <summary>
/// Upload → analyze → map → run → rows/result through Dispatcher (validation, authorization, unit of work) against real PostgreSQL,
/// with the real Organization and Employee modules ("employee.master" template). Identity is replaced by test doubles.
/// </summary>
[Collection("PostgreSQL")]
public sealed class DataTransferIntegrationTests : TestFixture
{
    private const string Template = "employee.master";
    private readonly FakeDataScope _scope = new();
    private readonly FakePermissions _permissions = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);
        new DataTransferModule().AddModule(services, configuration);
        services.AddSingleton<IPermissionService>(_permissions);
        services.AddSingleton<IDataScopeService>(_scope);
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<IUserAccountLookup, NoUserAccounts>();
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<DataTransferDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Upsert_InsertsUpdatesAndReportsRowErrors_WithResultFile()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var ali = await CreateEmployeeAsync("Ali", "Kaya", Tckn.Next());
        var zeynep = await CreateEmployeeAsync("Zeynep", "Arslan", Tckn.Next());
        var ayseId = Tckn.Next();
        var denizId = Tckn.Next();

        var file = Xlsx(
            ["Ad", "Soyad", "Kimlik numarası", "İşe başlama tarihi", "Yönetici", "Şirket", "Departman", "Notlar"],
            ["Ayşe", "Yılmaz", ayseId, "01.03.2024", "zeynep  arslan", "Acme", "satış", "yeni"],
            ["Ali", "KAYA", ali.NationalId, new DateTime(2020, 1, 15), null, null, "AR-GE", null],
            ["Veli", "Can", "12345678901", null, null, null, null, null],
            ["Ali", "Kaya", ali.NationalId, null, null, null, null, null],
            ["Zeynep", "Yıldız", zeynep.NationalId, null, null, null, null, null],
            ["Can", "Demir", Tckn.Next(), "1 Ocak 2020", "Olmayan Kişi", null, "Satış", null],
            [null, null, null, null, null, null, null, null],
            ["Deniz", "Ak", denizId, null, null, null, null, null]);

        var job = await UploadAsync("personel.xlsx", file);
        job.Status.ShouldBe("Uploaded");
        job.TotalRows.ShouldBe(7);
        job.SourceColumns.Select(c => c.SuggestedTargetKey).ShouldBe(
            ["firstName", "lastName", "nationalId", "hireDate", "manager", "orgUnit:Company", "orgUnit:Department", null]);
        job.SourceColumns[0].Samples.ShouldBe(["Ayşe", "Ali", "Veli"]);
        job.SourceColumns[3].Samples[1].ShouldBe("15.01.2020");

        var mapped = await MapAsync(job, "Upsert", "SkipInvalidRows");
        mapped.Status.ShouldBe("Mapped");

        var run = await RunAsync(job.Id);
        run.Status.ShouldBe("CompletedWithErrors");
        (run.InsertedCount, run.UpdatedCount, run.SkippedCount, run.ErrorCount).ShouldBe((2, 1, 0, 4));
        run.FinishedAt.ShouldNotBeNull();

        var employees = await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Employees.AsNoTracking().ToListAsync();
        var ayse = employees.Single(e => e.NationalId == ayseId);
        (ayse.DisplayName, ayse.OrgUnitId, ayse.ManagerEmployeeId, ayse.HireDate).ShouldBe(("Ayşe Yılmaz", org.AcmeSales, zeynep.Id, new DateOnly(2024, 3, 1)));
        var aliRow = employees.Single(e => e.Id == ali.Id);
        (aliRow.DisplayName, aliRow.HireDate, aliRow.OrgUnitId).ShouldBe(("Ali Kaya", new DateOnly(2020, 1, 15), org.AcmeRnd));
        employees.ShouldContain(e => e.NationalId == denizId);
        employees.Count.ShouldBe(4);

        // Import goes through career history like every other write path (E2): initial Hire records with the resolved
        // per-level placement, and CareerChanged/ManagerChanged in the Employee outbox.
        var ayseCareer = ayse.CareerTimeline.ShouldHaveSingleItem();
        (ayseCareer.ValidFrom, ayseCareer.ChangeType, ayseCareer.ManagerEmployeeId).ShouldBe((new DateOnly(2024, 3, 1), CareerChangeType.Hire, (Guid?)zeynep.Id));
        ayseCareer.Assignments.Select(a => a.OrgUnitId).ShouldBe([org.AcmeSales, org.Acme], ignoreOrder: true);
        var aliCareer = aliRow.CareerTimeline.ShouldHaveSingleItem();
        (aliCareer.ValidFrom, aliCareer.ChangeType).ShouldBe((new DateOnly(2020, 1, 15), CareerChangeType.Hire));
        var outbox = await ServiceProvider!.GetRequiredService<EmployeeDbContext>().OutboxMessages.AsNoTracking().Where(m => m.TenantId == tenantId).Select(m => m.Type).ToListAsync();
        outbox.Count(t => t == EventTypeRegistry.NameOf(typeof(Sense.Modules.Employee.Contracts.Events.CareerChanged))).ShouldBe(2);
        outbox.Count(t => t == EventTypeRegistry.NameOf(typeof(Sense.Modules.Employee.Contracts.Events.ManagerChanged))).ShouldBe(1);

        var errors = (await ExecuteQueryAsync(new ListImportJobRowsQuery(job.Id, "Error", new PagedQuery { PageSize = 50 }))).Value;
        errors.TotalCount.ShouldBe(4);
        Codes(errors, 4).ShouldBe([ImportErrorCodes.InvalidNationalId]);
        Codes(errors, 5).ShouldContain(ImportErrorCodes.DuplicateRow);
        Codes(errors, 6).ShouldBe([ImportErrorCodes.NameMismatch]);
        Codes(errors, 7).ShouldBe([ImportErrorCodes.InvalidDate, ImportErrorCodes.LookupNotFound, ImportErrorCodes.AmbiguousMatch], ignoreOrder: true);
        errors.Items.Single(r => r.RowNumber == 4).Errors.Single().ShouldBe(new RowErrorDto("nationalId", ImportErrorCodes.InvalidNationalId, "Geçersiz T.C. kimlik numarası: '12345678901'."));
        errors.Items.Single(r => r.RowNumber == 4).MatchKey.ShouldBeNull();

        var all = (await ExecuteQueryAsync(new ListImportJobRowsQuery(job.Id, null, new PagedQuery { PageSize = 2 }))).Value;
        (all.TotalCount, all.Items[0].RowNumber, all.Items[0].Outcome, all.Items[1].Outcome).ShouldBe((7L, 2, "Inserted", "Updated"));

        var history = (await ExecuteQueryAsync(new ListImportJobsQuery(new PagedQuery()))).Value;
        var listed = history.Items.ShouldHaveSingleItem();
        (listed.FileName, listed.Status, listed.RequestedByName, listed.ErrorCount).ShouldBe(("personel.xlsx", "CompletedWithErrors", "HR", 4));

        var result = (await ExecuteQueryAsync(new DownloadImportResultQuery(job.Id, "tr"))).Value;
        result.FileName.ShouldBe("personel-sonuc.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var sheet = workbook.Worksheet(1);
        sheet.Row(1).CellsUsed().Select(c => c.GetString()).TakeLast(2).ShouldBe(["Sonuç", "Hata açıklaması"]);
        sheet.Cell(2, 9).GetString().ShouldBe("Eklendi");
        sheet.Cell(3, 9).GetString().ShouldBe("Güncellendi");
        sheet.Cell(4, 9).GetString().ShouldBe("Hata");
        sheet.Cell(4, 10).GetString().ShouldBe("Kimlik numarası: Geçersiz T.C. kimlik numarası: '12345678901'.");
        sheet.Cell(7, 10).GetString().ShouldContain("Departman: 'Satış' birden fazla kayıtla eşleşiyor.");
        sheet.Cell(3, 4).GetDateTime().ShouldBe(new DateTime(2020, 1, 15));

        var english = (await ExecuteQueryAsync(new DownloadImportResultQuery(job.Id, "en"))).Value;
        using var englishBook = new XLWorkbook(new MemoryStream(english.Content));
        englishBook.Worksheet(1).Cell(1, 10).GetString().ShouldBe("Error description");
        englishBook.Worksheet(1).Cell(2, 9).GetString().ShouldBe("Inserted");
    }

    [Fact]
    public async Task UpdateOnly_WritesOnlyMappedNonEmptyCells_ReportsNoMatch_AndRespectsDataScope()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var ali = await CreateEmployeeAsync("Ali", "Kaya", Tckn.Next(), workEmail: "ali@acme.com", hireDate: new DateOnly(2020, 1, 1), orgUnitId: org.AcmeSales);
        var berk = await CreateEmployeeAsync("Berk", "Çelik", Tckn.Next(), workEmail: "berk@acme.com");
        var outsider = await CreateEmployeeAsync("Oğuz", "Dış", Tckn.Next());

        var file = Xlsx(
            ["Kimlik numarası", "Ad", "Soyad", "Doğum tarihi", "İş e-postası", "Departman"],
            [ali.NationalId, null, null, "1990-05-05", null, "AR-GE"],
            [Tckn.Next(), "Yeni", "Kişi", "1991-01-01", null, null],
            [berk.NationalId, "berk", "çelik", null, "yeni@acme.com", null],
            [outsider.NationalId, "Oğuz", "Dış", "1985-01-01", null, null]);

        var job = await UploadAsync("guncelleme.xlsx", file);
        _scope.Visible = new HashSet<Guid> { ali.Id, berk.Id };

        // "İş e-postası" is ignored: its cells must not be written.
        var mapping = job.SourceColumns.Select(c => new ColumnMappingDto(c.Index, c.Header == "İş e-postası" ? null : c.SuggestedTargetKey)).ToList();
        (await ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, mapping, "UpdateOnly", "SkipInvalidRows"))).IsSuccess.ShouldBeTrue();

        var run = await RunAsync(job.Id);
        (run.Status, run.Mode, run.InsertedCount, run.UpdatedCount, run.SkippedCount, run.ErrorCount).ShouldBe(("CompletedWithErrors", "UpdateOnly", 0, 1, 1, 2));

        var employees = await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Employees.AsNoTracking().ToListAsync();
        employees.Count.ShouldBe(3);
        var aliRow = employees.Single(e => e.Id == ali.Id);
        (aliRow.BirthDate, aliRow.HireDate, aliRow.WorkEmail, aliRow.OrgUnitId, aliRow.DisplayName).ShouldBe(
            (new DateOnly(1990, 5, 5), new DateOnly(2020, 1, 1), "ali@acme.com", org.AcmeRnd, "Ali Kaya"));
        employees.Single(e => e.Id == berk.Id).WorkEmail.ShouldBe("berk@acme.com");
        employees.Single(e => e.Id == outsider.Id).BirthDate.ShouldBeNull();

        var rows = (await ExecuteQueryAsync(new ListImportJobRowsQuery(job.Id, null, new PagedQuery()))).Value;
        rows.Items.Select(r => r.Outcome).ShouldBe(["Updated", "Error", "Skipped", "Error"]);
        Codes(rows, 3).ShouldBe([ImportErrorCodes.NoMatch]);
        Codes(rows, 5).ShouldBe([ImportErrorCodes.OutOfScope]);
    }

    [Fact]
    public async Task AllOrNothing_AppliesNothingWhenAnyRowFails_AndUnitScopeLimitsInserts()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var validId = Tckn.Next();
        var file = Xlsx(["Ad", "Soyad", "Kimlik numarası", "Şirket", "Departman"],
            ["Ayşe", "Yılmaz", validId, "Acme", "Satış"],
            ["Veli", "Can", "11111111111", null, null]);

        var job = await UploadAsync("hepsi.xlsx", file);
        await MapAsync(job, "Upsert", "AllOrNothing");
        var run = await RunAsync(job.Id);

        (run.Status, run.FailurePolicy, run.InsertedCount, run.UpdatedCount, run.SkippedCount, run.ErrorCount).ShouldBe(("Failed", "AllOrNothing", 0, 0, 1, 1));
        (await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Employees.AsNoTracking().AnyAsync()).ShouldBeFalse();
        var rows = (await ExecuteQueryAsync(new ListImportJobRowsQuery(job.Id, null, new PagedQuery()))).Value;
        rows.Items.Select(r => r.Outcome).ShouldBe(["Skipped", "Error"]);

        // A finished job can neither be remapped nor re-run.
        (await ExecuteCommandAsync(new RunImportJobCommand(job.Id))).Error.Code.ShouldBe(DataTransferDomainErrors.JobNotRunnable);
        (await ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, [], "Upsert", "SkipInvalidRows"))).Error.Code.ShouldBe(DataTransferDomainErrors.JobNotEditable);

        // Unit-scoped caller: inserts need a target unit inside the accessible set.
        _scope.AccessibleOrgUnits = new HashSet<Guid> { org.AcmeSales };
        var scopedFile = Xlsx(["Ad", "Soyad", "Kimlik numarası", "Şirket", "Departman"],
            ["Ayşe", "Yılmaz", validId, "Acme", "Satış"],
            ["Can", "Ak", Tckn.Next(), null, null],
            ["Cem", "Ak", Tckn.Next(), "Acme", "AR-GE"]);
        var scopedJob = await UploadAsync("kapsam.xlsx", scopedFile);
        await MapAsync(scopedJob, "Upsert", "SkipInvalidRows");
        var scopedRun = await RunAsync(scopedJob.Id);
        (scopedRun.InsertedCount, scopedRun.ErrorCount).ShouldBe((1, 2));
        var scopedRows = (await ExecuteQueryAsync(new ListImportJobRowsQuery(scopedJob.Id, "Error", new PagedQuery()))).Value;
        scopedRows.Items.SelectMany(r => r.Errors).Select(e => e.Code).ShouldBe([ImportErrorCodes.OutOfScope, ImportErrorCodes.OutOfScope]);
    }

    [Fact]
    public async Task Mapping_WithoutRequiredColumns_Returns422_AndUploadIsValidated()
    {
        SetUser();
        var file = Xlsx(["Ad", "Soyad", "Kimlik numarası"], ["Ali", "Kaya", Tckn.Next()]);
        var job = await UploadAsync("eksik.xlsx", file);

        (await ExecuteCommandAsync(new RunImportJobCommand(job.Id))).Error.Code.ShouldBe(DataTransferDomainErrors.JobNotRunnable);

        var mapping = job.SourceColumns.Select(c => new ColumnMappingDto(c.Index, c.SuggestedTargetKey == "lastName" ? "lastName" : null)).ToList();
        var saved = await ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, mapping, "Upsert", "SkipInvalidRows"));
        saved.IsFailure.ShouldBeTrue();
        saved.Error.Type.ShouldBe(ErrorType.Rule);
        saved.Error.Code.ShouldBe(ImportErrorCodes.UnmappedRequired);
        saved.Error.Args!["missing"].ShouldBe("firstName,nationalId");
        (await ExecuteQueryAsync(new GetImportJobQuery(job.Id))).Value.Status.ShouldBe("Uploaded");

        var duplicateTarget = job.SourceColumns.Select(c => new ColumnMappingDto(c.Index, "firstName")).ToList();
        (await ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, duplicateTarget, "Upsert", "SkipInvalidRows"))).Error.Code.ShouldBe(DataTransferErrors.MappingInvalid);
        await Should.ThrowAsync<FluentValidation.ValidationException>(() => ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, mapping, "InsertOnly", "SkipInvalidRows")));

        (await ExecuteCommandAsync(new CreateImportJobCommand(Template, "notes.txt", [1, 2, 3]))).Error.Code.ShouldBe(DataTransferErrors.FileTypeNotSupported);
        (await ExecuteCommandAsync(new CreateImportJobCommand(Template, "broken.xlsx", [1, 2, 3]))).Error.Code.ShouldBe(DataTransferErrors.FileUnreadable);
        (await ExecuteCommandAsync(new CreateImportJobCommand(Template, "empty.xlsx", Xlsx(["Ad"])))).Error.Code.ShouldBe(DataTransferErrors.FileEmpty);
        (await ExecuteCommandAsync(new CreateImportJobCommand("unknown.template", "a.xlsx", file))).Error.Code.ShouldBe(DataTransferErrors.TemplateNotFound);

        _permissions.Denied.Add("employee.employees.import");
        var forbidden = await ExecuteCommandAsync(new CreateImportJobCommand(Template, "a.xlsx", file));
        (forbidden.Error.Type, forbidden.Error.Args!["permission"]).ShouldBe((ErrorType.Forbidden, (object?)"employee.employees.import"));
    }

    [Fact]
    public async Task DownloadedTemplate_FilledAndUploaded_MapsByKeyRow_AndCsvIsSupported()
    {
        var tenantId = SetUser();
        var org = await SeedOrgAsync(tenantId);
        var template = (await ExecuteQueryAsync(new DownloadImportTemplateQuery(Template, "en"))).Value;

        var nationalId = Tckn.Next();
        byte[] filled;
        using (var workbook = new XLWorkbook(new MemoryStream(template.Content)))
        {
            var sheet = workbook.Worksheet(1);
            var columnOf = sheet.Row(2).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
            sheet.Cell(1, columnOf["firstName"]).Value = "Given name";
            sheet.Cell(3, columnOf["firstName"]).Value = "Selin";
            sheet.Cell(3, columnOf["lastName"]).Value = "Öz";
            sheet.Cell(3, columnOf["nationalId"]).Value = nationalId;
            sheet.Cell(3, columnOf["hireDate"]).Value = new DateTime(2025, 2, 3);
            sheet.Cell(3, columnOf["orgUnit:Department"]).Value = "AR-GE";
            // E6 columns: the workbook's Turkish label and the enum member name are both accepted.
            sheet.Cell(3, columnOf["gender"]).Value = "Kadın";
            sheet.Cell(3, columnOf["employmentType"]).Value = "PartTime";
            sheet.Cell(3, columnOf["childCount"]).Value = 2;
            sheet.Cell(3, columnOf["city"]).Value = "Ankara";
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            filled = stream.ToArray();
        }

        var job = await UploadAsync("template-filled.xlsx", filled);
        job.TotalRows.ShouldBe(1);
        var firstName = job.SourceColumns.Single(c => c.Header == "Given name");
        (firstName.SuggestedTargetKey, firstName.AutoMatched).ShouldBe(("firstName", true));
        // Every template column except the renamed "firstName" header maps itself by the hidden key row.
        job.SourceColumns.Count(c => c.SuggestedTargetKey is not null).ShouldBe(36);

        await MapAsync(job, "Upsert", "SkipInvalidRows");
        var run = await RunAsync(job.Id);
        (run.Status, run.InsertedCount).ShouldBe(("Completed", 1));
        var selin = await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Employees.AsNoTracking().SingleAsync(e => e.NationalId == nationalId);
        (selin.DisplayName, selin.HireDate, selin.OrgUnitId).ShouldBe(("Selin Öz", new DateOnly(2025, 2, 3), org.AcmeRnd));
        (selin.Gender, selin.EmploymentType, selin.ChildCount, selin.City)
            .ShouldBe((Gender.Female, EmploymentType.PartTime, 2, "Ankara"));

        var csvId = Tckn.Next();
        var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes($"Ad;Soyad;Kimlik numarası;Doğum tarihi\r\nÇağla;Şen;{csvId};21/09/1999\r\n")).ToArray();
        var csvJob = await UploadAsync("liste.csv", csv);
        csvJob.SourceColumns.Select(c => c.SuggestedTargetKey).ShouldBe(["firstName", "lastName", "nationalId", "birthDate"]);
        await MapAsync(csvJob, "Upsert", "SkipInvalidRows");
        (await RunAsync(csvJob.Id)).InsertedCount.ShouldBe(1);
        (await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Employees.AsNoTracking().SingleAsync(e => e.NationalId == csvId)).BirthDate.ShouldBe(new DateOnly(1999, 9, 21));
    }

    /// <summary>
    /// E6 part 2 (salary block + custom field columns): a full block on a new-hire row creates a SalaryRecord; an
    /// empty block on an update row leaves existing salary history untouched; a block with only the effective date
    /// filled is silently ignored (11.3.03 "Maaş bloğu bütünlüğü"); a custom field column round-trips through
    /// CustomFieldValueCoercion, and an unknown option value is a row error rather than a crash.
    /// </summary>
    [Fact]
    public async Task Import_AppliesSalaryBlockAndCustomFields_PerRowSemantics()
    {
        var tenantId = SetUser();
        await SeedOrgAsync(tenantId);

        var definition = await ExecuteCommandAsync(new CreateCustomFieldDefinitionCommand(
            "beden", "Beden", CustomFieldDataType.SingleSelect, CustomFieldSection.Personal, ["S", "M", "L"], false, false, false));
        definition.IsSuccess.ShouldBeTrue(definition.IsFailure ? definition.Error.Code : null);

        var existing = await CreateEmployeeAsync("Deniz", "Kara", Tckn.Next());
        var existingSalary = await ExecuteCommandAsync(
            new AddSalaryRecordCommand(existing.Id, new DateOnly(2023, 1, 1), 20000m, "TRY", SalaryType.Net, PayFrequency.Monthly));
        existingSalary.IsSuccess.ShouldBeTrue(existingSalary.IsFailure ? existingSalary.Error.Code : null);

        var newHireId = Tckn.Next();
        var onlyDateId = Tckn.Next();
        var badOptionId = Tckn.Next();

        var template = (await ExecuteQueryAsync(new DownloadImportTemplateQuery(Template, "tr"))).Value;
        byte[] filled;
        using (var workbook = new XLWorkbook(new MemoryStream(template.Content)))
        {
            var sheet = workbook.Worksheet(1);
            var columnOf = sheet.Row(2).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);

            // Row 3: new hire, full salary block + a valid custom field value.
            sheet.Cell(3, columnOf["firstName"]).Value = "Ela";
            sheet.Cell(3, columnOf["lastName"]).Value = "Su";
            sheet.Cell(3, columnOf["nationalId"]).Value = newHireId;
            sheet.Cell(3, columnOf["salaryEffectiveFrom"]).Value = new DateTime(2026, 1, 1);
            sheet.Cell(3, columnOf["salaryAmount"]).Value = "30000";
            sheet.Cell(3, columnOf["salaryCurrency"]).Value = "TL";
            sheet.Cell(3, columnOf["salaryType"]).Value = "Net";
            sheet.Cell(3, columnOf["payFrequency"]).Value = "Aylık";
            sheet.Cell(3, columnOf["transportAllowance"]).Value = "500";
            sheet.Cell(3, columnOf["customField:beden"]).Value = "M";

            // Row 4: existing employee, empty salary block and empty custom field column - both untouched.
            sheet.Cell(4, columnOf["nationalId"]).Value = existing.NationalId;
            sheet.Cell(4, columnOf["city"]).Value = "İzmir";

            // Row 5: new hire, only the effective date filled - the whole block is silently ignored (not an error).
            sheet.Cell(5, columnOf["firstName"]).Value = "Onur";
            sheet.Cell(5, columnOf["lastName"]).Value = "Ak";
            sheet.Cell(5, columnOf["nationalId"]).Value = onlyDateId;
            sheet.Cell(5, columnOf["salaryEffectiveFrom"]).Value = new DateTime(2026, 2, 1);

            // Row 6: new hire, unknown custom field option - a row error, not a crash.
            sheet.Cell(6, columnOf["firstName"]).Value = "Mert";
            sheet.Cell(6, columnOf["lastName"]).Value = "Yaz";
            sheet.Cell(6, columnOf["nationalId"]).Value = badOptionId;
            sheet.Cell(6, columnOf["customField:beden"]).Value = "XL";

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            filled = stream.ToArray();
        }

        var job = await UploadAsync("maas-ve-ozel-alan.xlsx", filled);
        await MapAsync(job, "Upsert", "SkipInvalidRows");
        var run = await RunAsync(job.Id);
        (run.InsertedCount, run.UpdatedCount, run.ErrorCount).ShouldBe((2, 1, 1));

        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var ela = await db.Employees.Include(e => e.SalaryRecords).Include(e => e.CustomFieldValues).AsSplitQuery().AsNoTracking().SingleAsync(e => e.NationalId == newHireId);
        var elaSalary = ela.SalaryRecords.ShouldHaveSingleItem();
        (elaSalary.EffectiveFrom, elaSalary.Amount, elaSalary.Currency, elaSalary.SalaryType, elaSalary.PayFrequency, elaSalary.TransportAllowance, elaSalary.Source).ShouldBe(
            (new DateOnly(2026, 1, 1), 30000m, "TRY", SalaryType.Net, PayFrequency.Monthly, 500m, SalarySource.Import));
        ela.CustomFieldValues.ShouldHaveSingleItem().Value.ShouldBe("M");

        var deniz = await db.Employees.Include(e => e.SalaryRecords).AsSplitQuery().AsNoTracking().SingleAsync(e => e.Id == existing.Id);
        deniz.SalaryRecords.ShouldHaveSingleItem().EffectiveFrom.ShouldBe(new DateOnly(2023, 1, 1));

        var onur = await db.Employees.Include(e => e.SalaryRecords).AsSplitQuery().AsNoTracking().SingleAsync(e => e.NationalId == onlyDateId);
        onur.SalaryRecords.ShouldBeEmpty();

        (await db.Employees.AsNoTracking().AnyAsync(e => e.NationalId == badOptionId)).ShouldBeFalse();
        var errors = (await ExecuteQueryAsync(new ListImportJobRowsQuery(job.Id, "Error", new PagedQuery()))).Value;
        var badRow = errors.Items.ShouldHaveSingleItem();
        badRow.Errors.Single().Code.ShouldBe(CustomFieldDomainErrors.OptionUnknown);
    }

    private static IReadOnlyList<string> Codes(PagedResult<ImportJobRowDto> rows, int rowNumber) =>
        rows.Items.Single(r => r.RowNumber == rowNumber).Errors.Select(e => e.Code).ToList();

    private async Task<ImportJobDto> UploadAsync(string fileName, byte[] content)
    {
        var result = await ExecuteCommandAsync(new CreateImportJobCommand(Template, fileName, content));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task<ImportJobDto> MapAsync(ImportJobDto job, string mode, string policy)
    {
        var mapping = job.SourceColumns.Select(c => new ColumnMappingDto(c.Index, c.SuggestedTargetKey)).ToList();
        var result = await ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, mapping, mode, policy));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task<ImportJobDto> RunAsync(Guid jobId)
    {
        var result = await ExecuteCommandAsync(new RunImportJobCommand(jobId));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task<EmployeeDetailDto> CreateEmployeeAsync(string firstName, string lastName, string nationalId, string? workEmail = null, DateOnly? hireDate = null, Guid? orgUnitId = null)
    {
        var result = await ExecuteCommandAsync(new CreateEmployeeCommand(firstName, lastName, nationalId, workEmail, null, null, hireDate, null, orgUnitId, null));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private sealed record Org(Guid Acme, Guid AcmeSales, Guid AcmeRnd, Guid BetaSales);

    /// <summary>Acme (Satış, AR-GE) and Beta (Satış): "Satış" alone is ambiguous, "Acme" + "Satış" is not.</summary>
    private async Task<Org> SeedOrgAsync(Guid tenantId)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(t => t.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        var company = types.Single(t => t.Code == OrgUnitTypeCodes.Company).Id;
        var department = types.Single(t => t.Code == OrgUnitTypeCodes.Department).Id;

        var acme = OrgUnit.Create(tenantId, company, null, "Acme").Value;
        var beta = OrgUnit.Create(tenantId, company, null, "Beta").Value;
        var acmeSales = OrgUnit.Create(tenantId, department, acme.Id, "Satış").Value;
        var acmeRnd = OrgUnit.Create(tenantId, department, acme.Id, "AR-GE").Value;
        var betaSales = OrgUnit.Create(tenantId, department, beta.Id, "Satış").Value;
        db.OrgUnits.AddRange(acme, beta, acmeSales, acmeRnd, betaSales);
        await db.SaveChangesAsync();
        return new Org(acme.Id, acmeSales.Id, acmeRnd.Id, betaSales.Id);
    }

    private static byte[] Xlsx(string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Veri");
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (rows[r][c])
                {
                    case DateTime date:
                        cell.Value = date;
                        break;
                    case string text:
                        cell.Value = text;
                        break;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private Guid SetUser()
    {
        var tenantId = Guid.NewGuid();
        _scope.Visible = null;
        _scope.AccessibleOrgUnits = null;
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: null, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
        return tenantId;
    }
}

internal sealed class FakePermissions : IPermissionService
{
    public HashSet<string> Denied { get; } = [];

    public Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(!Denied.Contains(permission));

    public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    public Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(DataScope.Company);
}

internal sealed class FakeDataScope : IDataScopeService
{
    public IReadOnlySet<Guid>? Visible { get; set; }

    public IReadOnlySet<Guid>? AccessibleOrgUnits { get; set; }

    public Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(Visible);

    public Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visible is null || Visible.Contains(employeeId));

    public Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(AccessibleOrgUnits);
}

internal sealed class AllAccessSettingsEnabled : IAccessSettingsService
{
    public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

internal sealed class NoUserAccounts : IUserAccountLookup
{
    public Task<IReadOnlyDictionary<Guid, UserAccountInfo>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, UserAccountInfo>>(new Dictionary<Guid, UserAccountInfo>());
}

internal static class Tckn
{
    private static int _seed = 200_000_000;

    /// <summary>Unique checksum-valid TCKN per call.</summary>
    public static string Next()
    {
        var d = Interlocked.Increment(ref _seed).ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => c - '0').ToList();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        d.Add((((odd * 7) - even) % 10 + 10) % 10);
        d.Add(d.Sum() % 10);
        return string.Concat(d);
    }
}

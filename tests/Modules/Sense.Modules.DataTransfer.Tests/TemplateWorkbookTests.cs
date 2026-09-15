using ClosedXML.Excel;
using NSubstitute;
using Sense.Modules.DataTransfer.Application;
using Sense.Modules.DataTransfer.Application.ImportJobs;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Modules.DataTransfer.Infrastructure.Files;
using Sense.Modules.Employee.Application.Import;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.DataTransfer.Tests;

/// <summary>Template workbook structure (real employee.master template, real ClosedXML writer/reader, org lookups stubbed).</summary>
public sealed class TemplateWorkbookTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly OrgUnitTypeInfo CompanyType = new(Guid.NewGuid(), "Company", "Şirket", 1);
    private static readonly OrgUnitTypeInfo DepartmentType = new(Guid.NewGuid(), "Department", "Departman", 3);

    [Fact]
    public async Task Template_HasHeadersHiddenKeyRowDropdownsInstructionsAndExamples()
    {
        var bytes = await DownloadAsync("tr");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));

        workbook.Worksheets.Select(w => w.Name).ShouldBe(["Çalışanlar", "Açıklamalar", "Örnekler", "Listeler"]);
        var data = workbook.Worksheet("Çalışanlar");

        var headers = data.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        headers.ShouldBe(["Ad", "Soyad", "Kimlik numarası", "İş e-postası", "Kişisel e-posta", "İş telefonu", "İşe başlama tarihi", "Doğum tarihi", "Yönetici", "Şirket", "Departman"]);

        var keys = data.Row(2).CellsUsed().Select(c => c.GetString()).ToList();
        keys.ShouldBe(["firstName", "lastName", "nationalId", "workEmail", "personalEmail", "workPhone", "hireDate", "birthDate", "manager", "orgUnit:Company", "orgUnit:Department"]);
        data.Row(2).IsHidden.ShouldBeTrue();
        data.RowsUsed(XLCellsUsedOptions.Contents).Count().ShouldBe(2);

        var lists = workbook.Worksheet("Listeler");
        lists.Visibility.ShouldBe(XLWorksheetVisibility.Hidden);
        lists.Column(2).CellsUsed().Select(c => c.GetString()).ShouldBe(["orgUnit:Department", "AR-GE", "Satış"]);

        var departmentValidation = data.DataValidations.Single(v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == 11));
        departmentValidation.AllowedValues.ShouldBe(XLAllowedValues.List);
        departmentValidation.Value.ShouldContain("Listeler");
        departmentValidation.Ranges.Single().RangeAddress.FirstAddress.RowNumber.ShouldBe(3);
        data.DataValidations.Count().ShouldBe(2);

        var instructions = workbook.Worksheet("Açıklamalar");
        instructions.Cell(1, 1).GetString().ShouldBe("Veri aktarım kuralları");
        var instructionText = string.Join('\n', instructions.CellsUsed().Select(c => c.GetString()));
        instructionText.ShouldContain("Kimlik numarası + Ad + Soyad");
        instructionText.ShouldContain("GG.AA.YYYY");
        instructionText.ShouldContain("Sadece güncelleme modu");
        instructionText.ShouldContain("20.000");

        var examples = workbook.Worksheet("Örnekler");
        examples.Row(1).Cell(1).GetString().ShouldBe("Ad");
        var exampleRows = examples.RowsUsed().Skip(1).ToList();
        exampleRows.Count.ShouldBe(5);
        exampleRows[0].Cell(1).Style.Fill.BackgroundColor.ShouldBe(XLColor.FromHtml("#C6EFCE"));
        exampleRows[2].Cell(1).Style.Fill.BackgroundColor.ShouldBe(XLColor.FromHtml("#FFC7CE"));
        exampleRows[2].Cell(3).GetString().ShouldBe("12345678901");
    }

    [Fact]
    public async Task Template_IsLocalizedByLangAndRereadWithKeyRow()
    {
        var bytes = await DownloadAsync("en");
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            workbook.Worksheets.Select(w => w.Name).ShouldBe(["Employees", "Instructions", "Examples", "Lists"]);
            workbook.Worksheet(1).Cell(1, 3).GetString().ShouldBe("National ID");
        }

        var sheet = new SpreadsheetReader().Read(bytes, ImportFileFormat.Xlsx, 10).Value;
        sheet.Headers[0].ShouldBe("First name");
        var catalog = Catalog();
        var columns = await catalog.GetColumnsAsync(Template(), ImportCultures.Turkish, CancellationToken.None);
        ColumnAutoMapper.IsKeyRow(sheet.Rows.First(), columns.Select(c => c.Column.Key)).ShouldBeTrue();

        // English headers auto-map even when a Turkish request culture analyzes the file.
        var mapped = ColumnAutoMapper.BuildSourceColumns(sheet, hasKeyRow: false, columns);
        mapped.Single(c => c.Header == "National ID").SuggestedTargetKey.ShouldBe("nationalId");
    }

    [Fact]
    public async Task TemplateList_ExposesColumnsRequirementsAndOptions()
    {
        var handler = new ListImportTemplatesHandler(Catalog());
        var templates = (await handler.Handle(new ListImportTemplatesQuery("tr"), CancellationToken.None)).Value;

        var template = templates.ShouldHaveSingleItem();
        template.Code.ShouldBe("employee.master");
        template.Name.ShouldBe("Çalışanlar");
        template.SupportedModes.ShouldBe(["Upsert", "UpdateOnly"]);
        var nationalId = template.Columns.Single(c => c.Key == "nationalId");
        (nationalId.Header, nationalId.DataType, nationalId.Requirement, nationalId.IsMatchKey).ShouldBe(("Kimlik numarası", "NationalId", "Always", true));
        template.Columns.Single(c => c.Key == "orgUnit:Department").Options.ShouldBe(["AR-GE", "Satış"]);
        template.Columns.Single(c => c.Key == "hireDate").Requirement.ShouldBe("Optional");
    }

    [Fact]
    public void ResultWorkbook_AppendsOutcomeAndErrorColumnsKeepingRowNumbers()
    {
        var bytes = new ImportWorkbookWriter().BuildResult(new ResultWorkbookModel(
            "Sonuç",
            ["Ad", "Kimlik numarası"],
            [new ImportCell("firstName"), new ImportCell("nationalId")],
            [
                new ResultWorkbookRow(3, [new ImportCell("Ali"), new ImportCell("10000000146")], "Eklendi", null, false),
                new ResultWorkbookRow(5, [new ImportCell("Veli"), new ImportCell("123")], "Hata", "Kimlik numarası: Geçersiz", true),
            ],
            "Sonuç",
            "Hata açıklaması"));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        sheet.Row(1).CellsUsed().Select(c => c.GetString()).ShouldBe(["Ad", "Kimlik numarası", "Sonuç", "Hata açıklaması"]);
        sheet.Row(2).IsHidden.ShouldBeTrue();
        sheet.Cell(3, 3).GetString().ShouldBe("Eklendi");
        sheet.Cell(5, 4).GetString().ShouldBe("Kimlik numarası: Geçersiz");
        sheet.Cell(5, 4).Style.Fill.BackgroundColor.ShouldBe(XLColor.FromHtml("#FFC7CE"));
    }

    [Fact]
    public void SheetName_IsSanitizedForExcel() =>
        ImportWorkbookWriter.SheetName("Çalışanlar [2026]: a/b?*" + new string('x', 40)).Length.ShouldBe(31);

    private static async Task<byte[]> DownloadAsync(string lang)
    {
        var handler = new DownloadImportTemplateHandler(Catalog(), new ImportWorkbookWriter());
        var result = await handler.Handle(new DownloadImportTemplateQuery("employee.master", lang), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("employee.master.xlsx");
        return result.Value.Content;
    }

    private static EmployeeMasterImportTemplate Template()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ListActiveTypesAsync(TenantId, Arg.Any<CancellationToken>()).Returns([CompanyType, DepartmentType]);
        var company = new OrgUnitInfo(Guid.NewGuid(), CompanyType.Id, null, "Acme");
        orgUnits.ListActiveUnitsAsync(TenantId, Arg.Any<CancellationToken>()).Returns(
        [
            company,
            new OrgUnitInfo(Guid.NewGuid(), DepartmentType.Id, company.Id, "Satış"),
            new OrgUnitInfo(Guid.NewGuid(), DepartmentType.Id, company.Id, "AR-GE"),
            new OrgUnitInfo(Guid.NewGuid(), DepartmentType.Id, company.Id, "satış"),
        ]);
        return new EmployeeMasterImportTemplate(tenant, orgUnits);
    }

    private static ImportTemplateCatalog Catalog() =>
        new([Template()], [], new ResourceImportLocalizer(), Substitute.For<ICurrentUser>(), Substitute.For<IPermissionService>());
}

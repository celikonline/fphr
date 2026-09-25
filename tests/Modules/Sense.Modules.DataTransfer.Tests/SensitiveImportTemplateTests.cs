using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sense.Modules.DataTransfer.Api;
using Sense.Modules.DataTransfer.Application.ImportJobs;
using Sense.Modules.DataTransfer.Infrastructure.Persistence;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.DataTransfer.Tests;

/// <summary>
/// Gizli sütunlu şablonlar (ör. Attendance "terminal-credentials": kart/PIN) gerçek PostgreSQL'de: örnek değer saklanmaz, yüklenen dosya iş
/// koşunca silinir, sonuç dosyası yalnız satır sonucunu taşır; şablon hazır değilse (sunucu sırrı yok) yükleme/eşleme/çalıştırma tek seferde 503
/// döner ve hiçbir satır modül işleyicisine gitmez.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class SensitiveImportTemplateTests : TestFixture
{
    private static readonly string[] Secrets = ["48291573", "04A1B2C3D4", "BAD-SECRET-77"];

    private readonly SecretTemplate _template = new();
    private readonly SecretRowHandler _handler = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new DataTransferModule().AddModule(services, configuration);
        services.AddSingleton<IPermissionService>(new FakePermissions());
        services.AddSingleton<IDataScopeService>(new FakeDataScope());
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<IImportTemplate>(_template);
        services.AddSingleton<IImportRowHandler>(_handler);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<DataTransferDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Result_file_contains_no_credential_values()
    {
        SetUser();
        var job = await UploadAsync(File(("S-1", Secrets[0]), ("S-2", Secrets[1]), ("S-3", Secrets[2])));
        job.SourceColumns.ShouldAllBe(c => c.Samples.Count == 0);
        job.SourceColumns.Select(c => c.SuggestedTargetKey).ShouldBe([SecretTemplate.EmployeeKey, SecretTemplate.SecretKey]);

        await MapAsync(job);
        var run = (await ExecuteCommandAsync(new RunImportJobCommand(job.Id))).Value;

        (run.Status, run.InsertedCount, run.ErrorCount).ShouldBe(("CompletedWithErrors", 2, 1));
        _handler.ReceivedSecrets.ShouldBe(Secrets);
        var db = ServiceProvider!.GetRequiredService<DataTransferDbContext>();
        (await db.ImportSourceFiles.AsNoTracking().AnyAsync(f => f.ImportJobId == job.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();

        var result = (await ExecuteQueryAsync(new DownloadImportResultQuery(job.Id, "tr"))).Value;
        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var cells = workbook.Worksheets.SelectMany(s => s.CellsUsed()).Select(c => c.GetString()).ToList();
        var sheet = workbook.Worksheet(1);
        sheet.Row(1).CellsUsed().Select(c => c.GetString()).ShouldBe(["Çalışan no", "Sonuç", "Hata açıklaması"]);
        (sheet.Cell(2, 1).GetString(), sheet.Cell(2, 2).GetString()).ShouldBe(("S-1", "Eklendi"));
        (sheet.Cell(4, 1).GetString(), sheet.Cell(4, 2).GetString()).ShouldBe(("S-3", "Hata"));
        sheet.Cell(4, 3).GetString().ShouldStartWith("Gizli değer:");

        var rows = (await ExecuteQueryAsync(new ListImportJobRowsQuery(job.Id, null, new PagedQuery { PageSize = 10 }))).Value;
        var stored = await DataTransferRowsAsJsonAsync();
        var echoed = string.Join('|', cells) + JsonSerializer.Serialize(run) + JsonSerializer.Serialize(job) + JsonSerializer.Serialize(rows) + stored;
        foreach (var secret in Secrets)
        {
            echoed.ShouldNotContain(secret);
        }
    }

    [Fact]
    public async Task Pepper_missing_returns_503_before_any_row()
    {
        var tenantId = SetUser();
        _template.Ready = false;

        var upload = await ExecuteCommandAsync(new CreateImportJobCommand(SecretTemplate.Code, "kimlik.xlsx", File(("S-1", Secrets[0]))));

        upload.Error.ShouldBe(SecretTemplate.NotReady);
        (await ServiceProvider!.GetRequiredService<DataTransferDbContext>().ImportJobs.AsNoTracking().AnyAsync(j => j.TenantId == tenantId, TestContext.Current.CancellationToken)).ShouldBeFalse();

        _template.Ready = true;
        var job = await UploadAsync(File(("S-1", Secrets[0])));
        var mapping = job.SourceColumns.Select(c => new ColumnMappingDto(c.Index, c.SuggestedTargetKey)).ToList();
        _template.Ready = false;

        (await ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, mapping, "Upsert", "SkipInvalidRows"))).Error.ShouldBe(SecretTemplate.NotReady);
        _template.Ready = true;
        await MapAsync(job);
        _template.Ready = false;

        var run = await ExecuteCommandAsync(new RunImportJobCommand(job.Id));

        run.Error.Type.ShouldBe(ErrorType.ServiceUnavailable);
        _handler.ReceivedSecrets.ShouldBeEmpty();
        (await ExecuteQueryAsync(new GetImportJobQuery(job.Id))).Value.Status.ShouldBe("Mapped");
    }

    private async Task<ImportJobDto> UploadAsync(byte[] content)
    {
        var result = await ExecuteCommandAsync(new CreateImportJobCommand(SecretTemplate.Code, "kimlik.xlsx", content));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private async Task MapAsync(ImportJobDto job)
    {
        var mapping = job.SourceColumns.Select(c => new ColumnMappingDto(c.Index, c.SuggestedTargetKey)).ToList();
        var result = await ExecuteCommandAsync(new SaveImportJobMappingCommand(job.Id, mapping, "Upsert", "SkipInvalidRows"));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
    }

    private async Task<string> DataTransferRowsAsJsonAsync()
    {
        await using var connection = new NpgsqlConnection(ServiceProvider!.GetRequiredService<IConfiguration>().GetConnectionString("Database"));
        await connection.OpenAsync();
        var all = new List<string>();
        foreach (var table in new[] { "import_jobs", "import_job_rows" })
        {
            await using var command = new NpgsqlCommand($"SELECT coalesce(string_agg(row_to_json(t)::text, '|'), '') FROM datatransfer.{table} t", connection);
            all.Add((string)(await command.ExecuteScalarAsync())!);
        }

        return string.Join('|', all);
    }

    private static byte[] File(params (string Employee, string Secret)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Veri");
        sheet.Cell(1, 1).Value = SecretTemplate.EmployeeHeader;
        sheet.Cell(1, 2).Value = SecretTemplate.SecretHeader;
        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = rows[i].Employee;
            sheet.Cell(i + 2, 2).Value = rows[i].Secret;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private Guid SetUser()
    {
        var tenantId = Guid.NewGuid();
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: null, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
        return tenantId;
    }

    /// <summary>İki sütunlu test şablonu: çalışan no (eşleştirme anahtarı) + gizli değer; <see cref="Ready"/> sunucu sırrının varlığını taklit eder.</summary>
    private sealed class SecretTemplate : IImportTemplate, IImportTemplateReadiness
    {
        public const string Code = "test.secret";
        public const string EmployeeKey = "employeeNo";
        public const string SecretKey = "secret";
        public const string EmployeeHeader = "Çalışan no";
        public const string SecretHeader = "Gizli değer";

        public static readonly Error NotReady = Error.ServiceUnavailable("test.secret.not_configured");

        public bool Ready { get; set; } = true;

        string IImportTemplate.Code => Code;

        public string NameKey => "test.secret.name";

        public string DescriptionKey => "test.secret.description";

        public string RequiredPermission => "test.secret.import";

        public IReadOnlyList<ImportMode> SupportedModes { get; } = [ImportMode.Upsert];

        public IReadOnlyList<string> InstructionKeys { get; } = [];

        public Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImportColumn>>(
            [
                new(EmployeeKey, null, EmployeeHeader, ImportColumnDataType.Text, ImportColumnRequirement.Always, IsMatchKey: true),
                new(SecretKey, null, SecretHeader, ImportColumnDataType.Text, ImportColumnRequirement.Always, IsSensitive: true),
            ]);

        public IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns) => [];

        public Result CheckReady() => Ready ? Result.Success() : NotReady;
    }

    /// <summary>Her satırı ekler; "BAD-" ile başlayan gizli değer satır hatasıdır (argümansız, değer yok).</summary>
    private sealed class SecretRowHandler : IImportRowHandler
    {
        public List<string> ReceivedSecrets { get; } = [];

        public string TemplateCode => SecretTemplate.Code;

        public Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default)
        {
            var results = batch.Rows.Select(row =>
            {
                var secret = row.Get(SecretTemplate.SecretKey)!.Text!;
                ReceivedSecrets.Add(secret);
                var employee = row.Get(SecretTemplate.EmployeeKey)?.Text;
                return secret.StartsWith("BAD-", StringComparison.Ordinal)
                    ? new ImportRowResult(row.RowNumber, ImportRowOutcome.Error, employee, null, [new ImportRowError(SecretTemplate.SecretKey, ImportErrorCodes.RequiredMissing)])
                    : new ImportRowResult(row.RowNumber, ImportRowOutcome.Inserted, employee, Guid.NewGuid(), []);
            }).ToList();
            return Task.FromResult(new ImportBatchResult(results, Applied: true));
        }
    }
}

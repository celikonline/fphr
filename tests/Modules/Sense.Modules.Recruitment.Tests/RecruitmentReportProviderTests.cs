using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.Tags;
using Sense.Modules.Recruitment.Infrastructure;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Modules.Recruitment.Infrastructure.Reporting;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Export;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// Raporlar menüsü katkıları (R5, roadmap #94/#96, faz6-durum-takibi.md) on real PostgreSQL:
/// <c>recruitment.daily_counts</c> and <c>recruitment.candidates_export</c> read through RecruitmentDbContext, and
/// the produced Excel content is read back with ClosedXML (mirrors LeaveReportProviderTests). A minimal
/// registration - just the DbContext + module handlers + the two providers - no Organization/Identity ports are
/// needed since neither provider resolves org unit or location names (unlike the public career-site queries in
/// RecruitmentIntegrationTests).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class RecruitmentReportProviderTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<RecruitmentDbContext>(configuration, RecruitmentDbContext.SchemaName);
        services.AddModuleHandlers(
            RecruitmentDbContext.SchemaName,
            typeof(RecruitmentErrors).Assembly,
            typeof(RecruitmentDbContext).Assembly,
            typeof(RecruitmentPermissions).Assembly);
        services.AddRecruitmentCoreServices();

        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.TryAddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddScoped<IReportProvider, RecruitmentDailyCountsReportProvider>();
        services.AddScoped<IReportProvider, RecruitmentCandidatesExportReportProvider>();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<RecruitmentDbContext>().Database.MigrateAsync();

    private IReportProvider Provider(string key)
    {
        var scope = ServiceProvider!.CreateScope();
        return scope.ServiceProvider.GetServices<IReportProvider>().Single(p => p.Key == key);
    }

    private static IXLWorksheet FirstSheet(ReportFileResult file)
    {
        var workbook = new XLWorkbook(new MemoryStream(file.Content));
        return workbook.Worksheets.First();
    }

    private static string? Cell(IXLWorksheet sheet, int row, string header)
    {
        var headerCell = sheet.Row(1).CellsUsed().First(c => c.GetString() == header);
        var value = sheet.Cell(row, headerCell.Address.ColumnNumber);
        return value.IsEmpty() ? null : value.GetFormattedString();
    }

    private async Task<Guid> CreatePostingAsync(Guid tenantId, string title)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var posting = JobPosting.Create(tenantId, title, Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, "Açıklama", Guid.NewGuid(), null).Value;
        posting.Publish();
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync();
        return posting.Id;
    }

    private async Task<Guid> CreateCandidateAsync(Guid tenantId, string first, string last, string email, CandidateSource source = CandidateSource.Manual)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(tenantId, first, last, email, null, source: source).Value;
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private async Task CreateApplicationAsync(Guid tenantId, Guid postingId, Guid candidateId, ApplicationStage stage = ApplicationStage.Applied)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var application = JobApplication.Create(tenantId, postingId, candidateId);
        if (stage != ApplicationStage.Applied)
        {
            application.MoveStage(stage);
        }

        db.JobApplications.Add(application);
        await db.SaveChangesAsync();
    }

    /// <summary>Backdates a candidate's created_at past a filter window - mirrors RecruitmentIntegrationTests'
    /// BackdateCandidateCreatedAtAsync (the audit interceptor refuses a normal EF update to CreatedAt).</summary>
    private async Task BackdateCandidateCreatedAtAsync(Guid tenantId, Guid candidateId, DateTime date)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE recruitment.candidates SET created_at = {date} WHERE id = {candidateId} AND tenant_id = {tenantId}");
    }

    [Fact]
    public async Task DailyCounts_CountsApplicationsAndCandidates_PerDay_IncludingZeroDays()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // headers/enum texts asserted in Turkish
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        var postingId = await CreatePostingAsync(tenantId, "Backend Geliştirici");
        var candidateA = await CreateCandidateAsync(tenantId, "Ada", "Yılmaz", "ada@example.com");
        var candidateB = await CreateCandidateAsync(tenantId, "Ela", "Kaya", "ela@example.com");
        await CreateApplicationAsync(tenantId, postingId, candidateA);
        await CreateApplicationAsync(tenantId, postingId, candidateB);

        var today = DateOnly.FromDateTime(Clock!.GetUtcNow().UtcDateTime);
        var yesterday = today.AddDays(-1);

        var file = await Provider(RecruitmentDailyCountsReportProvider.ReportKey).GenerateAsync(
            tenantId,
            new Dictionary<string, string> { [ReportParameterKeys.From] = yesterday.ToString("yyyy-MM-dd"), [ReportParameterKeys.To] = today.ToString("yyyy-MM-dd") },
            CancellationToken.None);

        var sheet = FirstSheet(file);
        // Row 2 = yesterday (zero), Row 3 = today (2 applications, 2 candidates).
        Cell(sheet, 2, "Tarih").ShouldNotBeNull();
        Cell(sheet, 2, "Yeni başvuru").ShouldBe("0");
        Cell(sheet, 2, "Yeni eklenen aday").ShouldBe("0");
        Cell(sheet, 3, "Yeni başvuru").ShouldBe("2");
        Cell(sheet, 3, "Yeni eklenen aday").ShouldBe("2");
    }

    [Fact]
    public async Task DailyCounts_ExcludesOtherTenantsData()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // headers/enum texts asserted in Turkish
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var today = DateOnly.FromDateTime(Clock!.GetUtcNow().UtcDateTime);

        using (TenantContextSetter!.BeginScope(tenantA))
        {
            var postingA = await CreatePostingAsync(tenantA, "Tenant A İlanı");
            var candidateA = await CreateCandidateAsync(tenantA, "A", "Aday", "a@example.com");
            await CreateApplicationAsync(tenantA, postingA, candidateA);
        }

        using (TenantContextSetter!.BeginScope(tenantB))
        {
            var postingB = await CreatePostingAsync(tenantB, "Tenant B İlanı");
            var candidateB1 = await CreateCandidateAsync(tenantB, "B1", "Aday", "b1@example.com");
            var candidateB2 = await CreateCandidateAsync(tenantB, "B2", "Aday", "b2@example.com");
            await CreateApplicationAsync(tenantB, postingB, candidateB1);
            await CreateApplicationAsync(tenantB, postingB, candidateB2);
        }

        ReportFileResult file;
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            file = await Provider(RecruitmentDailyCountsReportProvider.ReportKey).GenerateAsync(
                tenantA,
                new Dictionary<string, string> { [ReportParameterKeys.From] = today.ToString("yyyy-MM-dd"), [ReportParameterKeys.To] = today.ToString("yyyy-MM-dd") },
                CancellationToken.None);
        }

        var sheet = FirstSheet(file);
        Cell(sheet, 2, "Yeni başvuru").ShouldBe("1");
        Cell(sheet, 2, "Yeni eklenen aday").ShouldBe("1");
    }

    [Fact]
    public async Task CandidatesExport_RowContainsExpectedContactTagsSourceAndStage()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // headers/enum texts asserted in Turkish
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        Guid tagId;
        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var tag = Tag.Create(tenantId, "Senior").Value;
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            tagId = tag.Id;
        }

        var postingId = await CreatePostingAsync(tenantId, "Backend Geliştirici");

        Guid candidateId;
        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var candidate = Candidate.Create(tenantId, "Ada", "Yılmaz", "ada.yilmaz@example.com", "+905551112233", source: CandidateSource.CareerSite).Value;
            candidate.SetTags([tagId]);
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync();
            candidateId = candidate.Id;
        }

        await CreateApplicationAsync(tenantId, postingId, candidateId, ApplicationStage.Interview);

        var file = await Provider(RecruitmentCandidatesExportReportProvider.ReportKey).GenerateAsync(
            tenantId, new Dictionary<string, string>(), CancellationToken.None);

        var sheet = FirstSheet(file);
        Cell(sheet, 2, "E-posta").ShouldBe("ada.yilmaz@example.com");
        Cell(sheet, 2, "Telefon").ShouldBe("+905551112233");
        Cell(sheet, 2, "Etiketler").ShouldBe("Senior");
        Cell(sheet, 2, "Kaynak").ShouldBe("Kariyer sitesi");
        Cell(sheet, 2, "Başvurular").ShouldBe("Backend Geliştirici (Mülakat)");
    }

    [Fact]
    public async Task CandidatesExport_DateRangeFilter_ExcludesCandidatesOutsideRange()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // headers/enum texts asserted in Turkish
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        var recentId = await CreateCandidateAsync(tenantId, "Yeni", "Aday", "yeni@example.com");
        var oldId = await CreateCandidateAsync(tenantId, "Eski", "Aday", "eski@example.com");
        await BackdateCandidateCreatedAtAsync(tenantId, oldId, DateTime.UtcNow.AddDays(-30));

        var today = DateOnly.FromDateTime(Clock!.GetUtcNow().UtcDateTime);
        var file = await Provider(RecruitmentCandidatesExportReportProvider.ReportKey).GenerateAsync(
            tenantId,
            new Dictionary<string, string> { [ReportParameterKeys.From] = today.ToString("yyyy-MM-dd"), [ReportParameterKeys.To] = today.ToString("yyyy-MM-dd") },
            CancellationToken.None);

        var sheet = FirstSheet(file);
        var emails = sheet.RowsUsed().Skip(1).Select(r => Cell(sheet, r.RowNumber(), "E-posta")).ToList();
        emails.ShouldContain("yeni@example.com");
        emails.ShouldNotContain("eski@example.com");
        _ = recentId;
    }

    [Fact]
    public async Task CandidatesExport_ExcludesOtherTenantsCandidates()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // headers/enum texts asserted in Turkish
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (TenantContextSetter!.BeginScope(tenantA))
        {
            await CreateCandidateAsync(tenantA, "A", "Aday", "a-export@example.com");
        }

        using (TenantContextSetter!.BeginScope(tenantB))
        {
            await CreateCandidateAsync(tenantB, "B", "Aday", "b-export@example.com");
        }

        ReportFileResult file;
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            file = await Provider(RecruitmentCandidatesExportReportProvider.ReportKey).GenerateAsync(
                tenantA, new Dictionary<string, string>(), CancellationToken.None);
        }

        var sheet = FirstSheet(file);
        var emails = sheet.RowsUsed().Skip(1).Select(r => Cell(sheet, r.RowNumber(), "E-posta")).ToList();
        emails.ShouldContain("a-export@example.com");
        emails.ShouldNotContain("b-export@example.com");
    }
}

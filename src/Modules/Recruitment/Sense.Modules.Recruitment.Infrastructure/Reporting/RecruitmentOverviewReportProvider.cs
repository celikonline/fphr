using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Recruitment.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "ATS raporları" → Genel bakış (R5, roadmap #94, faz6-durum-takibi.md): one row per job posting with
/// the number of its applications currently sitting in each pipeline stage ("aşamada bekleyen") plus a total -
/// the hiring funnel at a glance. Optional AppliedAt date range narrows which applications are counted. The Kolay
/// İK tab's time-based figures (average stage transition / posting close / contact time) and interview/feedback
/// distributions are NOT included: <see cref="JobApplication"/> only stores the current stage, and interviews/
/// feedback forms are not modeled yet (R2b), so those numbers cannot be computed truthfully.
/// </summary>
public sealed class RecruitmentOverviewReportProvider(
    RecruitmentDbContext context,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "recruitment.overview";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Administrative;

    public string? RequiredPermission => RecruitmentPermissions.ApplicationsRead.Key;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var range = ReportParameters.GetDateRange(parameters);

        var applicationsQuery = context.JobApplications.AsNoTracking().Where(a => a.TenantId == tenantId);
        if (range is { } r)
        {
            // Npgsql needs Kind=Utc for timestamptz comparisons (see RecruitmentDailyCountsReportProvider).
            var rangeStart = DateTime.SpecifyKind(r.From.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var rangeEndExclusive = DateTime.SpecifyKind(r.To.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            applicationsQuery = applicationsQuery.Where(a => a.AppliedAt >= rangeStart && a.AppliedAt < rangeEndExclusive);
        }

        var stageCounts = await applicationsQuery
            .GroupBy(a => new { a.JobPostingId, a.Stage })
            .Select(g => new { g.Key.JobPostingId, g.Key.Stage, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var postingIds = stageCounts.Select(s => s.JobPostingId).Distinct().ToList();
        var postingTitles = postingIds.Count == 0
            ? []
            : await context.JobPostings.AsNoTracking()
                .Where(p => p.TenantId == tenantId && postingIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Title })
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var stages = Enum.GetValues<ApplicationStage>();
        var columns = new List<ExportColumn> { new(localizer.Column("jobPosting")) };
        columns.AddRange(stages.Select(stage => new ExportColumn(localizer.EnumText<ApplicationStage>(stage) ?? stage.ToString(), ExportColumnType.Number)));
        columns.Add(new ExportColumn(localizer.Column("totalApplications"), ExportColumnType.Number));

        var rows = stageCounts
            .GroupBy(s => s.JobPostingId)
            .Select(g => (Title: postingTitles.GetValueOrDefault(g.Key) ?? string.Empty, Counts: g.ToDictionary(x => x.Stage, x => x.Count)))
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x =>
            {
                var row = new List<object?> { x.Title };
                row.AddRange(stages.Select(stage => (object?)x.Counts.GetValueOrDefault(stage)));
                row.Add(x.Counts.Values.Sum());
                return (IReadOnlyList<object?>)row;
            })
            .ToList();

        var sheet = localizer.Sheet("recruitmentOverview");
        var file = exporter.ExportToExcel($"{sheet} {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, rows)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

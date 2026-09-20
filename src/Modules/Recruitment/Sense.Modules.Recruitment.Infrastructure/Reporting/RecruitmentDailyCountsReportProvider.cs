using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Recruitment.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "ATS raporları" → Günlük sayılar (R5, roadmap #94, faz6-durum-takibi.md): for every calendar day in
/// the requested range, how many new applications came in and how many new candidates were added (any source -
/// manual entry or the public career site). One row per day, including zero-count days, so a trend chart on the
/// web side never has to fill gaps itself. Contacted/stage-distribution columns from the Kolay İK "Overview" tab
/// are NOT included here - <see cref="JobApplication"/> only tracks the current stage, not a stage-change history,
/// so "aşamada bekleyen"/"ortalama geçiş süresi" numbers are deferred until that history exists (see status doc).
/// </summary>
public sealed class RecruitmentDailyCountsReportProvider(
    RecruitmentDbContext context,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "recruitment.daily_counts";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Administrative;

    public string? RequiredPermission => RecruitmentPermissions.ApplicationsRead.Key;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange, IsRequired: true),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        // Validated upstream (ReportParameterValidation) - a missing range here means a caller bypassed the command pipeline.
        var (from, to) = ReportParameters.GetDateRange(parameters)
            ?? throw new InvalidOperationException("Recruitment daily counts report requires a from/to date range.");

        // Npgsql requires Kind=Utc for a "timestamp with time zone" column (AppliedAt/CreatedAt are always stored UTC) -
        // DateOnly.ToDateTime always produces Kind=Unspecified, which Npgsql rejects outright rather than assuming UTC.
        var rangeStart = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var rangeEndExclusive = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var applicationDates = await context.JobApplications.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AppliedAt >= rangeStart && a.AppliedAt < rangeEndExclusive)
            .Select(a => a.AppliedAt)
            .ToListAsync(cancellationToken);
        var applicationCountsByDay = applicationDates
            .GroupBy(d => DateOnly.FromDateTime(d))
            .ToDictionary(g => g.Key, g => g.Count());

        var candidateDates = await context.Candidates.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CreatedAt >= rangeStart && c.CreatedAt < rangeEndExclusive)
            .Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        var candidateCountsByDay = candidateDates
            .GroupBy(d => DateOnly.FromDateTime(d))
            .ToDictionary(g => g.Key, g => g.Count());

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("date"), ExportColumnType.Date),
            new(localizer.Column("newApplications"), ExportColumnType.Number),
            new(localizer.Column("newCandidates"), ExportColumnType.Number),
        };

        var rows = new List<IReadOnlyList<object?>>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            rows.Add(
            [
                day,
                applicationCountsByDay.GetValueOrDefault(day),
                candidateCountsByDay.GetValueOrDefault(day),
            ]);
        }

        var sheet = localizer.Sheet("recruitmentDailyCounts");
        var file = exporter.ExportToExcel($"{sheet} {from:yyyy-MM-dd} {to:yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, rows)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

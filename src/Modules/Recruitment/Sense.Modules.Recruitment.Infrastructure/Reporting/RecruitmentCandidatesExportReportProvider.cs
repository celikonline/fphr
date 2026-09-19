using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Recruitment.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "ATS raporları" → Adaylar dışa aktarımı (R5, roadmap #94/#96, faz6-durum-takibi.md): every candidate -
/// contact info, source, tags (roadmap #89), and, for each posting applied to, its current pipeline stage. The
/// Kolay İK doc's "aşama geçmişi" (full stage-change history) variant is NOT modeled - <see cref="JobApplication"/>
/// only stores the current <see cref="ApplicationStage"/>, no per-transition timestamps exist yet - so each
/// application contributes its single current stage, not a history. Filter: optional CreatedAt date range (unlike
/// the daily-counts report, a range here is a narrowing filter, not a required parameter - "export everything" is a
/// valid, common request for this report).
/// </summary>
public sealed class RecruitmentCandidatesExportReportProvider(
    RecruitmentDbContext context,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "recruitment.candidates_export";

    private const string ApplicationSeparator = "; ";
    private const string TagSeparator = ", ";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Administrative;

    public string? RequiredPermission => RecruitmentPermissions.CandidatesRead.Key;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var range = ReportParameters.GetDateRange(parameters);

        var candidatesQuery = context.Candidates.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (range is { } r)
        {
            // See RecruitmentDailyCountsReportProvider's identical comment: Npgsql needs Kind=Utc explicitly.
            var rangeStart = DateTime.SpecifyKind(r.From.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var rangeEndExclusive = DateTime.SpecifyKind(r.To.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            candidatesQuery = candidatesQuery.Where(c => c.CreatedAt >= rangeStart && c.CreatedAt < rangeEndExclusive);
        }

        var candidates = await candidatesQuery.OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);

        var tagNames = await context.Tags.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var applications = candidateIds.Count == 0
            ? []
            : await context.JobApplications.AsNoTracking()
                .Where(a => a.TenantId == tenantId && candidateIds.Contains(a.CandidateId))
                .Select(a => new { a.CandidateId, a.JobPostingId, a.Stage })
                .ToListAsync(cancellationToken);
        var postingTitles = await context.JobPostings.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => new { p.Id, p.Title })
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);
        var applicationsByCandidate = applications.ToLookup(a => a.CandidateId);

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
            new(localizer.Column("email")),
            new(localizer.Column("phone")),
            new(localizer.Column("source")),
            new(localizer.Column("tags")),
            new(localizer.Column("applications")),
            new(localizer.Column("createdAt"), ExportColumnType.DateTime),
        };

        var rows = new List<IReadOnlyList<object?>>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var tags = string.Join(TagSeparator, candidate.Tags
                .Select(t => tagNames.GetValueOrDefault(t.TagId))
                .Where(name => name is not null));

            var applicationSummaries = applicationsByCandidate[candidate.Id]
                .Select(a => $"{postingTitles.GetValueOrDefault(a.JobPostingId)} ({localizer.EnumText<ApplicationStage>(a.Stage)})");

            rows.Add(
            [
                candidate.FirstName,
                candidate.LastName,
                candidate.Email,
                candidate.Phone,
                localizer.EnumText<CandidateSource>(candidate.Source),
                tags,
                string.Join(ApplicationSeparator, applicationSummaries),
                candidate.CreatedAt,
            ]);
        }

        var sheet = localizer.Sheet("recruitmentCandidatesExport");
        var file = exporter.ExportToExcel($"{sheet} {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, rows)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}

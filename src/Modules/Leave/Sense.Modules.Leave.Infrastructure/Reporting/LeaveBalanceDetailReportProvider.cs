using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>Annual leave details: tenant-local current columns and optional monthly historical columns.</summary>
public sealed class LeaveBalanceDetailReportProvider(
    LeaveDbContext context, IDataScopeService dataScope, IOrgUnitLookup orgUnits, IEmployeeReportReader employees,
    IReportExporter exporter, ISimplePdfWriter pdf, IStringLocalizer<SharedResource> localizer, TimeProvider clock,
    ICurrentUser user, IPermissionService permissions, ITenantTimeZoneLookup timeZones, ILeaveDayCalculator days,
    IReportGenerationContext? generation = null) : IReportProvider
{
    public const string ReportKey = "leave.balance.detail";
    public string Key => ReportKey;
    public string ExecutionMode => "async";
    public string CategoryKey => ReportCategories.Administrative;
    public string? RequiredPermission => LeavePermissions.BalancesRead.Key;
    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.Month, ReportParameterKind.Month),
        new(ReportParameterKeys.Year, ReportParameterKind.Year),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
        new("format", ReportParameterKind.Select, Options: ["xlsx", "pdf"]),
    ];

    private static readonly (string Key, ExportColumnType Type)[] CurrentColumns =
    [
        ("hireDate", ExportColumnType.Date), ("leaveAccrualStartDate", ExportColumnType.Date),
        ("extraGrantDays", ExportColumnType.Number), ("totalEntitledDays", ExportColumnType.Number),
        ("carryOverLossDays", ExportColumnType.Number), ("carriedOverDays", ExportColumnType.Number),
        ("availableCarriedOverDays", ExportColumnType.Number), ("lastPeriodAccruedDays", ExportColumnType.Number),
        ("currentPeriod", ExportColumnType.Text), ("currentPeriodOpeningBalance", ExportColumnType.Number),
        ("currentPeriodUsedDays", ExportColumnType.Number), ("totalUsedDays", ExportColumnType.Number),
        ("currentBalance", ExportColumnType.Number), ("currentPeriodFutureApprovedDays", ExportColumnType.Number),
        ("currentPeriodAvailableBalance", ExportColumnType.Number), ("nextAccrualDate", ExportColumnType.Date),
        ("nextPeriodApprovedDays", ExportColumnType.Number), ("employmentStatus", ExportColumnType.Text),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        // A missing user must never turn a background system principal into an unrestricted export.
        if (user.UserId is not { } userId)
        {
            throw new UnauthorizedAccessException();
        }
        var sensitive = await permissions.HasAsync(userId, "employee.sensitive.view", cancellationToken)
            && (generation?.IsBackground != true || generation.CanViewSensitive);
        var zoneId = await timeZones.FindTimeZoneIdAsync(tenantId, cancellationToken);
        var zone = ResolveTimeZone(zoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        var range = ReportParameters.ResolveMonthYearRange(parameters, today);
        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, LeavePermissions.BalancesRead.Key, parameters, cancellationToken);
        if (generation?.IsBackground == true && generation.VisibleEmployeeIds is { } ceiling)
        {
            rows = rows.Where(r => ceiling.Contains(r.EmployeeId)).ToList();
        }
        var ids = rows.Select(r => r.EmployeeId).ToList();
        var profiles = await context.Profiles.AsNoTracking().Where(p => p.TenantId == tenantId && ids.Contains(p.EmployeeId)).ToListAsync(cancellationToken);
        var profileById = profiles.ToDictionary(p => p.EmployeeId);
        var ruleSets = await context.RuleSets.AsNoTracking().Where(s => s.TenantId == tenantId).ToListAsync(cancellationToken);
        var types = ruleSets.ToDictionary(s => s.Id, s => s.Types.FirstOrDefault(t => t.Code == LeaveTypeCode.Annual && t.HasBalance && !t.IsDisabled));
        var typeIds = types.Values.Where(t => t is not null).Select(t => t!.Id).ToList();
        var ledger = await context.LedgerEntries.AsNoTracking().Where(e => e.TenantId == tenantId && ids.Contains(e.EmployeeId) && typeIds.Contains(e.LeaveTypeId)).ToListAsync(cancellationToken);
        var requests = range is { } r
            ? await context.Requests.AsNoTracking().Where(q => q.TenantId == tenantId && ids.Contains(q.EmployeeId) && typeIds.Contains(q.LeaveTypeId)
                && q.StartDate <= r.To && q.EndDate >= r.From.AddDays(-6) && (q.Status == LeaveRequestStatus.Approved || q.Status == LeaveRequestStatus.Pending)).ToListAsync(cancellationToken)
            : [];
        var columns = new List<ExportColumn> { new(localizer.Column("firstName")), new(localizer.Column("lastName")) };
        if (sensitive)
        {
            columns.Add(new(localizer.Column("nationalId")));
        }
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.AddRange(CurrentColumns.Select(c => new ExportColumn(localizer.Column(c.Key), c.Type)));
        if (range is not null)
        {
            columns.Add(new(localizer.Column("range")));
            columns.AddRange(new[] { "rangeOpeningBalance", "rangeApprovedDays", "rangeClosingBalance", "rangePendingDays" }
                .Select(k => new ExportColumn(localizer.Column(k), ExportColumnType.Number)));
        }
        var data = new List<IReadOnlyList<object?>>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = new List<object?> { row.FirstName, row.LastName };
            if (sensitive)
            {
                cells.Add(row.NationalId);
            }
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            var profile = profileById.GetValueOrDefault(row.EmployeeId);
            var annual = profile is null ? null : types.GetValueOrDefault(profile.RuleSetId);
            cells.Add(profile?.HireDate ?? row.HireDate);
            cells.Add(profile?.LeaveAccrualStartDate);
            if (profile is null || annual is null)
            {
                cells.AddRange(Enumerable.Repeat<object?>(null, 15));
                cells.Add(localizer.EnumText("EmployeeStatus", row.Status));
                if (range is { } blankRange)
                {
                    cells.Add(PeriodText(blankRange.From, blankRange.To));
                    cells.AddRange(Enumerable.Repeat<object?>(null, 4));
                }
            }
            else
            {
                var entries = ledger.Where(e => e.EmployeeId == row.EmployeeId && e.LeaveTypeId == annual.Id).ToList();
                var projected = LeaveBalanceReportProjection.Project(annual, profile.LeaveAccrualStartDate, today, entries);
                var b = projected.Balance;
                cells.AddRange([projected.LifetimeExtraGrants, projected.LifetimeEntitled, projected.CarryOverLoss,
                    b.CarriedOverIntoPeriod, projected.AvailableCarriedOver, b.LastPeriodAccrued,
                    PeriodText(b.CurrentPeriodFrom, b.CurrentPeriodTo), projected.PeriodOpeningBalance,
                    b.CurrentPeriodUsed, projected.LifetimeUsed, b.CurrentBalance, b.FutureApprovedInPeriod,
                    b.AvailableBalance, b.NextAccrualDate, projected.NextPeriodApproved,
                    localizer.EnumText("EmployeeStatus", row.Status)]);
                if (range is { } period)
                {
                    decimal approved = 0, pending = 0;
                    foreach (var request in requests.Where(q => q.EmployeeId == row.EmployeeId && q.LeaveTypeId == annual.Id))
                    {
                        var counted = await DaysIntersectingAsync(tenantId, request, annual.CountsNonWorkingDays, period.From, period.To, cancellationToken);
                        if (request.Status == LeaveRequestStatus.Approved) approved += counted;
                        else pending += counted;
                    }
                    cells.AddRange([PeriodText(period.From, period.To),
                        LeaveBalanceReportProjection.BalanceAt(annual, profile.LeaveAccrualStartDate, period.From, entries, opening: true), approved,
                        LeaveBalanceReportProjection.BalanceAt(annual, profile.LeaveAccrualStartDate, period.To, entries), pending]);
                }
            }
            data.Add(cells);
        }
        var sheet = localizer.Sheet("leaveBalanceDetail");
        var table = new ExportTable(sheet, columns, data);
        var fileName = $"{sheet} {today:yyyy-MM-dd}";
        if (parameters.TryGetValue("format", out var format) && format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new ReportFileResult(fileName + ".pdf", "application/pdf", WritePdf(table, today));
        }
        var file = exporter.ExportToExcel(fileName + ".xlsx", [table], cancellationToken);
        return new(file.FileName, file.ContentType, file.Content);
    }

    private async Task<decimal> DaysIntersectingAsync(Guid tenantId, LeaveRequest request, bool countsNonWorking, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (request.StartDate == request.EndDate)
        {
            return request.StartDate >= from && request.StartDate <= to ? request.CalculatedDays : 0m;
        }
        var count = await days.CalculateAsync(tenantId, request.EmployeeId, request.StartDate, request.EndDate, countsNonWorking, ct);
        var reportable = count.Breakdown
            .Where(d => d.Counted)
            .OrderBy(d => d.Date)
            .Select(d => (d.Date, Weight: d.HalfDayHoliday ? 0.5m : 1m))
            .ToList();
        var automaticDays = reportable.Sum(d => d.Weight);
        if (reportable.Count > 0 && reportable.All(d => d.Date >= from && d.Date <= to))
        {
            return request.CalculatedDays;
        }

        var selectedDays = reportable.Where(d => d.Date >= from && d.Date <= to).Sum(d => d.Weight);
        if (automaticDays == 0m || selectedDays == 0m)
        {
            return 0m;
        }

        // On-behalf requests may carry an HR-entered day count. Split that override proportionally to the
        // calculator's reportable-day weights, with cumulative rounding so separate periods sum to the exact
        // stored total. Never use calendar-day counts: weekends/holidays/half days have their own weights.
        if (automaticDays != request.CalculatedDays)
        {
            var priorWeight = reportable.Where(d => d.Date < from).Sum(d => d.Weight);
            var throughWeight = reportable.Where(d => d.Date <= to).Sum(d => d.Weight);
            var before = decimal.Round(request.CalculatedDays * priorWeight / automaticDays, 4, MidpointRounding.AwayFromZero);
            var through = decimal.Round(request.CalculatedDays * throughWeight / automaticDays, 4, MidpointRounding.AwayFromZero);
            return through - before;
        }

        return selectedDays;
    }

    private byte[] WritePdf(ExportTable table, DateOnly today)
    {
        const int groupWidth = 6;
        var blocks = new List<SimplePdfBlock>();
        for (var start = 2; start < table.Columns.Count; start += groupWidth)
        {
            if (blocks.Count > 0) blocks.Add(new SimplePdfPageBreak());
            var indices = new[] { 0, 1 }.Concat(Enumerable.Range(start, Math.Min(groupWidth, table.Columns.Count - start))).ToArray();
            blocks.Add(new SimplePdfTable(null, indices.Select(i => table.Columns[i].Header).ToList(),
                table.Rows.Select(row => (IReadOnlyList<string>)indices.Select(i => CellText(row[i])).ToList()).ToList(),
                indices.Select((i, j) => (i, j)).Where(x => table.Columns[x.i].Type == ExportColumnType.Number).Select(x => x.j).ToList()));
        }
        return pdf.Write(new SimplePdfDocument(table.SheetName, null, blocks, today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), Landscape: true));
    }

    private static string CellText(object? value) => value switch
    {
        null => string.Empty, DateOnly date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.####", CultureInfo.CurrentCulture), _ => value.ToString() ?? string.Empty,
    };
    private static string PeriodText(DateOnly from, DateOnly to) => $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        try { return id is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}

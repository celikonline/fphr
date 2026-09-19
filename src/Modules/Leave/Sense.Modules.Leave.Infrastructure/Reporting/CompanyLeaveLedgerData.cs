using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>
/// senseik-048: bir çalışanın şirket geneli izin/fazla mesai defteri satırı. <see cref="OrgUnitId"/>/<see cref="OrgAssignments"/>
/// ham haliyle taşınır - tüketici (JSON handler tek bir "Birim yolu" metni, Excel sağlayıcısı dinamik kariyer
/// kolonları) <see cref="OrgUnitHierarchy.LevelValues"/> ile kendi biçimini üretir (LeaveBalanceSummaryReportProvider/
/// LeaveRequestsReportProvider ile aynı desen).
/// </summary>
internal sealed record CompanyLeaveLedgerRow(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    Guid? OrgUnitId,
    IReadOnlyDictionary<Guid, Guid> OrgAssignments,
    bool IsOnLeaveInPeriod,
    DateOnly? LeaveStartDate,
    DateOnly? LeaveEndDate,
    string? LeaveTypeName,
    decimal? AnnualLeaveBalance,
    decimal OvertimeHoursYtd,
    bool OvertimeLimitExceeded);

internal sealed record CompanyLeaveLedgerResult(IReadOnlyList<CompanyLeaveLedgerRow> Rows, OrgUnitHierarchy Hierarchy, decimal OvertimeLimitHours);

/// <summary>
/// Şirket geneli izin/fazla mesai defterinin ortak veri toplama mantığı - hem interaktif JSON sorgusu
/// (<c>GetCompanyLeaveLedgerHandler</c>, ekran filtreleri) hem de Excel dışa aktarımı (<c>CompanyLeaveLedgerReportProvider</c>,
/// Raporlar kataloğu) buradan besleniyor (DRY). Yıllık izin bakiyesi <see cref="LeaveBalanceSummaryReportProvider"/> ile
/// aynı şekilde bugüne göre projekte edilir (yalnız Code=Annual, HasBalance, etkin tür); "bu ay izinde" onaylı izinlerin
/// [monthStart, monthEnd] ile kesişimidir; fazla mesai yıl toplamı <see cref="OvertimeRequestRules.EvaluateAsync"/> ile
/// aynı taban (DurationMinutes, Pending+Approved) üzerinden 270 saat (Overtime.AnnualLimitHours) sınırıyla karşılaştırılır.
/// </summary>
internal static class CompanyLeaveLedgerData
{
    public static async Task<CompanyLeaveLedgerResult> BuildAsync(
        LeaveDbContext context,
        IDataScopeService dataScope,
        IOrgUnitLookup orgUnits,
        IEmployeeReportReader employees,
        ILegislationParameterReader legislation,
        TimeProvider clock,
        Guid tenantId,
        string scopePermission,
        IReadOnlyDictionary<string, string> parameters,
        DateOnly monthStart,
        DateOnly monthEnd,
        int overtimeYear,
        CancellationToken cancellationToken)
    {
        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, scopePermission, parameters, cancellationToken);
        var ids = rows.Select(r => r.EmployeeId).ToList();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var balanceByEmployee = await LoadAnnualBalancesAsync(context, tenantId, ids, today, cancellationToken);
        var onLeaveByEmployee = await LoadOnLeaveAsync(context, tenantId, ids, monthStart, monthEnd, cancellationToken);
        var overtimeMinutesByEmployee = await LoadOvertimeMinutesAsync(context, tenantId, ids, overtimeYear, cancellationToken);
        var yearEnd = new DateOnly(overtimeYear, 12, 31);
        var limitHours = await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.OvertimeAnnualLimitHours, yearEnd, OvertimeRequestRules.FallbackAnnualLimitHours, cancellationToken);

        var result = new List<CompanyLeaveLedgerRow>(rows.Count);
        foreach (var row in rows)
        {
            var onLeave = onLeaveByEmployee.GetValueOrDefault(row.EmployeeId);
            var overtimeMinutes = overtimeMinutesByEmployee.GetValueOrDefault(row.EmployeeId);
            var overtimeHours = Math.Round(overtimeMinutes / 60m, 1, MidpointRounding.AwayFromZero);

            result.Add(new CompanyLeaveLedgerRow(
                row.EmployeeId, row.FirstName, row.LastName, row.OrgUnitId, row.OrgAssignments,
                IsOnLeaveInPeriod: onLeave is not null,
                LeaveStartDate: onLeave?.StartDate,
                LeaveEndDate: onLeave?.EndDate,
                LeaveTypeName: onLeave?.TypeName,
                // TryGetValue, not GetValueOrDefault: the latter yields 0m for decimal, hiding "no annual type/profile" (web shows "—" for null).
                AnnualLeaveBalance: balanceByEmployee.TryGetValue(row.EmployeeId, out var balance) ? balance : null,
                OvertimeHoursYtd: overtimeHours,
                OvertimeLimitExceeded: overtimeHours > limitHours));
        }

        return new CompanyLeaveLedgerResult(result, hierarchy, limitHours);
    }

    /// <summary>Bugüne göre projekte edilen yıllık izin (Annual) bakiyesi; kural setinde etkin bir yıllık izin türü yoksa çalışan haritada yer almaz (ekranda "—").</summary>
    private static async Task<IReadOnlyDictionary<Guid, decimal>> LoadAnnualBalancesAsync(
        LeaveDbContext context, Guid tenantId, IReadOnlyList<Guid> employeeIds, DateOnly today, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var profiles = await context.Profiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId && employeeIds.Contains(p.EmployeeId))
            .ToListAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var ruleSets = await context.RuleSets.AsNoTracking().Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken);
        var annualTypeByRuleSet = ruleSets.ToDictionary(r => r.Id, r => r.Types.FirstOrDefault(t => t.Code == LeaveTypeCode.Annual && t.HasBalance && !t.IsDisabled));
        var annualTypeIds = annualTypeByRuleSet.Values.Where(t => t is not null).Select(t => t!.Id).ToHashSet();

        var ledger = annualTypeIds.Count == 0
            ? []
            : await context.LedgerEntries.AsNoTracking()
                .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.EmployeeId) && annualTypeIds.Contains(e.LeaveTypeId))
                .ToListAsync(cancellationToken);
        var ledgerByEmployee = ledger.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<LeaveLedgerEntry>)g.ToList());

        var balances = new Dictionary<Guid, decimal>();
        foreach (var profile in profiles)
        {
            var annualType = annualTypeByRuleSet.GetValueOrDefault(profile.RuleSetId);
            if (annualType is null)
            {
                continue;
            }

            var entries = ledgerByEmployee.GetValueOrDefault(profile.EmployeeId) ?? [];
            balances[profile.EmployeeId] = LeaveBalanceEngine.Project(annualType, profile.LeaveAccrualStartDate, today, entries).AvailableBalance;
        }

        return balances;
    }

    private sealed record OnLeaveInfo(DateOnly StartDate, DateOnly EndDate, string? TypeName);

    /// <summary>Ay ile kesişen ilk onaylı izin (birden fazlası varsa en erken başlayan) - "bu ay kimler izinde" sorusu.</summary>
    private static async Task<IReadOnlyDictionary<Guid, OnLeaveInfo>> LoadOnLeaveAsync(
        LeaveDbContext context, Guid tenantId, IReadOnlyList<Guid> employeeIds, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
        {
            return new Dictionary<Guid, OnLeaveInfo>();
        }

        var requests = await context.Requests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && employeeIds.Contains(r.EmployeeId)
                        && r.Status == LeaveRequestStatus.Approved && r.StartDate <= monthEnd && r.EndDate >= monthStart)
            .ToListAsync(cancellationToken);
        if (requests.Count == 0)
        {
            return new Dictionary<Guid, OnLeaveInfo>();
        }

        var typeNames = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        return requests
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g =>
            {
                var first = g.OrderBy(r => r.StartDate).First();
                return new OnLeaveInfo(first.StartDate, first.EndDate, typeNames.GetValueOrDefault(first.LeaveTypeId));
            });
    }

    /// <summary>Yıl içindeki aktif (Pending + Approved) fazla mesai dakikaları toplamı; OvertimeRequestRules.EvaluateAsync'teki yıl sınırı hesabıyla aynı taban (DurationMinutes).</summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> LoadOvertimeMinutesAsync(
        LeaveDbContext context, Guid tenantId, IReadOnlyList<Guid> employeeIds, int year, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var overtime = await context.OvertimeRequests.AsNoTracking()
            .Where(o => o.TenantId == tenantId && employeeIds.Contains(o.EmployeeId) && o.WorkDate >= yearStart && o.WorkDate <= yearEnd
                        && (o.Status == OvertimeRequestStatus.Pending || o.Status == OvertimeRequestStatus.Approved))
            .ToListAsync(cancellationToken);

        return overtime.GroupBy(o => o.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(o => o.DurationMinutes));
    }
}

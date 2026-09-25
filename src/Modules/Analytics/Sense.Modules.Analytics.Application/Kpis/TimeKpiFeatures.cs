using FluentValidation;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Analytics.Application.Kpis;

/// <summary>Zaman (devam) KPI uçlarının ortak izni (zaman-devam Z1.3): panoyu görme izni + IDataScopeService kapsamı.</summary>
internal static class TimeKpiPermissions
{
    public const string DashboardView = "attendance.dashboard.view";
}

/// <summary>Grup ekseni: <c>groupBy</c> değeri -> Organization seviye kodu (OrgUnitTypeCodes).</summary>
internal static class TimeKpiGroupBy
{
    public static readonly IReadOnlyDictionary<string, string> LevelCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["branch"] = "Branch",
        ["unit"] = "Department",
        ["team"] = "Team",
        ["title"] = "Title",
    };

    /// <summary>Kaynağı olmayan (atanmamış) grup satırının adı; istemci <c>groupId == null</c> ile de ayırt eder.</summary>
    public const string UnassignedName = "Unassigned";
}

/// <summary>Geç kalma / erken çıkma satırı (bir birim); <see cref="OrgUnitId"/> null = atanmamış çalışanlar.</summary>
public sealed record LateEarlyByUnitDto(Guid? OrgUnitId, string OrgUnitName, int LateCount, int LateMinutes, int EarlyLeaveCount, int EarlyLeaveMinutes);

/// <summary>Fazla mesai grup satırı; <see cref="EmployeeCount"/> Analytics kapsamındaki (görünür) çalışan sayısı.</summary>
public sealed record OvertimeByGroupDto(Guid? GroupId, string GroupName, int OvertimeMinutes, int EmployeeCount);

/// <summary>Vardiyaya katılım: <see cref="Rate"/> = çalışılan / planlı vardiya günü (planlı 0 ise 0).</summary>
public sealed record ShiftAttendanceRateDto(int PlannedShiftDays, int WorkedShiftDays, decimal Rate);

[RequiresPermission(TimeKpiPermissions.DashboardView)]
public sealed record GetAttendanceTrendKpiQuery(DateOnly? From = null, DateOnly? To = null) : IQuery<IReadOnlyList<DailyAttendanceTotalsDto>>;

[RequiresPermission(TimeKpiPermissions.DashboardView)]
public sealed record GetLateEarlyByUnitKpiQuery(DateOnly? From = null, DateOnly? To = null) : IQuery<IReadOnlyList<LateEarlyByUnitDto>>;

[RequiresPermission(TimeKpiPermissions.DashboardView)]
public sealed record GetOvertimeByGroupKpiQuery(string GroupBy = "unit", DateOnly? From = null, DateOnly? To = null) : IQuery<IReadOnlyList<OvertimeByGroupDto>>;

[RequiresPermission(TimeKpiPermissions.DashboardView)]
public sealed record GetShiftAttendanceRateKpiQuery(DateOnly? From = null, DateOnly? To = null) : IQuery<ShiftAttendanceRateDto>;

/// <summary>Aralık: from &lt;= to ve en çok bir yıl.</summary>
internal static class TimeKpiRange
{
    public const int MaxDays = 366;

    public static bool IsValid(DateOnly? from, DateOnly? to) =>
        from is null || to is null || (from <= to && to.Value.DayNumber - from.Value.DayNumber < MaxDays);

    /// <summary>Verilmeyen uçlar için son 7 gün (bugün dahil) varsayılanı.</summary>
    public static (DateOnly From, DateOnly To) Resolve(DateOnly? from, DateOnly? to, TimeProvider clock)
    {
        var end = to ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return (from ?? end.AddDays(-6), end);
    }
}

public sealed class GetAttendanceTrendKpiValidator : AbstractValidator<GetAttendanceTrendKpiQuery>
{
    public GetAttendanceTrendKpiValidator() => RuleFor(x => x).Must(x => TimeKpiRange.IsValid(x.From, x.To)).WithName("range");
}

public sealed class GetLateEarlyByUnitKpiValidator : AbstractValidator<GetLateEarlyByUnitKpiQuery>
{
    public GetLateEarlyByUnitKpiValidator() => RuleFor(x => x).Must(x => TimeKpiRange.IsValid(x.From, x.To)).WithName("range");
}

public sealed class GetShiftAttendanceRateKpiValidator : AbstractValidator<GetShiftAttendanceRateKpiQuery>
{
    public GetShiftAttendanceRateKpiValidator() => RuleFor(x => x).Must(x => TimeKpiRange.IsValid(x.From, x.To)).WithName("range");
}

public sealed class GetOvertimeByGroupKpiValidator : AbstractValidator<GetOvertimeByGroupKpiQuery>
{
    public GetOvertimeByGroupKpiValidator()
    {
        RuleFor(x => x.GroupBy).Must(g => TimeKpiGroupBy.LevelCodes.ContainsKey(g ?? string.Empty));
        RuleFor(x => x).Must(x => TimeKpiRange.IsValid(x.From, x.To)).WithName("range");
    }
}

/// <summary>Kapsamdaki çalışan kimlikleri: null (Company) kapsam = tüm aktif çalışanlar, aksi hâlde görünür küme.</summary>
internal static class TimeKpiScope
{
    public static async Task<IReadOnlyList<Guid>> EmployeeIdsAsync(
        ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory directory, CancellationToken ct)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(TimeKpiPermissions.DashboardView, ct);
        return visible is null ? await directory.ListActiveEmployeeIdsAsync(tenant.TenantId, ct) : visible.ToList();
    }
}

/// <summary>Çalışanı bir Organization seviyesindeki birime eşler (Attendance org bilmez; gruplama burada yapılır).</summary>
internal sealed class TimeKpiOrgGrouper
{
    private readonly Guid? _typeId;
    private readonly IReadOnlyDictionary<Guid, OrgUnitInfo> _units;

    private TimeKpiOrgGrouper(Guid? typeId, IReadOnlyDictionary<Guid, OrgUnitInfo> units)
    {
        _typeId = typeId;
        _units = units;
    }

    public static async Task<TimeKpiOrgGrouper> CreateAsync(IOrgUnitLookup orgUnits, Guid tenantId, string levelCode, CancellationToken ct)
    {
        var types = await orgUnits.ListActiveTypesAsync(tenantId, ct);
        var units = await orgUnits.ListActiveUnitsAsync(tenantId, ct);
        var typeId = types.FirstOrDefault(t => string.Equals(t.Code, levelCode, StringComparison.OrdinalIgnoreCase))?.Id;
        return new TimeKpiOrgGrouper(typeId, units.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First()));
    }

    /// <summary>Çalışanın bu seviyedeki birimi (açık atama önce, sonra en derin biriminin ata zinciri); yoksa null.</summary>
    public OrgUnitInfo? UnitFor(EmployeeReportRow row)
    {
        if (_typeId is not { } typeId)
        {
            return null;
        }

        if (row.OrgAssignments.TryGetValue(typeId, out var assigned) && _units.TryGetValue(assigned, out var direct))
        {
            return direct;
        }

        var visited = new HashSet<Guid>();
        var current = row.OrgUnitId;
        while (current is { } id && visited.Add(id) && _units.TryGetValue(id, out var unit))
        {
            if (unit.TypeId == typeId)
            {
                return unit;
            }

            current = unit.ParentId;
        }

        return null;
    }
}

public sealed class GetAttendanceTrendKpiHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory directory, IAttendanceDailyKpiReader reader, TimeProvider clock)
    : IQueryHandler<GetAttendanceTrendKpiQuery, IReadOnlyList<DailyAttendanceTotalsDto>>
{
    public async Task<Result<IReadOnlyList<DailyAttendanceTotalsDto>>> Handle(GetAttendanceTrendKpiQuery query, CancellationToken cancellationToken)
    {
        var (from, to) = TimeKpiRange.Resolve(query.From, query.To, clock);
        var ids = await TimeKpiScope.EmployeeIdsAsync(tenant, dataScope, directory, cancellationToken);
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<DailyAttendanceTotalsDto>>([]);
        }

        return Result.Success(await reader.DailyTotalsAsync(tenant.TenantId, from, to, ids, cancellationToken));
    }
}

public sealed class GetLateEarlyByUnitKpiHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory directory, IAttendanceDailyKpiReader reader,
    IEmployeeReportReader employees, IOrgUnitLookup orgUnits, TimeProvider clock)
    : IQueryHandler<GetLateEarlyByUnitKpiQuery, IReadOnlyList<LateEarlyByUnitDto>>
{
    public async Task<Result<IReadOnlyList<LateEarlyByUnitDto>>> Handle(GetLateEarlyByUnitKpiQuery query, CancellationToken cancellationToken)
    {
        var (from, to) = TimeKpiRange.Resolve(query.From, query.To, clock);
        var ids = await TimeKpiScope.EmployeeIdsAsync(tenant, dataScope, directory, cancellationToken);
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<LateEarlyByUnitDto>>([]);
        }

        var totals = await reader.EmployeeTotalsAsync(tenant.TenantId, from, to, ids, cancellationToken);
        var rows = (await employees.ListAsync(tenant.TenantId, ids, null, false, cancellationToken)).ToDictionary(r => r.EmployeeId);
        var grouper = await TimeKpiOrgGrouper.CreateAsync(orgUnits, tenant.TenantId, TimeKpiGroupBy.LevelCodes["unit"], cancellationToken);

        var result = totals
            .Where(t => rows.ContainsKey(t.EmployeeId))
            .GroupBy(t => grouper.UnitFor(rows[t.EmployeeId]))
            .Select(g => new LateEarlyByUnitDto(
                g.Key?.Id, g.Key?.Name ?? TimeKpiGroupBy.UnassignedName,
                g.Sum(t => t.LateCount), g.Sum(t => t.LateMinutes), g.Sum(t => t.EarlyLeaveCount), g.Sum(t => t.EarlyLeaveMinutes)))
            .Where(d => d.LateCount > 0 || d.EarlyLeaveCount > 0)
            .OrderByDescending(d => d.LateMinutes + d.EarlyLeaveMinutes)
            .ThenBy(d => d.OrgUnitName, StringComparer.Ordinal)
            .ToList();
        return Result.Success<IReadOnlyList<LateEarlyByUnitDto>>(result);
    }
}

public sealed class GetOvertimeByGroupKpiHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory directory, IAttendanceDailyKpiReader reader,
    IEmployeeReportReader employees, IOrgUnitLookup orgUnits, TimeProvider clock)
    : IQueryHandler<GetOvertimeByGroupKpiQuery, IReadOnlyList<OvertimeByGroupDto>>
{
    public async Task<Result<IReadOnlyList<OvertimeByGroupDto>>> Handle(GetOvertimeByGroupKpiQuery query, CancellationToken cancellationToken)
    {
        var (from, to) = TimeKpiRange.Resolve(query.From, query.To, clock);
        var ids = await TimeKpiScope.EmployeeIdsAsync(tenant, dataScope, directory, cancellationToken);
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<OvertimeByGroupDto>>([]);
        }

        var totals = (await reader.EmployeeTotalsAsync(tenant.TenantId, from, to, ids, cancellationToken)).ToDictionary(t => t.EmployeeId);
        var rows = await employees.ListAsync(tenant.TenantId, ids, null, false, cancellationToken);
        var grouper = await TimeKpiOrgGrouper.CreateAsync(orgUnits, tenant.TenantId, TimeKpiGroupBy.LevelCodes[query.GroupBy], cancellationToken);

        var result = rows
            .GroupBy(r => grouper.UnitFor(r))
            .Select(g => new OvertimeByGroupDto(
                g.Key?.Id, g.Key?.Name ?? TimeKpiGroupBy.UnassignedName,
                g.Sum(r => totals.TryGetValue(r.EmployeeId, out var t) ? t.OvertimeMinutes : 0), g.Count()))
            .OrderByDescending(d => d.OvertimeMinutes)
            .ThenBy(d => d.GroupName, StringComparer.Ordinal)
            .ToList();
        return Result.Success<IReadOnlyList<OvertimeByGroupDto>>(result);
    }
}

public sealed class GetShiftAttendanceRateKpiHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory directory, IAttendanceDailyKpiReader reader, TimeProvider clock)
    : IQueryHandler<GetShiftAttendanceRateKpiQuery, ShiftAttendanceRateDto>
{
    public async Task<Result<ShiftAttendanceRateDto>> Handle(GetShiftAttendanceRateKpiQuery query, CancellationToken cancellationToken)
    {
        var (from, to) = TimeKpiRange.Resolve(query.From, query.To, clock);
        var ids = await TimeKpiScope.EmployeeIdsAsync(tenant, dataScope, directory, cancellationToken);
        if (ids.Count == 0)
        {
            return Result.Success(new ShiftAttendanceRateDto(0, 0, 0m));
        }

        var totals = await reader.EmployeeTotalsAsync(tenant.TenantId, from, to, ids, cancellationToken);
        var planned = totals.Sum(t => t.PlannedShiftDays);
        var worked = totals.Sum(t => t.WorkedShiftDays);
        var rate = planned == 0 ? 0m : Math.Round((decimal)worked / planned, 4);
        return Result.Success(new ShiftAttendanceRateDto(planned, worked, rate));
    }
}

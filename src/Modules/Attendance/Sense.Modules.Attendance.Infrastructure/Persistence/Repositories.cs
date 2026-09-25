using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Modules.Attendance.Domain.Compensatory;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Infrastructure.Persistence;

public sealed class WorkScheduleRepository(AttendanceDbContext context) : IWorkScheduleRepository
{
    public void Add(WorkSchedule schedule) => context.WorkSchedules.Add(schedule);

    public void Remove(WorkSchedule schedule) => context.WorkSchedules.Remove(schedule);

    public async Task<Result<WorkSchedule>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await context.WorkSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return schedule is null ? Error.NotFound(AttendanceDomainErrors.ScheduleNotFound) : schedule;
    }

    public async Task<WorkSchedule?> FindDefaultAsync(CancellationToken cancellationToken = default) =>
        await context.WorkSchedules.FirstOrDefaultAsync(s => s.IsDefault, cancellationToken)
        ?? context.WorkSchedules.Local.FirstOrDefault(s => s.IsDefault);

    public async Task<IReadOnlyList<WorkSchedule>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.WorkSchedules.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query.OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public Task<int> CountAssignedEmployeesAsync(Guid workScheduleId, CancellationToken cancellationToken = default)
    {
        // Current assignment per employee = the row whose EffectiveFrom is the employee's max (GroupBy+First doesn't
        // translate to SQL; GroupBy+Max does, so the "current row" is found by joining back on the max date).
        var maxEffectiveFromByEmployee = context.WorkScheduleAssignments
            .GroupBy(a => a.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, EffectiveFrom = g.Max(a => a.EffectiveFrom) });

        var current = context.WorkScheduleAssignments.Join(
            maxEffectiveFromByEmployee,
            a => new { a.EmployeeId, a.EffectiveFrom },
            m => new { m.EmployeeId, m.EffectiveFrom },
            (a, _) => a);

        return current.CountAsync(a => a.WorkScheduleId == workScheduleId, cancellationToken);
    }
}

public sealed class EmployeeWorkScheduleAssignmentRepository(AttendanceDbContext context) : IEmployeeWorkScheduleAssignmentRepository
{
    public void Add(EmployeeWorkScheduleAssignment assignment) => context.WorkScheduleAssignments.Add(assignment);

    public void Remove(EmployeeWorkScheduleAssignment assignment) => context.WorkScheduleAssignments.Remove(assignment);

    public async Task<Result<EmployeeWorkScheduleAssignment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assignment = await context.WorkScheduleAssignments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return assignment is null ? Error.NotFound(AttendanceDomainErrors.AssignmentNotFound) : assignment;
    }

    public async Task<EmployeeWorkScheduleAssignment?> FindEffectiveOnAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        await context.WorkScheduleAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveFrom <= date && (a.EffectiveTo == null || a.EffectiveTo >= date))
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.WorkScheduleAssignments
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListCoveringRangeAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var all = await context.WorkScheduleAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveFrom <= to)
            .OrderBy(a => a.EffectiveFrom)
            .ToListAsync(cancellationToken);

        var lastBeforeRange = all.LastOrDefault(a => a.EffectiveFrom <= from);
        return all.Where(a => a.EffectiveFrom >= from || a == lastBeforeRange).ToList();
    }
}

public sealed class AttendanceDayRepository(AttendanceDbContext context) : IAttendanceDayRepository
{
    public void Add(AttendanceDay day) => context.AttendanceDays.Add(day);

    public async Task<Result<AttendanceDay>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var day = await context.AttendanceDays.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return day is null ? Error.NotFound(AttendanceDomainErrors.DayNotFound) : day;
    }

    public async Task<AttendanceDay?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        await context.AttendanceDays.FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.Date == date, cancellationToken);

    public async Task<IReadOnlyList<AttendanceDay>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await context.AttendanceDays
            .Where(d => d.EmployeeId == employeeId && d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceDay>> ListForEmployeesAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        return await context.AttendanceDays
            .Where(d => employeeIds.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date).ThenBy(d => d.EmployeeId)
            .ToListAsync(cancellationToken);
    }
}

public sealed class AnomalyResolutionRepository(AttendanceDbContext context) : IAnomalyResolutionRepository
{
    public void Add(AnomalyResolution resolution) => context.AnomalyResolutions.Add(resolution);

    public async Task<IReadOnlyList<AnomalyResolution>> ListForDayAsync(Guid attendanceDayId, CancellationToken cancellationToken = default) =>
        await context.AnomalyResolutions
            .Where(r => r.AttendanceDayId == attendanceDayId)
            .OrderBy(r => r.ResolvedAt)
            .ToListAsync(cancellationToken);
}

public sealed class ShiftSettingsRepository(AttendanceDbContext context) : IShiftSettingsRepository
{
    public void Add(ShiftSettings settings) => context.ShiftSettings.Add(settings);

    public async Task<ShiftSettings?> FindAsync(CancellationToken cancellationToken = default) =>
        await context.ShiftSettings.FirstOrDefaultAsync(cancellationToken)
        ?? context.ShiftSettings.Local.FirstOrDefault();
}

public sealed class ShiftAssignmentRepository(AttendanceDbContext context) : IShiftAssignmentRepository
{
    public void Add(ShiftAssignment assignment) => context.ShiftAssignments.Add(assignment);

    public async Task<Result<ShiftAssignment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assignment = await context.ShiftAssignments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return assignment is null ? Error.NotFound(AttendanceDomainErrors.ShiftNotFound) : assignment;
    }

    public async Task<ShiftAssignment?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        await context.ShiftAssignments.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date, cancellationToken);

    public async Task<IReadOnlyList<ShiftAssignment>> ListForRangeAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        var query = context.ShiftAssignments.Where(a => a.Date >= from && a.Date <= to);
        if (employeeIds is { Count: > 0 })
        {
            query = query.Where(a => employeeIds.Contains(a.EmployeeId));
        }

        return await query.OrderBy(a => a.Date).ThenBy(a => a.EmployeeId).ToListAsync(cancellationToken);
    }

    public Task<int> CountUnpublishedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        context.ShiftAssignments.CountAsync(a => a.Date >= from && a.Date <= to && a.Status == ShiftAssignmentStatus.Draft, cancellationToken);
}

public sealed class ShiftPlanLockRepository(AttendanceDbContext context) : IShiftPlanLockRepository
{
    public void Add(ShiftPlanLock plan) => context.ShiftPlanLocks.Add(plan);

    public async Task<ShiftPlanLock?> FindByPeriodAsync(DateOnly periodMonth, CancellationToken cancellationToken = default)
    {
        var normalized = ShiftPlanLock.NormalizeToMonthStart(periodMonth);
        return await context.ShiftPlanLocks.FirstOrDefaultAsync(p => p.PeriodMonth == normalized, cancellationToken)
            ?? context.ShiftPlanLocks.Local.FirstOrDefault(p => p.PeriodMonth == normalized);
    }
}

public sealed class EmployeeAvailabilityRepository(AttendanceDbContext context) : IEmployeeAvailabilityRepository
{
    public void Add(EmployeeAvailability availability) => context.EmployeeAvailabilities.Add(availability);

    public async Task<Result<EmployeeAvailability>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var availability = await context.EmployeeAvailabilities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return availability is null ? Error.NotFound(AttendanceDomainErrors.AvailabilityNotFound) : availability;
    }

    public async Task<IReadOnlyList<EmployeeAvailability>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await context.EmployeeAvailabilities
            .Where(a => a.EmployeeId == employeeId && a.Date >= from && a.Date <= to)
            .OrderBy(a => a.Date)
            .ToListAsync(cancellationToken);
}

public sealed class SelfCheckInRepository(AttendanceDbContext context) : ISelfCheckInRepository
{
    public void Add(SelfCheckIn checkIn) => context.SelfCheckIns.Add(checkIn);
}

public sealed class CompensatoryWorkLedgerRepository(AttendanceDbContext context) : ICompensatoryWorkLedgerRepository
{
    public void Add(CompensatoryWorkLedger ledger) => context.CompensatoryWorkLedgers.Add(ledger);

    public async Task<Result<CompensatoryWorkLedger>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ledger = await context.CompensatoryWorkLedgers.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return ledger is null ? Error.NotFound(AttendanceDomainErrors.CompensatoryLedgerNotFound) : ledger;
    }

    public async Task<CompensatoryWorkLedger?> FindByEmployeeAndWeekAsync(Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        await context.CompensatoryWorkLedgers.FirstOrDefaultAsync(l => l.EmployeeId == employeeId && l.WeekStart == weekStart, cancellationToken)
        ?? context.CompensatoryWorkLedgers.Local.FirstOrDefault(l => l.EmployeeId == employeeId && l.WeekStart == weekStart);

    public async Task<IReadOnlyList<CompensatoryWorkLedger>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await context.CompensatoryWorkLedgers
            .Where(l => l.EmployeeId == employeeId && l.WeekStart >= from && l.WeekStart <= to)
            .OrderBy(l => l.WeekStart)
            .ToListAsync(cancellationToken);
}

public sealed class MonthlyTimesheetApprovalRepository(AttendanceDbContext context) : IMonthlyTimesheetApprovalRepository
{
    public void Add(MonthlyTimesheetApproval approval) => context.MonthlyTimesheetApprovals.Add(approval);

    public async Task<MonthlyTimesheetApproval?> FindAsync(Guid employeeId, DateOnly periodMonth, CancellationToken cancellationToken = default)
    {
        var normalized = MonthlyTimesheetApproval.NormalizeToMonthStart(periodMonth);
        return await context.MonthlyTimesheetApprovals
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.PeriodMonth == normalized, cancellationToken)
            ?? context.MonthlyTimesheetApprovals.Local.FirstOrDefault(a => a.EmployeeId == employeeId && a.PeriodMonth == normalized);
    }

    public async Task<IReadOnlyList<MonthlyTimesheetApproval>> ListForPeriodAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly periodMonth, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var normalized = MonthlyTimesheetApproval.NormalizeToMonthStart(periodMonth);
        return await context.MonthlyTimesheetApprovals
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.PeriodMonth == normalized)
            .ToListAsync(cancellationToken);
    }
}

public sealed class EqualizationPeriodRepository(AttendanceDbContext context) : IEqualizationPeriodRepository
{
    public void Add(EqualizationPeriod period) => context.EqualizationPeriods.Add(period);

    public async Task<EqualizationPeriod?> FindCoveringAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        await context.EqualizationPeriods
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.PeriodStart <= date && date <= p.PeriodEnd, cancellationToken)
        ?? context.EqualizationPeriods.Local.FirstOrDefault(p => p.EmployeeId == employeeId && p.PeriodStart <= date && date <= p.PeriodEnd);

    public async Task<IReadOnlyList<EqualizationPeriod>> ListClosedEndingInRangeAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        return await context.EqualizationPeriods
            .Where(p => employeeIds.Contains(p.EmployeeId) && p.Status == EqualizationPeriodStatus.Closed && p.PeriodEnd >= from && p.PeriodEnd <= to)
            .OrderBy(p => p.EmployeeId).ThenBy(p => p.PeriodEnd)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Sorgular kiracı filtresini atlayıp açık <c>tenantId</c> ile süzer (arka plan yeniden hesaplaması kiracı bağlamı olmadan
/// da çalışır) ve silinmiş okutmaları her zaman dışarıda bırakır.
/// </summary>
public sealed class PunchRepository(AttendanceDbContext context) : IPunchRepository
{
    public void Add(Punch punch) => context.Punches.Add(punch);

    public async Task<IReadOnlyList<Punch>> ListForDayAsync(Guid tenantId, Guid employeeId, DateOnly workDate, CancellationToken cancellationToken = default) =>
        await Live(tenantId)
            .Where(p => p.EmployeeId == employeeId && p.WorkDate == workDate)
            .OrderBy(p => p.OccurredAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Punch>> ListForRangeAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        var query = Live(tenantId).Where(p => p.WorkDate >= from && p.WorkDate <= to);
        if (employeeIds is not null)
        {
            if (employeeIds.Count == 0)
            {
                return [];
            }

            query = query.Where(p => employeeIds.Contains(p.EmployeeId));
        }

        return await query.OrderBy(p => p.EmployeeId).ThenBy(p => p.OccurredAtUtc).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(
        Guid tenantId, Guid employeeId, DateTime occurredAtUtc, PunchDirection direction, PunchSource source, CancellationToken cancellationToken = default) =>
        Live(tenantId).AnyAsync(
            p => p.EmployeeId == employeeId && p.OccurredAtUtc == occurredAtUtc && p.Direction == direction && p.Source == source, cancellationToken);

    private IQueryable<Punch> Live(Guid tenantId) =>
        context.Punches.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).Where(p => p.TenantId == tenantId && p.DeletedAtUtc == null);
}

public sealed class ShiftDefinitionRepository(AttendanceDbContext context) : IShiftDefinitionRepository
{
    public void Add(ShiftDefinition definition) => context.ShiftDefinitions.Add(definition);

    public async Task<Result<ShiftDefinition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await context.ShiftDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return definition is null ? Error.NotFound(AttendanceDomainErrors.ShiftDefinitionNotFound) : definition;
    }

    public async Task<IReadOnlyList<ShiftDefinition>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.ShiftDefinitions.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        return await query.OrderBy(d => d.Code).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftDefinition>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.ShiftDefinitions.Where(d => ids.Contains(d.Id)).ToListAsync(cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim();
        return context.ShiftDefinitions.AnyAsync(d => d.Code == normalized && (excludeId == null || d.Id != excludeId), cancellationToken);
    }
}

public sealed class PunchRequestRepository(AttendanceDbContext context) : IPunchRequestRepository
{
    public void Add(PunchRequest request) => context.PunchRequests.Add(request);

    public async Task<Result<PunchRequest>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await context.PunchRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return request is null ? Error.NotFound(AttendanceDomainErrors.PunchRequestNotFound) : request;
    }

    public Task<PunchRequest?> FindByApprovalRequestIdAsync(Guid approvalRequestId, CancellationToken cancellationToken = default) =>
        context.PunchRequests.FirstOrDefaultAsync(r => r.ApprovalRequestId == approvalRequestId, cancellationToken);

    public async Task<IReadOnlyList<PunchRequest>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await context.PunchRequests
            .Where(r => r.EmployeeId == employeeId && r.WorkDate >= from && r.WorkDate <= to)
            .OrderBy(r => r.WorkDate).ThenBy(r => r.OccurredAtUtc)
            .ToListAsync(cancellationToken);
}

public sealed class ShiftPlanTemplateRepository(AttendanceDbContext context) : IShiftPlanTemplateRepository
{
    public void Add(ShiftPlanTemplate template) => context.ShiftPlanTemplates.Add(template);

    public void Remove(ShiftPlanTemplate template) => context.ShiftPlanTemplates.Remove(template);

    public async Task<Result<ShiftPlanTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await context.ShiftPlanTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template is null ? Error.NotFound(AttendanceDomainErrors.ShiftTemplateNotFound) : template;
    }

    public async Task<IReadOnlyList<ShiftPlanTemplate>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.ShiftPlanTemplates.OrderBy(t => t.Name).ToListAsync(cancellationToken);
}

public sealed class RotationRepository(AttendanceDbContext context) : IRotationRepository
{
    public void AddPattern(RotationPattern pattern) => context.RotationPatterns.Add(pattern);

    public async Task<Result<RotationPattern>> GetPatternByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var pattern = await context.RotationPatterns.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return pattern is null ? Error.NotFound(AttendanceDomainErrors.RotationPatternNotFound) : pattern;
    }

    public async Task<IReadOnlyList<RotationPattern>> ListPatternsAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.RotationPatterns.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        return await query.OrderBy(p => p.Code).ToListAsync(cancellationToken);
    }

    public Task<bool> PatternCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim();
        return context.RotationPatterns.AnyAsync(p => p.Code == normalized && (excludeId == null || p.Id != excludeId), cancellationToken);
    }

    public void AddAssignment(RotationAssignment assignment) => context.RotationAssignments.Add(assignment);

    public async Task<Result<RotationAssignment>> GetAssignmentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assignment = await context.RotationAssignments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return assignment is null ? Error.NotFound(AttendanceDomainErrors.RotationAssignmentNotFound) : assignment;
    }

    public async Task<IReadOnlyList<RotationAssignment>> ListAssignmentsOverlappingAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        var query = context.RotationAssignments.Where(a => a.StartDate <= to && (a.EndDate == null || a.EndDate >= from));
        if (employeeIds is not null)
        {
            if (employeeIds.Count == 0)
            {
                return [];
            }

            query = query.Where(a => employeeIds.Contains(a.EmployeeId));
        }

        return await query.OrderBy(a => a.EmployeeId).ThenBy(a => a.StartDate).ToListAsync(cancellationToken);
    }
}

public sealed class TerminalRepository(AttendanceDbContext context) : ITerminalRepository
{
    public void AddTerminal(Terminal terminal) => context.Terminals.Add(terminal);

    public async Task<Result<Terminal>> GetTerminalByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var terminal = await context.Terminals.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return terminal is null ? Error.NotFound(AttendanceDomainErrors.TerminalNotFound) : terminal;
    }

    public async Task<IReadOnlyList<Terminal>> ListTerminalsAsync(CancellationToken cancellationToken = default) =>
        await context.Terminals.OrderBy(t => t.Code).ToListAsync(cancellationToken);

    public Task<bool> TerminalCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim();
        return context.Terminals.AnyAsync(t => t.Code == normalized && (excludeId == null || t.Id != excludeId), cancellationToken);
    }

    public async Task<IReadOnlyList<Terminal>> ListActiveByApiKeyPrefixAcrossTenantsAsync(string apiKeyPrefix, CancellationToken cancellationToken = default) =>
        await context.Terminals.IgnoreQueryFilters([ModuleDbContext.TenantFilter])
            .Where(t => t.ApiKeyPrefix == apiKeyPrefix && t.IsActive)
            .ToListAsync(cancellationToken);

    public void AddGroup(TerminalGroup group) => context.TerminalGroups.Add(group);

    public async Task<Result<TerminalGroup>> GetGroupByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await context.TerminalGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        return group is null ? Error.NotFound(AttendanceDomainErrors.TerminalGroupNotFound) : group;
    }

    public async Task<IReadOnlyList<TerminalGroup>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        await context.TerminalGroups.OrderBy(g => g.Name).ToListAsync(cancellationToken);

    public void AddCredential(EmployeeTerminalCredential credential) => context.EmployeeTerminalCredentials.Add(credential);

    public async Task<Result<EmployeeTerminalCredential>> GetCredentialByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var credential = await context.EmployeeTerminalCredentials.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return credential is null ? Error.NotFound(AttendanceDomainErrors.TerminalCredentialNotFound) : credential;
    }

    public Task<EmployeeTerminalCredential?> FindActiveCredentialAsync(
        Guid tenantId, TerminalCredentialKind kind, string value, CancellationToken cancellationToken = default)
    {
        var normalized = value.Trim();
        return context.EmployeeTerminalCredentials.IgnoreQueryFilters([ModuleDbContext.TenantFilter])
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Kind == kind && c.Value == normalized && c.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeTerminalCredential>> ListCredentialsForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.EmployeeTerminalCredentials
            .Where(c => c.EmployeeId == employeeId)
            .OrderBy(c => c.Kind)
            .ToListAsync(cancellationToken);
}

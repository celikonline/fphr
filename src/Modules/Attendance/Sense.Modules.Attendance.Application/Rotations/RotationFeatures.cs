using FluentValidation;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Rotations;

public static class RotationErrors
{
    public const string CodeDuplicate = "attendance.rotation.code_duplicate";
    public const string Inactive = "attendance.rotation.inactive";
    public const string AssignmentOverlap = "attendance.rotation_assignment.overlap";
}

/// <summary>Adım DTO'su: <c>DayType</c> web sözleşmesinde yalnız "work" | "off" (off = <see cref="ShiftDayType.RestDay"/>).</summary>
public sealed record RotationStepDto(int DayOffset, Guid? ShiftDefinitionId, string DayType);

public sealed record RotationPatternDto(
    Guid Id, string Code, string Name, RotationPreset Preset, int CycleLengthDays, IReadOnlyList<RotationStepDto> Steps, bool IsActive);

public sealed record ApplyRotationResult(int CreatedCount, IReadOnlyList<string> SkippedLockedMonths);

internal static class RotationMapper
{
    public const string WorkDayType = "work";
    public const string OffDayType = "off";

    public static RotationPatternDto ToDto(RotationPattern p) =>
        new(p.Id, p.Code, p.Name, p.Preset, p.CycleLengthDays, [.. p.Steps.Select(s => new RotationStepDto(s.DayOffset, s.ShiftDefinitionId, s.DayType == ShiftDayType.Work ? WorkDayType : OffDayType))], p.IsActive);

    /// <summary>Adımları alan modeline çevirir; "work" adımı vardiya tanımı ister, bilinmeyen gün türü reddedilir.</summary>
    public static Result<IReadOnlyList<RotationStep>> ToSteps(IReadOnlyList<RotationStepDto>? dtos)
    {
        if (dtos is null)
        {
            return Error.Validation(AttendanceDomainErrors.RotationStepInvalid);
        }

        var steps = new List<RotationStep>();
        foreach (var dto in dtos)
        {
            var dayType = dto.DayType switch
            {
                WorkDayType => (ShiftDayType?)ShiftDayType.Work,
                OffDayType => ShiftDayType.RestDay,
                _ => null,
            };
            if (dayType is null || (dayType == ShiftDayType.Work && dto.ShiftDefinitionId is null))
            {
                return Error.Validation(AttendanceDomainErrors.RotationStepInvalid);
            }

            steps.Add(RotationStep.Of(dto.DayOffset, dayType == ShiftDayType.Work ? dto.ShiftDefinitionId : null, dayType.Value));
        }

        return steps;
    }

    public static async Task<Result> EnsureShiftDefinitionsExistAsync(IShiftDefinitionRepository definitions, IReadOnlyList<RotationStep> steps, CancellationToken ct)
    {
        var ids = steps.Where(s => s.ShiftDefinitionId is not null).Select(s => s.ShiftDefinitionId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success();
        }

        var found = await definitions.ListByIdsAsync(ids, ct);
        return found.Count == ids.Count ? Result.Success() : Error.NotFound(AttendanceDomainErrors.ShiftDefinitionNotFound);
    }
}

[RequiresPermission("attendance.rotation.view")]
public sealed record ListRotationsQuery(bool IncludeInactive) : IQuery<IReadOnlyList<RotationPatternDto>>;

public sealed class ListRotationsHandler(IRotationRepository rotations) : IQueryHandler<ListRotationsQuery, IReadOnlyList<RotationPatternDto>>
{
    public async Task<Result<IReadOnlyList<RotationPatternDto>>> Handle(ListRotationsQuery query, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyList<RotationPatternDto>>([.. (await rotations.ListPatternsAsync(query.IncludeInactive, cancellationToken)).Select(RotationMapper.ToDto)]);
}

[RequiresPermission("attendance.rotation.manage")]
public sealed record CreateRotationCommand(
    string Code, string Name, RotationPreset Preset, int CycleLengthDays, IReadOnlyList<RotationStepDto> Steps) : ICommand<Guid>;

public sealed class CreateRotationHandler(
    ITenantContext tenant, IRotationRepository rotations, IShiftDefinitionRepository definitions) : ICommandHandler<CreateRotationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRotationCommand command, CancellationToken cancellationToken)
    {
        var steps = RotationMapper.ToSteps(command.Steps);
        if (steps.IsFailure)
        {
            return steps.Error;
        }

        var created = RotationPattern.Create(tenant.TenantId, command.Code, command.Name, command.Preset, command.CycleLengthDays, steps.Value);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (await rotations.PatternCodeExistsAsync(command.Code, null, cancellationToken))
        {
            return Error.Conflict(RotationErrors.CodeDuplicate);
        }

        var exists = await RotationMapper.EnsureShiftDefinitionsExistAsync(definitions, steps.Value, cancellationToken);
        if (exists.IsFailure)
        {
            return exists.Error;
        }

        rotations.AddPattern(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("attendance.rotation.manage")]
public sealed record UpdateRotationCommand(
    Guid Id, string Code, string Name, RotationPreset Preset, int CycleLengthDays, IReadOnlyList<RotationStepDto> Steps) : ICommand;

public sealed class UpdateRotationHandler(IRotationRepository rotations, IShiftDefinitionRepository definitions) : ICommandHandler<UpdateRotationCommand>
{
    public async Task<Result> Handle(UpdateRotationCommand command, CancellationToken cancellationToken)
    {
        var pattern = await rotations.GetPatternByIdAsync(command.Id, cancellationToken);
        if (pattern.IsFailure)
        {
            return pattern.Error;
        }

        var steps = RotationMapper.ToSteps(command.Steps);
        if (steps.IsFailure)
        {
            return steps.Error;
        }

        if (await rotations.PatternCodeExistsAsync(command.Code, command.Id, cancellationToken))
        {
            return Error.Conflict(RotationErrors.CodeDuplicate);
        }

        var exists = await RotationMapper.EnsureShiftDefinitionsExistAsync(definitions, steps.Value, cancellationToken);
        if (exists.IsFailure)
        {
            return exists;
        }

        return pattern.Value.Update(command.Code, command.Name, command.Preset, command.CycleLengthDays, steps.Value);
    }
}

[RequiresPermission("attendance.rotation.manage")]
public sealed record SetRotationActiveCommand(Guid Id, bool IsActive) : ICommand;

public sealed class SetRotationActiveHandler(IRotationRepository rotations) : ICommandHandler<SetRotationActiveCommand>
{
    public async Task<Result> Handle(SetRotationActiveCommand command, CancellationToken cancellationToken)
    {
        var pattern = await rotations.GetPatternByIdAsync(command.Id, cancellationToken);
        if (pattern.IsFailure)
        {
            return pattern.Error;
        }

        if (command.IsActive)
        {
            pattern.Value.Activate();
        }
        else
        {
            pattern.Value.Deactivate();
        }

        return Result.Success();
    }
}

/// <summary>
/// Kalıbı çalışanlara bağlar ve taslak <see cref="ShiftAssignment"/> üretir. Toplu atama atomiktir: doğrulama/çakışma
/// hatasında hiçbir kayıt eklenmez. Kilitli aylara yazılmaz (ay listesi döner). Bitişi olmayan atama
/// <see cref="ApplyRotationHandler.OpenEndedHorizonDays"/> gün ilerisine kadar açılır. Yayınlanmış vardiyalar ezilmez.
/// </summary>
[RequiresPermission("attendance.rotation.manage")]
public sealed record ApplyRotationCommand(Guid RotationId, IReadOnlyList<Guid> EmployeeIds, DateOnly StartDate, DateOnly? EndDate, int OffsetDays) : ICommand<ApplyRotationResult>;

public sealed class ApplyRotationValidator : AbstractValidator<ApplyRotationCommand>
{
    public ApplyRotationValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty();
        RuleFor(x => x.OffsetDays).GreaterThanOrEqualTo(0);
    }
}

public sealed class ApplyRotationHandler(
    ITenantContext tenant, IRotationRepository rotations, IShiftDefinitionRepository definitions, IShiftAssignmentRepository shifts,
    IShiftPlanLockRepository locks, IEmployeeLookup employeeLookup, IEmployeeDirectory directory, IDataScopeService dataScope)
    : ICommandHandler<ApplyRotationCommand, ApplyRotationResult>
{
    public const int OpenEndedHorizonDays = 90;

    private static readonly TimeOnly OffDayStart = new(0, 0);
    private static readonly TimeOnly OffDayEnd = new(0, 1);

    public async Task<Result<ApplyRotationResult>> Handle(ApplyRotationCommand command, CancellationToken cancellationToken)
    {
        var pattern = await rotations.GetPatternByIdAsync(command.RotationId, cancellationToken);
        if (pattern.IsFailure)
        {
            return pattern.Error;
        }

        if (!pattern.Value.IsActive)
        {
            return Error.Rule(RotationErrors.Inactive);
        }

        if (command.OffsetDays >= pattern.Value.CycleLengthDays)
        {
            return Error.Validation(AttendanceDomainErrors.RotationAssignmentRangeInvalid);
        }

        var employeeIds = command.EmployeeIds.Distinct().ToList();
        var newAssignments = new List<RotationAssignment>();
        foreach (var employeeId in employeeIds)
        {
            var assignment = RotationAssignment.Create(tenant.TenantId, employeeId, pattern.Value.Id, command.StartDate, command.EndDate, command.OffsetDays);
            if (assignment.IsFailure)
            {
                return assignment.Error;
            }

            newAssignments.Add(assignment.Value);
        }

        var scope = await CheckEmployeesAsync(employeeIds, cancellationToken);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        var overlap = await CheckOverlapAsync(employeeIds, command, cancellationToken);
        if (overlap.IsFailure)
        {
            return overlap.Error;
        }

        var shiftIds = pattern.Value.Steps.Where(s => s.ShiftDefinitionId is not null).Select(s => s.ShiftDefinitionId!.Value).Distinct().ToList();
        var definitionMap = (await definitions.ListByIdsAsync(shiftIds, cancellationToken)).ToDictionary(d => d.Id);
        if (definitionMap.Count != shiftIds.Count)
        {
            return Error.NotFound(AttendanceDomainErrors.ShiftDefinitionNotFound);
        }

        var horizonEnd = command.EndDate ?? command.StartDate.AddDays(OpenEndedHorizonDays - 1);
        var days = RotationExpander.Expand(pattern.Value, command.StartDate, command.OffsetDays, command.StartDate, horizonEnd);
        var (openDays, skippedMonths) = await SplitLockedAsync(days, cancellationToken);
        var existing = (await shifts.ListForRangeAsync(command.StartDate, horizonEnd, employeeIds, cancellationToken))
            .ToDictionary(a => (a.EmployeeId, a.Date));

        var planned = new List<(Guid EmployeeId, DateOnly Date, Guid? ShiftId, ShiftDayType DayType, ShiftDefinition? Definition)>();
        foreach (var employeeId in employeeIds)
        {
            foreach (var day in openDays)
            {
                if (existing.TryGetValue((employeeId, day.Date), out var current) && current.Status == ShiftAssignmentStatus.Published)
                {
                    continue;
                }

                planned.Add((employeeId, day.Date, day.ShiftDefinitionId, day.DayType, day.ShiftDefinitionId is { } id ? definitionMap[id] : null));
            }
        }

        var built = BuildAssignments(planned, existing);
        if (built.IsFailure)
        {
            return built.Error;
        }

        foreach (var assignment in newAssignments)
        {
            rotations.AddAssignment(assignment);
        }

        foreach (var created in built.Value)
        {
            shifts.Add(created);
        }

        return new ApplyRotationResult(planned.Count, skippedMonths);
    }

    private Result<List<ShiftAssignment>> BuildAssignments(
        List<(Guid EmployeeId, DateOnly Date, Guid? ShiftId, ShiftDayType DayType, ShiftDefinition? Definition)> planned,
        Dictionary<(Guid, DateOnly), ShiftAssignment> existing)
    {
        var toAdd = new List<ShiftAssignment>();
        foreach (var (employeeId, date, shiftId, dayType, definition) in planned)
        {
            var (start, end, breakMinutes, isNight) = TimesOf(definition);
            if (existing.TryGetValue((employeeId, date), out var current))
            {
                var updated = current.Update(start, end, breakMinutes, isNight, current.Note);
                var plan = updated.IsSuccess ? current.SetPlan(shiftId, dayType) : updated;
                if (plan.IsFailure)
                {
                    return plan.Error;
                }

                continue;
            }

            var created = ShiftAssignment.Create(tenant.TenantId, employeeId, date, start, end, breakMinutes, isNight, null);
            if (created.IsFailure)
            {
                return created.Error;
            }

            var planned2 = created.Value.SetPlan(shiftId, dayType);
            if (planned2.IsFailure)
            {
                return planned2.Error;
            }

            toAdd.Add(created.Value);
        }

        return toAdd;
    }

    private static (TimeOnly Start, TimeOnly End, int BreakMinutes, bool IsNight) TimesOf(ShiftDefinition? definition)
    {
        if (definition is null)
        {
            return (OffDayStart, OffDayEnd, 0, false);
        }

        var start = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(definition.StartMinute));
        var end = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(definition.EndMinute));
        if (start == end && definition.DurationMinutes is { } duration)
        {
            end = start.AddMinutes(duration);
        }

        return (start, end, definition.Breaks.Sum(b => b.DurationMinutes), end <= start);
    }

    private async Task<(List<(DateOnly Date, Guid? ShiftDefinitionId, ShiftDayType DayType)> Open, List<string> SkippedMonths)> SplitLockedAsync(
        IReadOnlyList<(DateOnly Date, Guid? ShiftDefinitionId, ShiftDayType DayType)> days, CancellationToken cancellationToken)
    {
        var lockedByMonth = new Dictionary<DateOnly, bool>();
        var open = new List<(DateOnly, Guid?, ShiftDayType)>();
        foreach (var day in days)
        {
            var month = ShiftPlanLock.NormalizeToMonthStart(day.Date);
            if (!lockedByMonth.TryGetValue(month, out var isLocked))
            {
                isLocked = (await ShiftPlanLockGuard.EnsureNotLockedAsync(locks, month, cancellationToken)).IsFailure;
                lockedByMonth[month] = isLocked;
            }

            if (!isLocked)
            {
                open.Add(day);
            }
        }

        var skipped = lockedByMonth.Where(kv => kv.Value).Select(kv => kv.Key).Order().Select(m => m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        return (open, skipped);
    }

    private async Task<Result> CheckEmployeesAsync(IReadOnlyList<Guid> employeeIds, CancellationToken cancellationToken)
    {
        foreach (var employeeId in employeeIds)
        {
            if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.RotationManage.Key, employeeId, cancellationToken))
            {
                return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
            }

            if (!await employeeLookup.ExistsActiveAsync(tenant.TenantId, employeeId, cancellationToken))
            {
                return Error.NotFound(AttendanceErrors.EmployeeNotFound);
            }
        }

        return Result.Success();
    }

    private async Task<Result> CheckOverlapAsync(IReadOnlyList<Guid> employeeIds, ApplyRotationCommand command, CancellationToken cancellationToken)
    {
        var overlapping = await rotations.ListAssignmentsOverlappingAsync(command.StartDate, command.EndDate ?? DateOnly.MaxValue, employeeIds, cancellationToken);
        if (overlapping.Count == 0)
        {
            return Result.Success();
        }

        var conflicting = overlapping[0].EmployeeId;
        var contacts = await directory.GetContactsAsync(tenant.TenantId, [conflicting], cancellationToken);
        var name = contacts.Count > 0 ? $"{contacts[0].FirstName} {contacts[0].LastName}".Trim() : conflicting.ToString();
        return Error.Conflict(RotationErrors.AssignmentOverlap, ("employee", name));
    }
}

using FluentValidation;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.ShiftTemplates;

/// <summary>Şablonun bir hücresi: çalışan x dönem başından gün ofseti. Yalnız atanmış hücreler saklanır.</summary>
public sealed record ShiftTemplateEntryDto(Guid EmployeeId, int DayOffset, Guid? ShiftDefinitionId, ShiftDayType DayType);

public sealed record ShiftTemplateDto(Guid Id, string Name, int PeriodLengthDays, IReadOnlyList<ShiftTemplateEntryDto> Entries, Guid CreatedByUserId);

/// <summary>Toplu uygulamada atlanan hücrenin nedeni (web: <c>locked</c> | <c>published</c>; B8 toplu atamayla aynı şekil).</summary>
public enum ShiftTemplateSkipReason
{
    Locked = 0,
    Published = 1,
}

public sealed record ShiftTemplateSkippedDto(Guid EmployeeId, DateOnly Date, ShiftTemplateSkipReason Reason);

public sealed record ShiftTemplateApplyResultDto(int AffectedCount, IReadOnlyList<ShiftTemplateSkippedDto> Skipped);

internal static class ShiftTemplateMapper
{
    public static ShiftTemplateDto ToDto(ShiftPlanTemplate t) =>
        new(t.Id, t.Name, t.PeriodLengthDays, t.Entries.Select(e => new ShiftTemplateEntryDto(e.EmployeeId, e.DayOffset, e.ShiftDefinitionId, e.DayType)).ToList(), t.CreatedByUserId);
}

/// <summary>Şablon kaydet/uygula için ortak: çalışanların kapsam ve aktiflik denetimi.</summary>
internal static class ShiftTemplateEmployeeGuard
{
    public static async Task<Result> EnsureInScopeAndActiveAsync(
        IEnumerable<Guid> employeeIds, Guid tenantId, IDataScopeService dataScope, IEmployeeLookup employeeLookup, CancellationToken cancellationToken)
    {
        foreach (var employeeId in employeeIds.Distinct())
        {
            if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, employeeId, cancellationToken))
            {
                return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
            }

            if (!await employeeLookup.ExistsActiveAsync(tenantId, employeeId, cancellationToken))
            {
                return Error.NotFound(AttendanceErrors.EmployeeNotFound);
            }
        }

        return Result.Success();
    }
}

[RequiresPermission("attendance.shifttemplate.manage")]
public sealed record ListShiftTemplatesQuery : IQuery<IReadOnlyList<ShiftTemplateDto>>;

public sealed class ListShiftTemplatesHandler(IShiftPlanTemplateRepository templates) : IQueryHandler<ListShiftTemplatesQuery, IReadOnlyList<ShiftTemplateDto>>
{
    public async Task<Result<IReadOnlyList<ShiftTemplateDto>>> Handle(ListShiftTemplatesQuery query, CancellationToken cancellationToken) =>
        (await templates.ListAsync(cancellationToken)).Select(ShiftTemplateMapper.ToDto).ToList();
}

/// <summary>"Şablon olarak kaydet": seçilen çalışan x gün hücreleri, dönem başına göreli ofsetlerle saklanır.</summary>
[RequiresPermission("attendance.shifttemplate.manage")]
public sealed record CreateShiftTemplateCommand(string Name, int PeriodLengthDays, IReadOnlyList<ShiftTemplateEntryDto> Entries) : ICommand<Guid>;

public sealed class CreateShiftTemplateValidator : AbstractValidator<CreateShiftTemplateCommand>
{
    public CreateShiftTemplateValidator() => RuleFor(x => x.Entries).NotNull();
}

public sealed class CreateShiftTemplateHandler(
    ITenantContext tenant, ICurrentUser currentUser, IShiftPlanTemplateRepository templates, IShiftDefinitionRepository definitions,
    IEmployeeLookup employeeLookup, IDataScopeService dataScope)
    : ICommandHandler<CreateShiftTemplateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateShiftTemplateCommand command, CancellationToken cancellationToken)
    {
        var access = await ShiftTemplateEmployeeGuard.EnsureInScopeAndActiveAsync(
            command.Entries.Select(e => e.EmployeeId), tenant.TenantId, dataScope, employeeLookup, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var definitionIds = command.Entries.Where(e => e.DayType == ShiftDayType.Work && e.ShiftDefinitionId is not null).Select(e => e.ShiftDefinitionId!.Value).Distinct().ToList();
        var known = (await definitions.ListByIdsAsync(definitionIds, cancellationToken)).Where(d => d.IsActive).Select(d => d.Id).ToHashSet();
        if (definitionIds.Any(id => !known.Contains(id)))
        {
            return Error.NotFound(AttendanceDomainErrors.ShiftDefinitionNotFound);
        }

        // Çalışma günü olmayan hücrede vardiya tanımı tutulmaz; çalışma gününde tanım zorunludur.
        if (command.Entries.Any(e => e.DayType == ShiftDayType.Work && e.ShiftDefinitionId is null))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftTemplateEntryInvalid);
        }

        var entries = command.Entries
            .Select(e => ShiftPlanTemplateEntry.Of(e.EmployeeId, e.DayOffset, e.DayType == ShiftDayType.Work ? e.ShiftDefinitionId : null, e.DayType))
            .ToList();
        var created = ShiftPlanTemplate.Create(tenant.TenantId, command.Name, command.PeriodLengthDays, entries, currentUser.UserId ?? Guid.Empty);
        if (created.IsFailure)
        {
            return created.Error;
        }

        templates.Add(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("attendance.shifttemplate.manage")]
public sealed record DeleteShiftTemplateCommand(Guid Id) : ICommand;

public sealed class DeleteShiftTemplateHandler(IShiftPlanTemplateRepository templates) : ICommandHandler<DeleteShiftTemplateCommand>
{
    public async Task<Result> Handle(DeleteShiftTemplateCommand command, CancellationToken cancellationToken)
    {
        var found = await templates.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        templates.Remove(found.Value);
        return Result.Success();
    }
}

/// <summary>
/// "Şablonu uygula": şablon hücrelerini <paramref name="StartDate"/> gününden itibaren, yalnız şablondaki çalışanlara yazar. Kilitli aydaki hücreler ve
/// (<paramref name="OverwritePublished"/> yoksa) yayınlanmış vardiyalar atlanır; atlananlar sonuçta bildirilir.
/// </summary>
[RequiresPermission("attendance.shifttemplate.manage")]
public sealed record ApplyShiftTemplateCommand(Guid Id, DateOnly StartDate, bool OverwritePublished) : ICommand<ShiftTemplateApplyResultDto>;

public sealed class ApplyShiftTemplateHandler(
    ITenantContext tenant, IShiftPlanTemplateRepository templates, IShiftDefinitionRepository definitions, IShiftAssignmentRepository shifts,
    IShiftSettingsRepository settingsRepository, IShiftPlanLockRepository locks, IEmployeeAvailabilityRepository availability,
    IEmployeeLookup employeeLookup, IDataScopeService dataScope, IAttendanceUnitOfWork unitOfWork)
    : ICommandHandler<ApplyShiftTemplateCommand, ShiftTemplateApplyResultDto>
{
    public async Task<Result<ShiftTemplateApplyResultDto>> Handle(ApplyShiftTemplateCommand command, CancellationToken cancellationToken)
    {
        var found = await templates.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var template = found.Value;
        var access = await ShiftTemplateEmployeeGuard.EnsureInScopeAndActiveAsync(
            template.Entries.Select(e => e.EmployeeId), tenant.TenantId, dataScope, employeeLookup, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var definitionIds = template.Entries.Where(e => e.ShiftDefinitionId is not null).Select(e => e.ShiftDefinitionId!.Value).Distinct().ToList();
        var byId = (await definitions.ListByIdsAsync(definitionIds, cancellationToken)).Where(d => d.IsActive).ToDictionary(d => d.Id);
        if (definitionIds.Any(id => !byId.ContainsKey(id)))
        {
            return Error.NotFound(AttendanceDomainErrors.ShiftDefinitionNotFound);
        }

        var settings = await ShiftSettingsProvisioning.EnsureAsync(settingsRepository, tenant, unitOfWork, cancellationToken);
        var writer = new ShiftPlanCellWriter(tenant, shifts, availability, settings);
        var lockCache = new ShiftLockCache(locks);
        var skipped = new List<ShiftTemplateSkippedDto>();
        var affected = 0;

        foreach (var entry in template.Entries.OrderBy(e => e.EmployeeId).ThenBy(e => e.DayOffset))
        {
            var date = command.StartDate.AddDays(entry.DayOffset);
            if (await lockCache.IsLockedAsync(date, cancellationToken))
            {
                skipped.Add(new ShiftTemplateSkippedDto(entry.EmployeeId, date, ShiftTemplateSkipReason.Locked));
                continue;
            }

            var existing = await writer.FindAsync(entry.EmployeeId, date, cancellationToken);
            if (existing is { Status: ShiftAssignmentStatus.Published } && !command.OverwritePublished)
            {
                skipped.Add(new ShiftTemplateSkippedDto(entry.EmployeeId, date, ShiftTemplateSkipReason.Published));
                continue;
            }

            var definition = entry.ShiftDefinitionId is { } id ? byId[id] : null;
            var written = await writer.WriteAsync(entry.EmployeeId, date, definition, entry.DayType, cancellationToken);
            if (written.IsFailure)
            {
                return written.Error;
            }

            affected++;
        }

        return new ShiftTemplateApplyResultDto(affected, skipped);
    }
}

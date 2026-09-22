using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.OccupationalSafety.Contracts.Events;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.Incidents;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Application.Incidents;

/// <summary>İK kaydı: iş kazası ya da ramak kala. İş kazasında SGK bildirim son günü tatil takvimiyle hesaplanır.</summary>
[RequiresPermission("ohs.incidents.manage")]
public sealed record RecordIncidentCommand(
    OhsIncidentKind Kind, DateOnly EventDate, Guid? LocationId, Guid? EmployeeId, string Description) : ICommand<Guid>;

public sealed class RecordIncidentValidator : AbstractValidator<RecordIncidentCommand>
{
    public RecordIncidentValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(OhsLimits.IncidentDescriptionMaxLength);
    }
}

public sealed class RecordIncidentHandler(
    ITenantContext tenant,
    IOhsIncidentRepository incidents,
    IEmployeeLookup employees,
    IHolidayCalendarLookup holidays,
    TimeProvider clock,
    IIntegrationEventOutbox outbox) : ICommandHandler<RecordIncidentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordIncidentCommand command, CancellationToken cancellationToken)
    {
        if (command.EmployeeId is { } employeeId
            && !await employees.ExistsActiveAsync(tenant.TenantId, employeeId, cancellationToken))
        {
            return Error.NotFound(OhsDomainErrors.IncidentEmployeeNotFound);
        }

        DateOnly? deadline = command.Kind == OhsIncidentKind.WorkAccident
            ? await SgkNotificationDeadline.CalculateAsync(holidays, tenant.TenantId, command.EventDate, cancellationToken)
            : null;

        var created = OhsIncident.Create(
            tenant.TenantId, command.Kind, command.EventDate, IncidentClock.Today(clock), command.LocationId,
            command.EmployeeId, reportedByEmployeeId: null, command.Description, deadline);
        if (created.IsFailure)
        {
            return created.Error;
        }

        incidents.Add(created.Value);
        if (created.Value is { Kind: OhsIncidentKind.WorkAccident, EmployeeId: { } injured, SgkNotificationDeadline: { } due })
        {
            outbox.Enqueue(new WorkAccidentRecorded(tenant.TenantId, created.Value.Id, injured, command.EventDate, due));
        }

        return created.Value.Id;
    }
}

/// <summary>Self-servis ramak kala bildirimi: çalışan yaşadığı/gördüğü tehlikeli olayı bildirir (kaza kaydı İK'dadır).</summary>
[RequiresPermission("ohs.my.incident_reports")]
public sealed record ReportNearMissCommand(DateOnly EventDate, Guid? LocationId, string Description) : ICommand<Guid>;

public sealed class ReportNearMissValidator : AbstractValidator<ReportNearMissCommand>
{
    public ReportNearMissValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(OhsLimits.IncidentDescriptionMaxLength);
    }
}

public sealed class ReportNearMissHandler(
    ITenantContext tenant, ICurrentUser currentUser, IOhsIncidentRepository incidents, TimeProvider clock)
    : ICommandHandler<ReportNearMissCommand, Guid>
{
    public Task<Result<Guid>> Handle(ReportNearMissCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } reporterId)
        {
            return Task.FromResult<Result<Guid>>(Error.Rule(OhsDomainErrors.IncidentReporterNotEmployee));
        }

        var created = OhsIncident.Create(
            tenant.TenantId, OhsIncidentKind.NearMiss, command.EventDate, IncidentClock.Today(clock), command.LocationId,
            employeeId: null, reporterId, command.Description, sgkNotificationDeadline: null);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<Guid>>(created.Error);
        }

        incidents.Add(created.Value);
        return Task.FromResult<Result<Guid>>(created.Value.Id);
    }
}

[RequiresPermission("ohs.incidents.manage")]
public sealed record MarkIncidentSgkNotifiedCommand(Guid IncidentId, DateOnly NotifiedOn) : ICommand;

public sealed class MarkIncidentSgkNotifiedHandler(IOhsIncidentRepository incidents, TimeProvider clock)
    : ICommandHandler<MarkIncidentSgkNotifiedCommand>
{
    public async Task<Result> Handle(MarkIncidentSgkNotifiedCommand command, CancellationToken cancellationToken)
    {
        var incident = await incidents.GetByIdAsync(command.IncidentId, cancellationToken);
        return incident.IsFailure ? incident.Error : incident.Value.MarkSgkNotified(command.NotifiedOn, IncidentClock.Today(clock));
    }
}

[RequiresPermission("ohs.incidents.manage")]
public sealed record CloseIncidentCommand(Guid IncidentId, string CorrectiveAction) : ICommand;

public sealed class CloseIncidentValidator : AbstractValidator<CloseIncidentCommand>
{
    public CloseIncidentValidator()
    {
        RuleFor(x => x.CorrectiveAction).NotEmpty().MaximumLength(OhsLimits.IncidentDescriptionMaxLength);
    }
}

public sealed class CloseIncidentHandler(IOhsIncidentRepository incidents, TimeProvider clock)
    : ICommandHandler<CloseIncidentCommand>
{
    public async Task<Result> Handle(CloseIncidentCommand command, CancellationToken cancellationToken)
    {
        var incident = await incidents.GetByIdAsync(command.IncidentId, cancellationToken);
        return incident.IsFailure ? incident.Error : incident.Value.Close(command.CorrectiveAction, IncidentClock.Today(clock));
    }
}

internal static class IncidentClock
{
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

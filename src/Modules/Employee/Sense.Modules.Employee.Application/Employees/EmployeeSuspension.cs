using FluentValidation;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Contracts;
// Not "using Sense.Modules.Employee.Contracts.Events" - it declares its own SuspensionReason/TerminationType
// (Contracts stays independent of Domain, per architecture rule), which would collide with Domain.Employees'
// versions below; event types (EmployeeHired/EmployeeSuspended/EmployeeReactivated/EmployeeTerminated) are
// referenced as Contracts.Events.* instead.
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Suspends an Active employee (askıya alma); does not touch the Identity account.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record SuspendEmployeeCommand(Guid Id, SuspensionReason Reason) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class SuspendEmployeeValidator : AbstractValidator<SuspendEmployeeCommand>
{
    public SuspendEmployeeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SuspendEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IIntegrationEventOutbox outbox,
    TimeProvider clock,
    EmployeeDetailBuilder details) : ICommandHandler<SuspendEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(SuspendEmployeeCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var suspended = employee.Suspend(command.Reason, clock.GetUtcNow().UtcDateTime);
        if (suspended.IsFailure)
        {
            return suspended.Error;
        }

        outbox.Enqueue(new Contracts.Events.EmployeeSuspended(tenant.TenantId, employee.Id, ToContractReason(command.Reason)) { ActorUserId = currentUser.UserId });
        return await details.BuildAsync(employee, cancellationToken);
    }

    private static Contracts.Events.SuspensionReason ToContractReason(SuspensionReason reason) => reason switch
    {
        SuspensionReason.MilitaryService => Contracts.Events.SuspensionReason.MilitaryService,
        SuspensionReason.UnionOffice => Contracts.Events.SuspensionReason.UnionOffice,
        SuspensionReason.LongUnpaidLeave => Contracts.Events.SuspensionReason.LongUnpaidLeave,
        SuspensionReason.Detention => Contracts.Events.SuspensionReason.Detention,
        SuspensionReason.HealthReport => Contracts.Events.SuspensionReason.HealthReport,
        SuspensionReason.ForceMajeure => Contracts.Events.SuspensionReason.ForceMajeure,
        _ => Contracts.Events.SuspensionReason.Other,
    };
}

/// <summary>Reactivates a Suspended employee back to Active.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record ReactivateEmployeeCommand(Guid Id) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class ReactivateEmployeeValidator : AbstractValidator<ReactivateEmployeeCommand>
{
    public ReactivateEmployeeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class ReactivateEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IIntegrationEventOutbox outbox,
    EmployeeDetailBuilder details) : ICommandHandler<ReactivateEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(ReactivateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var reactivated = employee.Reactivate();
        if (reactivated.IsFailure)
        {
            return reactivated.Error;
        }

        outbox.Enqueue(new Contracts.Events.EmployeeReactivated(tenant.TenantId, employee.Id) { ActorUserId = currentUser.UserId });
        return await details.BuildAsync(employee, cancellationToken);
    }
}

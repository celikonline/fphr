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

/// <summary>Updates an employee record (same body as create); out-of-scope targets return 404 (IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record UpdateEmployeeCommand(
    Guid Id,
    string FirstName,
    string LastName,
    string? NationalId,
    string? WorkEmail,
    string? PersonalEmail,
    string? WorkPhone,
    DateOnly? HireDate,
    DateOnly? BirthDate,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId,
    Gender Gender = Gender.Unspecified,
    MaritalStatus MaritalStatus = MaritalStatus.Unspecified,
    int? ChildCount = null,
    EducationStatus? EducationStatus = null,
    HighestEducationLevel? HighestEducationLevel = null,
    MilitaryStatus? MilitaryStatus = null,
    DisabilityDegree? DisabilityDegree = null,
    BloodType? BloodType = null,
    string? MobilePhone = null,
    string? HomePhone = null,
    string? AddressLine = null,
    string? City = null,
    string? PostalCode = null,
    ContractType? ContractType = null,
    EmploymentType? EmploymentType = null,
    DateOnly? LeaveAccrualStartDate = null) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;

    public EmployeeInfo ToInfo() => new(
        FirstName, LastName, NationalId, WorkEmail, PersonalEmail, WorkPhone, HireDate, BirthDate,
        Gender, MaritalStatus, ChildCount, EducationStatus, HighestEducationLevel, MilitaryStatus, DisabilityDegree, BloodType,
        MobilePhone, HomePhone, AddressLine, City, PostalCode, ContractType, EmploymentType, LeaveAccrualStartDate);
}

public sealed class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeValidator(EmployeeSensitiveAccess sensitiveAccess)
    {
        RuleFor(x => x.Id).NotEmpty();
        EmployeeInfoRules.Apply(this, x => x.ToInfo(), sensitiveAccess);
    }
}

public sealed class UpdateEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    EmployeeReferenceValidator references,
    EmployeeSensitiveAccess sensitiveAccess,
    IIntegrationEventOutbox outbox,
    CareerService career,
    EmployeeDetailBuilder details) : ICommandHandler<UpdateEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(UpdateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var info = command.ToInfo();
        if (!await sensitiveAccess.CanViewAsync(cancellationToken).ConfigureAwait(false))
        {
            info = EmployeeSensitiveAccess.PreserveSensitiveFields(info, employee);
        }

        var checkedRefs = await references.ValidateAsync(info, command.OrgUnitId, command.ManagerEmployeeId, employee, EmployeePermissions.EmployeesUpdateKey, cancellationToken);
        if (checkedRefs.IsFailure)
        {
            return checkedRefs.Error;
        }

        var orgUnitChanged = command.OrgUnitId != employee.OrgUnitId;
        var managerChanged = command.ManagerEmployeeId != employee.ManagerEmployeeId;
        var placementChanged = orgUnitChanged || managerChanged || command.EmploymentType != employee.EmploymentType;
        var previousBirthDate = employee.BirthDate;
        var previousDisabilityDegree = employee.DisabilityDegree;

        // Unit/manager/employment type now live on the career timeline: materialize the initial record first (so a
        // hire-date correction can realign it), then route the change through the aggregate's career rules.
        OrgPlacement? placement = null;
        if (placementChanged || info.HireDate != employee.HireDate)
        {
            placement = await career.LoadPlacementAsync(cancellationToken);
            career.Materialize(employee, placement);
        }

        var updated = employee.UpdateInfo(info);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (placement is not null && !placementChanged)
        {
            // A Draft that just received its first hire date gets its initial record now (a placement change below
            // creates it itself, with the new values).
            career.Materialize(employee, placement);
        }

        if (placementChanged && placement is not null)
        {
            var latest = employee.CareerTimeline.LastOrDefault();
            var assignments = orgUnitChanged || latest is null ? placement.ChainFor(command.OrgUnitId) : latest.AssignmentInputs();
            var changeType = managerChanged && !orgUnitChanged ? CareerChangeType.ManagerChange : CareerChangeType.Other;
            var context = await career.BuildContextAsync(placement, cancellationToken);
            var moved = employee.ApplyPlacement(new PlacementRequest(assignments, command.ManagerEmployeeId, command.EmploymentType, changeType), context);
            if (moved.IsFailure)
            {
                return moved.Error;
            }

            career.Publish(employee, moved.Value);
        }

        if (updated.Value && employee.HireDate is { } hireDate)
        {
            outbox.Enqueue(new Contracts.Events.EmployeeHired(tenant.TenantId, employee.Id, hireDate, employee.LeaveAccrualStartDate ?? hireDate) { ActorUserId = currentUser.UserId });
        }

        if (employee.BirthDate != previousBirthDate || employee.DisabilityDegree != previousDisabilityDegree)
        {
            outbox.Enqueue(new Contracts.Events.EmployeeProfileChanged(tenant.TenantId, employee.Id, Contracts.Events.EmployeeProfileSections.Core)
            {
                ActorUserId = currentUser.UserId,
            });
        }

        return await details.BuildAsync(employee, cancellationToken);
    }
}

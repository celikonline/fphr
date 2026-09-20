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

/// <summary>Creates an employee record.</summary>
[RequiresPermission(EmployeePermissions.EmployeesCreateKey)]
public sealed record CreateEmployeeCommand(
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
    DateOnly? LeaveAccrualStartDate = null,
    bool ClearLeaveAccrualStartDate = false,
    string? SicilNumarasi = null,
    Nationality? Nationality = null,
    string? ForeignIdNumber = null,
    string? PassportNumber = null,
    string? SgkOccupationCode = null) : ICommand<EmployeeDetailDto>
{
    public EmployeeInfo ToInfo() => new(
        FirstName, LastName, NationalId, WorkEmail, PersonalEmail, WorkPhone, HireDate, BirthDate,
        Gender, MaritalStatus, ChildCount, EducationStatus, HighestEducationLevel, MilitaryStatus, DisabilityDegree, BloodType,
        MobilePhone, HomePhone, AddressLine, City, PostalCode, ContractType, EmploymentType, LeaveAccrualStartDate,
        SicilNumarasi, Nationality ?? Domain.Employees.Nationality.Turkish, ForeignIdNumber, PassportNumber, SgkOccupationCode,
        ClearLeaveAccrualStartDate);
}

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator(EmployeeSensitiveAccess sensitiveAccess) => EmployeeInfoRules.Apply(this, x => x.ToInfo(), sensitiveAccess);
}

public sealed class CreateEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    EmployeeReferenceValidator references,
    EmployeeSensitiveAccess sensitiveAccess,
    IIntegrationEventOutbox outbox,
    CareerService career,
    EmployeeDetailBuilder details) : ICommandHandler<CreateEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var info = command.ToInfo();
        if (!await sensitiveAccess.CanViewAsync(cancellationToken).ConfigureAwait(false))
        {
            info = EmployeeSensitiveAccess.PreserveSensitiveFields(info, existing: null);
        }

        var checkedRefs = await references.ValidateAsync(info, command.OrgUnitId, command.ManagerEmployeeId, existing: null, EmployeePermissions.EmployeesCreateKey, cancellationToken);
        if (checkedRefs.IsFailure)
        {
            return checkedRefs.Error;
        }

        var result = EmployeeEntity.Create(tenant.TenantId, info, userId: null, command.OrgUnitId, command.ManagerEmployeeId);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var employee = result.Value;
        employees.Add(employee);

        // Initial "Hire" career record (ValidFrom = HireDate ?? today) when there is anything to place.
        if (employee.HasCareerData)
        {
            var placement = await career.LoadPlacementAsync(cancellationToken);
            career.PublishInitial(employee, employee.EnsureInitialCareerRecord(placement.ChainFor(command.OrgUnitId), career.Today));
        }

        if (employee.Status == EmployeeStatus.Active && employee.HireDate is { } hireDate)
        {
            outbox.Enqueue(new Contracts.Events.EmployeeHired(tenant.TenantId, employee.Id, hireDate, employee.LeaveAccrualStartDate ?? hireDate) { ActorUserId = currentUser.UserId });
        }

        return await details.BuildAsync(employee, cancellationToken);
    }
}

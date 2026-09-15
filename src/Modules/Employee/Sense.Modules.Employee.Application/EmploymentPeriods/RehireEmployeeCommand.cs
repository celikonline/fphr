using FluentValidation;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.EmploymentPeriods;

/// <summary>Rehire response: the refreshed card plus the periods and merged seniority the rehire produced.</summary>
public sealed record RehireEmployeeResultDto(EmployeeDetailDto Employee, EmploymentHistoryDto EmploymentHistory);

/// <summary>
/// Re-hires a terminated employee ("Yeniden işe al", 11.3.03 E5a). <paramref name="MergePreviousPeriods"/> is the
/// wizard's "önceki dönem izin kıdemine sayılsın mı" checkbox: it moves the leave accrual start back by the previous
/// periods that still count (those not settled at exit). <paramref name="LeaveAccrualStartDate"/> overrides the
/// suggestion outright (HR judgement, audited like any other field change).
/// </summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record RehireEmployeeCommand(
    Guid Id,
    DateOnly HireDate,
    bool MergePreviousPeriods = true,
    DateOnly? LeaveAccrualStartDate = null) : ICommand<RehireEmployeeResultDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class RehireEmployeeValidator : AbstractValidator<RehireEmployeeCommand>
{
    public RehireEmployeeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class RehireEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    CareerService career,
    IIntegrationEventOutbox outbox,
    EmployeeDetailBuilder details,
    EmploymentHistoryReader history) : ICommandHandler<RehireEmployeeCommand, RehireEmployeeResultDto>
{
    public async Task<Result<RehireEmployeeResultDto>> Handle(RehireEmployeeCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithEmploymentPeriodsAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var rehired = employee.Rehire(command.HireDate, command.MergePreviousPeriods, command.LeaveAccrualStartDate);
        if (rehired.IsFailure)
        {
            return rehired.Error;
        }

        var careerResult = await AddRehireCareerRecordAsync(employee, command.HireDate, cancellationToken);
        if (careerResult.IsFailure)
        {
            return careerResult.Error;
        }

        outbox.Enqueue(new Contracts.Events.EmployeeRehired(
            tenant.TenantId,
            employee.Id,
            command.HireDate,
            rehired.Value.LeaveAccrualStartDate,
            rehired.Value.SeniorityStartDate,
            command.MergePreviousPeriods) { ActorUserId = currentUser.UserId });

        var dto = await details.BuildAsync(employee, cancellationToken);
        return new RehireEmployeeResultDto(dto, await history.ReadAsync(employee, cancellationToken));
    }

    /// <summary>The new period opens a career record too ("Rehire"), continuing the placement the employee left with.</summary>
    private async Task<Result> AddRehireCareerRecordAsync(Domain.Employees.Employee employee, DateOnly hireDate, CancellationToken ct)
    {
        var placement = await career.LoadPlacementAsync(ct);
        career.Materialize(employee, placement);

        var previous = employee.CareerTimeline.LastOrDefault();
        var context = await career.BuildContextAsync(placement, ct);
        var outcome = employee.AddCareerRecord(
            new CareerRecordData(
                hireDate,
                CareerChangeType.Rehire,
                previous?.EmploymentType ?? employee.EmploymentType,
                previous?.ManagerEmployeeId,
                null,
                previous?.AssignmentInputs() ?? []),
            context);

        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        career.Publish(employee, outcome.Value);
        return Result.Success();
    }
}

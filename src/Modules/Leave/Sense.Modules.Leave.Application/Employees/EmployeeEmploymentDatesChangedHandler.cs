using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Leave.Application.Employees;

/// <summary>
/// EmployeeEmploymentDatesChanged → realigns the leave profile's HireDate/LeaveAccrualStartDate and regenerates the
/// generated accrual rows from the corrected start (senseik-044, C-02: a hire-date correction used to leave the profile
/// accruing from the original date). No profile yet → nothing to do (EmployeeHired creates it with current dates);
/// unchanged dates (outbox redelivery) → no-op.
/// </summary>
public sealed class EmployeeEmploymentDatesChangedHandler(
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    LeaveBalanceService balances,
    ILeaveUnitOfWork unitOfWork) : IIntegrationEventHandler<EmployeeEmploymentDatesChanged>
{
    public async Task Handle(EmployeeEmploymentDatesChanged integrationEvent, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindByEmployeeIdAsync(integrationEvent.EmployeeId, cancellationToken);
        if (profile is null ||
            (profile.HireDate == integrationEvent.HireDate && profile.LeaveAccrualStartDate == integrationEvent.LeaveAccrualStartDate))
        {
            return;
        }

        profile.SetDates(integrationEvent.HireDate, integrationEvent.LeaveAccrualStartDate, profile.BirthDate);

        var ruleSet = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
        if (ruleSet.IsSuccess)
        {
            await balances.RecalculateAsync(profile, ruleSet.Value, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

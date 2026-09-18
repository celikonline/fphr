using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Leave.Application.Employees;

/// <summary>
/// EmployeeHired → leave profile on the tenant's default rule set (seeded on first use) + cumulative accruals from
/// LeaveAccrualStartDate. Idempotent: an outbox redelivery finds the existing profile and does nothing.
/// BirthDate is read through IEmployeeBirthDateLookup (never carried on EmployeeHired - sensitive/KVKK) so the
/// engine's ≤18/≥50 statutory age exception (4857 md.53/IV) can apply from the very first accrual.
/// </summary>
public sealed class EmployeeHiredHandler(
    IEmployeeLeaveProfileRepository profiles,
    LeaveBalanceService balances,
    ILeaveUnitOfWork unitOfWork,
    IEmployeeBirthDateLookup birthDates) : IIntegrationEventHandler<EmployeeHired>
{
    public async Task Handle(EmployeeHired integrationEvent, CancellationToken cancellationToken)
    {
        if (await profiles.FindByEmployeeIdAsync(integrationEvent.EmployeeId, cancellationToken) is not null)
        {
            return;
        }

        var ruleSet = await balances.EnsureDefaultRuleSetAsync(integrationEvent.TenantId, cancellationToken);
        var profile = EmployeeLeaveProfile.Create(
            integrationEvent.TenantId, integrationEvent.EmployeeId, ruleSet.Id, integrationEvent.HireDate, integrationEvent.LeaveAccrualStartDate);
        profiles.Add(profile);

        var birthDate = await birthDates.GetBirthDateAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, cancellationToken);
        profile.SetDates(profile.HireDate, profile.LeaveAccrualStartDate, birthDate);

        await balances.RecalculateAsync(profile, ruleSet, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

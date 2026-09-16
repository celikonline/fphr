using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Payroll.Application.Employees;

/// <summary>
/// EmployeeHired -> initial EmployeePayrollProfile (11.3.07 "Profil tanımı" akışı, P2). Defaults: ValidFrom=HireDate,
/// WageBasis derived from the employee's current salary record via the Contracts port
/// <see cref="IEmployeeSalaryLookup"/> (Monthly if the employee has no salary record yet), SgkStatus=Normal,
/// MealBenefitVariant=Cash (hardcoded - there is no <c>Payroll.Meal.DefaultVariant</c> tenant setting yet, TODO
/// P2+), MealDailyAmount=0, TransportVariant=None, PaymentMethod=Bank, SickDaysReducePay = WageBasis != Monthly
/// (maktu ücrette hastalık günleri ücretten/SGK gününden düşülmez, 11.3.07 "Ücret esası" - this default only
/// applies here; a manual edit via payroll.profile.manage can override it).
///
/// SgkWorkplaceCompanyId (required by the entity, see EmployeePayrollProfile.Validate) is resolved to the tenant's
/// Company-level root OrgUnit via Organization's IOrgUnitLookup ("Active hierarchy levels ordered by SortOrder
/// (Company first)" - Faz 0 seeds exactly one Company per tenant). TODO P2+: multi-company tenants need the
/// employee's own OrgUnit walked to its nearest Company ancestor instead of taking the tenant's only one; there is
/// no cross-module "employee's SGK workplace" concept yet, so this auto-creation intentionally does not attempt
/// per-employee SGK-workplace assignment (11.3.07-bordro.md leaves it to manual profile edits, payroll.profile.manage).
///
/// Idempotent: an outbox redelivery finds an existing profile and does nothing. If the tenant has no Company row yet
/// (not fully provisioned) the event is skipped - manual creation via payroll.profile.manage still works once one exists.
/// </summary>
public sealed class EmployeeHiredHandler(
    IEmployeePayrollProfileRepository profiles,
    IEmployeeSalaryLookup salaryLookup,
    IOrgUnitLookup orgUnits,
    IPayrollUnitOfWork unitOfWork) : IIntegrationEventHandler<EmployeeHired>
{
    public async Task Handle(EmployeeHired integrationEvent, CancellationToken cancellationToken)
    {
        var existing = await profiles.ListByEmployeeAsync(integrationEvent.EmployeeId, cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var companyOrgUnitId = await ResolveDefaultSgkWorkplaceCompanyIdAsync(integrationEvent.TenantId, cancellationToken);
        if (companyOrgUnitId is null)
        {
            return;
        }

        var payPeriod = await salaryLookup.GetCurrentPayPeriodAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, cancellationToken);
        var wageBasis = MapWageBasis(payPeriod?.PayFrequency);

        var data = new EmployeePayrollProfileData(
            SgkWorkplaceCompanyId: companyOrgUnitId.Value,
            SgkWorkplaceLocationId: null,
            ValidFrom: integrationEvent.HireDate,
            ValidTo: null,
            WageBasis: wageBasis,
            SgkStatus: SgkStatus.Normal,
            IncentiveLawCode: null,
            MealBenefitVariant: MealBenefitVariant.Cash,
            MealDailyAmount: 0m,
            TransportVariant: TransportVariant.None,
            PaymentMethod: PayrollPaymentMethod.Bank,
            BankAccountId: null,
            SickDaysReducePay: wageBasis != WageBasis.Monthly,
            ExternalPayrollCode: null,
            Notes: null);

        var created = EmployeePayrollProfile.Create(integrationEvent.TenantId, integrationEvent.EmployeeId, data);
        if (created.IsFailure)
        {
            // Defensive only: every field above is either fixed or event-derived, none of the operator-entered
            // validations (SGK workplace/meal amount/code length) can actually fail from this call site.
            return;
        }

        profiles.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static WageBasis MapWageBasis(EmployeePayFrequency? payFrequency) => payFrequency switch
    {
        EmployeePayFrequency.Daily => WageBasis.Daily,
        EmployeePayFrequency.Weekly => WageBasis.Weekly,
        EmployeePayFrequency.Hourly => WageBasis.Hourly,
        EmployeePayFrequency.Yearly => WageBasis.Yearly,
        _ => WageBasis.Monthly,
    };

    private async Task<Guid?> ResolveDefaultSgkWorkplaceCompanyIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var types = await orgUnits.ListActiveTypesAsync(tenantId, cancellationToken);
        var companyType = types.OrderBy(t => t.SortOrder).FirstOrDefault();
        if (companyType is null)
        {
            return null;
        }

        var units = await orgUnits.ListActiveUnitsAsync(tenantId, cancellationToken);
        return units.FirstOrDefault(u => u.TypeId == companyType.Id)?.Id;
    }
}

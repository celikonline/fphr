using Sense.Modules.Payroll.Domain.Incentives;

namespace Sense.Modules.Payroll.Application.Incentives;

/// <summary>
/// The teşvik kanunu kodu a declaration (APHB) carries for an employee in a month: the declaration code of the SGK-category
/// assignment in force (even when its definition is not approved, since the code is declared regardless of the calculation),
/// otherwise the payroll profile's own incentive law code.
/// </summary>
public static class IncentiveDeclaration
{
    public static string? CodeFor(IncentiveMonthPlan plan, Guid employeeId, string? profileLawCode) =>
        plan.EffectiveOfCategory(employeeId, IncentiveCategory.Sgk)?.Definition.DeclarationCode ?? profileLawCode;
}

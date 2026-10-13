namespace Sense.Modules.Payroll.Domain.Incentives;

/// <summary>What an incentive does to the payroll (PAY-INCENTIVE-PLAN "IncentiveDefinition"). The category follows from the kind.</summary>
public enum IncentiveKind
{
    /// <summary>(a) Employer SGK discount: a rate applied to the SGK base.</summary>
    EmployerSgkRateDiscount = 0,

    /// <summary>(b) The state covers (part of) the employer/employee SGK and unemployment premiums.</summary>
    EmployerShareCoverage = 1,

    /// <summary>(c) A share of the income tax withheld is relieved (terkin).</summary>
    IncomeTaxWithholdingRelief = 2,

    /// <summary>(d) Only the declaration code is reported; the calculation is untouched.</summary>
    DeclarationOnly = 3,
}

/// <summary>Incentives of one category cannot overlap for an employee: at most one SGK and one tax incentive per month.</summary>
public enum IncentiveCategory
{
    Sgk = 0,
    Tax = 1,
}

/// <summary>The premium base used when the state covers premiums (<see cref="IncentiveKind.EmployerShareCoverage"/>).</summary>
public enum IncentiveCoverageBase
{
    ActualSgkBase = 0,
    MinimumWage = 1,
}

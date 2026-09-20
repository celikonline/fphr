namespace Sense.Modules.Payroll.Domain.Unions;

/// <summary>How the employee benefits from a collective agreement (TİS) at this workplace (11.3.7 "Sendika / TİS").</summary>
public enum UnionCoverageType
{
    /// <summary>Dues-paying union member ("üye"); pays the standard union dues (`UNION` pay item).</summary>
    Member = 0,

    /// <summary>Non-member who still benefits from the collective agreement; pays solidarity dues instead ("dayanışma aidatlı", `SOLIDARITY_DUES`).</summary>
    SolidarityDues = 1,

    /// <summary>Union-approved exception (e.g. a manager the union agreed to exclude from dues) - benefits without paying.</summary>
    UnionApproved = 2,

    /// <summary>Outside the collective agreement's scope entirely - no dues, no coverage.</summary>
    Excluded = 3,
}

/// <summary>How <see cref="EmployeeUnionMembership.DuesValue"/> is interpreted (11.3.7 "aidat kuralı").</summary>
public enum UnionDuesRule
{
    /// <summary>DuesValue is a fraction of the gross monthly salary (e.g. 0.01 = 1%).</summary>
    PercentOfGross = 0,

    /// <summary>DuesValue is a fixed currency amount per period.</summary>
    FixedAmount = 1,

    /// <summary>DuesValue is a number of days' wage (gross monthly salary / calendar days in month) per period (yevmiye).</summary>
    DailyWage = 2,
}

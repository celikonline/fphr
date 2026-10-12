namespace Sense.Modules.Payroll.Domain.PayItems;

/// <summary>Whether a pay item adds to or subtracts from the payslip. Only <see cref="Earning"/> is supported today.</summary>
public enum PayItemDirection
{
    Earning = 0,

    /// <summary>Reserved for the extra deduction formulas; creating one is rejected (<c>pay_item_direction_not_supported</c>).</summary>
    Deduction = 1,
}

/// <summary>Whether the formula result is a gross amount or the net amount the employee should receive.</summary>
public enum PayItemAmountBasis
{
    Gross = 0,
    Net = 1,
}

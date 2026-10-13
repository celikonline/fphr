namespace Sense.Modules.Payroll.Domain.PayItems;

/// <summary>Whether a pay item adds to or subtracts from the payslip.</summary>
public enum PayItemDirection
{
    Earning = 0,

    /// <summary>A net deduction (PAY-DEDUCTION): taken off the net after the taxes, never part of the SGK/GV/damga bases. Tax flags are normalized to false and the basis is always gross.</summary>
    Deduction = 1,
}

/// <summary>Whether the formula result is a gross amount or the net amount the employee should receive.</summary>
public enum PayItemAmountBasis
{
    Gross = 0,
    Net = 1,
}

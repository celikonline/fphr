namespace Sense.Modules.Payroll.Domain;

/// <summary>Payment cadence the salary is expressed in (11.3.7 "Ödeme düzeni"). Only <see cref="Monthly"/> is used by the P1 calculation engine.</summary>
public enum WageBasis
{
    Monthly = 0,
    Daily = 1,
    Weekly = 2,
    Hourly = 3,
    Yearly = 4,
}

/// <summary>Period lifecycle (11.3.7 dönem akışı). Only <see cref="Open"/> is exercised by the P1 slice.</summary>
public enum PayrollPeriodStatus
{
    Open = 0,
    Approved = 1,
    Paid = 2,
    Locked = 3,
}

/// <summary>
/// Meal benefit payment shape (11.3.7 "Yemek ödemesi 4 varyantı"). Only <see cref="None"/> and <see cref="Cash"/> are
/// implemented by <see cref="Calculation.PayrollCalculator"/> in this slice; the other three are deferred to P2
/// (yemek varyantları) and make <see cref="Calculation.PayrollCalculator.Calculate"/> throw <see cref="NotSupportedException"/>.
/// </summary>
public enum MealBenefitVariant
{
    None = 0,
    Cash = 1,
    InKind = 2,
    MealCardOnly = 3,
    MealAndMarketCard = 4,
}

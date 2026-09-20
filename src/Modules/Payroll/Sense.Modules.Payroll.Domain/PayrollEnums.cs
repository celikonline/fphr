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
/// Meal benefit payment shape (11.3.7 "Yemek ödemesi 4 varyantı"), all implemented by <see cref="Calculation.PayrollCalculator"/>
/// (P2): <see cref="Cash"/>/<see cref="MealCardOnly"/> exempt up to the daily thresholds and pay the excess in net;
/// <see cref="InKind"/> is fully outside the SGK/GV base and not paid in cash (informational only); <see cref="MealAndMarketCard"/>
/// is fully taxed unless <see cref="Calculation.PayrollLegislationSnapshot.MealMarketCardExemptionApplies"/> says otherwise.
/// </summary>
public enum MealBenefitVariant
{
    None = 0,
    Cash = 1,
    InKind = 2,
    MealCardOnly = 3,
    MealAndMarketCard = 4,
}

/// <summary>SGK sicil durumu (11.3.7 "SGK statüsü"): affects incentive/discount eligibility (not applied yet, TODO P2+).</summary>
public enum SgkStatus
{
    Normal = 0,
    Retired = 1,
    Intern = 2,
    Apprentice = 3,
    Foreign = 4,
}

/// <summary>Yol yardımı varyantı (11.3.7): nakit / kart / servis / yok.</summary>
public enum TransportVariant
{
    None = 0,
    Cash = 1,
    Card = 2,
    Shuttle = 3,
}

/// <summary>Ödeme yöntemi (11.3.7 "Ödeme yöntemi"): banka veya elden.</summary>
public enum PayrollPaymentMethod
{
    Bank = 0,
    Cash = 1,
}

/// <summary>İcra/nafaka kesinti emri türü (senseik-019 2c, İİK m.83): nafakada kesinti oranı sınırı yoktur, icrada
/// net ücretin 1/4'ü ile sınırlıdır - bkz. <see cref="Calculation.GarnishmentCalculator"/>.</summary>
public enum GarnishmentType
{
    Icra = 0,
    Nafaka = 1,
}

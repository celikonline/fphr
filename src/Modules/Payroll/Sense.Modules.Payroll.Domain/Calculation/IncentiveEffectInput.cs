using Sense.Modules.Payroll.Domain.Incentives;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// A rate already resolved by the Application layer: <paramref name="Value"/> plus the key shown in the trace. For a rate that
/// references a <c>LegislationParameter</c> the key is that parameter's key; for a literal (SMMM-approved tenant data) it is
/// <c>Incentive.{Code}.Rate</c>.
/// </summary>
public sealed record ResolvedRate(decimal Value, string ParameterKey);

/// <summary>
/// One approved, in-force incentive effect handed to the pure engine (PAY-INCENTIVE-PLAN "Hesaba entegrasyon"). Only kinds (a)
/// <see cref="IncentiveKind.EmployerSgkRateDiscount"/>, (b) <see cref="IncentiveKind.EmployerShareCoverage"/> and (c)
/// <see cref="IncentiveKind.IncomeTaxWithholdingRelief"/> are ever passed; (d) declaration-only never reaches the calculation.
/// </summary>
/// <param name="Code">Incentive definition code (shown in the trace).</param>
/// <param name="Kind">What the incentive does.</param>
/// <param name="Rate">(a) SGK discount rate; (b) share of the premiums the state covers (1 = all); (c) share of the income tax relieved.</param>
/// <param name="CoverageBase">(b) premium base: actual SGK base or the minimum wage (capped).</param>
/// <param name="CapMultiplier">(b) with a minimum-wage base: multiple of the (prorated) minimum wage that caps the base.</param>
/// <param name="EmployerSgkRate">(b) employer SGK rate used for the covered employer SGK premium.</param>
/// <param name="CoversEmployerSgk">(b) the employer SGK premium is covered.</param>
/// <param name="CoversEmployerUnemployment">(b) the employer unemployment premium is covered.</param>
/// <param name="CoversEmployeeSgk">(b) the employee SGK premium is covered.</param>
/// <param name="CoversEmployeeUnemployment">(b) the employee unemployment premium is covered.</param>
public sealed record IncentiveEffectInput(
    string Code,
    IncentiveKind Kind,
    ResolvedRate? Rate,
    IncentiveCoverageBase? CoverageBase = null,
    decimal? CapMultiplier = null,
    ResolvedRate? EmployerSgkRate = null,
    bool CoversEmployerSgk = false,
    bool CoversEmployerUnemployment = false,
    bool CoversEmployeeSgk = false,
    bool CoversEmployeeUnemployment = false,
    decimal? MaxSupportMultiplier = null);

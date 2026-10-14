using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Profiles;

/// <summary>Editable content of an <see cref="EmployeePayrollProfile"/> (11.3.7 "Bordro parametresi").</summary>
public sealed record EmployeePayrollProfileData(
    Guid SgkWorkplaceCompanyId,
    Guid? SgkWorkplaceLocationId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    WageBasis WageBasis,
    SgkStatus SgkStatus,
    string? IncentiveLawCode,
    MealBenefitVariant MealBenefitVariant,
    decimal MealDailyAmount,
    TransportVariant TransportVariant,
    PayrollPaymentMethod PaymentMethod,
    Guid? BankAccountId,
    bool SickDaysReducePay,
    string? ExternalPayrollCode,
    string? Notes,
    decimal TransportDailyAmount = 0m,
    decimal RegularBenefitsMonthlyGross = 0m);

/// <summary>
/// One dated version of an employee's payroll parameters (11.3.7 "Bordro parametresi", P2): SGK workplace, wage basis,
/// SGK status, meal/transport benefit shape and payment method. Amounts (salary, transport/meal allowance) stay in
/// Employee's SalaryHistory (11.3.3, cross-module) - this profile only selects which rule the calculation engine
/// applies. Cross-module reference to Employee, so this is its own aggregate (not a child of Employee's aggregate);
/// non-overlap across an employee's profiles is enforced by the Application layer via the repository (no parent
/// aggregate here to hold every sibling in memory), mirroring how Employee.SalaryRecord enforces its own invariant
/// inside the Employee aggregate it belongs to.
/// </summary>
public sealed class EmployeePayrollProfile : TenantAggregateRoot<Guid>
{
    private EmployeePayrollProfile()
    {
    }

    private EmployeePayrollProfile(Guid id, Guid tenantId, Guid employeeId, EmployeePayrollProfileData data)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Apply(data);
    }

    public Guid EmployeeId { get; private set; }

    public Guid SgkWorkplaceCompanyId { get; private set; }

    public Guid? SgkWorkplaceLocationId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    /// <summary>Null = still in effect.</summary>
    public DateOnly? ValidTo { get; private set; }

    public WageBasis WageBasis { get; private set; }

    public SgkStatus SgkStatus { get; private set; }

    public string? IncentiveLawCode { get; private set; }

    public MealBenefitVariant MealBenefitVariant { get; private set; }

    /// <summary>Daily amount for variants that pay one (Cash/MealCardOnly/MealAndMarketCard); 0 for None/InKind.</summary>
    public decimal MealDailyAmount { get; private set; }

    public TransportVariant TransportVariant { get; private set; }

    /// <summary>Cash transport allowance per worked day (only for the Cash variant; card and shuttle are in kind and add no pay); 0 otherwise.</summary>
    public decimal TransportDailyAmount { get; private set; }

    /// <summary>Declared monthly gross total of recurring benefits included in severance/notice; not a new payslip earning.</summary>
    public decimal RegularBenefitsMonthlyGross { get; private set; }

    public PayrollPaymentMethod PaymentMethod { get; private set; }

    /// <summary>Cross-module reference to Employee.BankAccount (11.3.3); required when <see cref="PaymentMethod"/> is Bank (Application-layer check, not enforced here).</summary>
    public Guid? BankAccountId { get; private set; }

    public bool SickDaysReducePay { get; private set; }

    public string? ExternalPayrollCode { get; private set; }

    public string? Notes { get; private set; }

    public bool IsOpen => ValidTo is null;

    /// <summary>True when [<paramref name="validFrom"/>, <paramref name="validTo"/>] shares any day with this record's own range.</summary>
    public bool Overlaps(DateOnly validFrom, DateOnly? validTo) =>
        validFrom <= (ValidTo ?? DateOnly.MaxValue) && ValidFrom <= (validTo ?? DateOnly.MaxValue);

    public static Result<EmployeePayrollProfile> Create(Guid tenantId, Guid employeeId, EmployeePayrollProfileData data)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);
        var validated = Validate(data);
        return validated.IsFailure ? validated.Error : new EmployeePayrollProfile(Guid.NewGuid(), tenantId, employeeId, data);
    }

    /// <summary>Applies edited content; returns true when something actually changed.</summary>
    public Result<bool> Update(EmployeePayrollProfileData data)
    {
        var validated = Validate(data);
        return validated.IsFailure ? validated.Error : Result.Success(Apply(data));
    }

    public Result<bool> SetRegularBenefitsMonthlyGross(decimal amount)
    {
        if (amount is < 0m or > 999_999_999_999.99m) return Error.Validation(PayrollDomainErrors.ProfileRegularBenefitsInvalid);
        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var changed = RegularBenefitsMonthlyGross != amount;
        RegularBenefitsMonthlyGross = amount;
        return Result.Success(changed);
    }

    /// <summary>Closes the profile at <paramref name="validTo"/> (a replacing record normally starts the next day).</summary>
    public Result Close(DateOnly validTo)
    {
        if (validTo < ValidFrom)
        {
            return Error.Validation(PayrollDomainErrors.ProfileDateRangeInvalid);
        }

        ValidTo = validTo;
        return Result.Success();
    }

    private bool Apply(EmployeePayrollProfileData data)
    {
        var mealDailyAmount = RequiresMealAmount(data.MealBenefitVariant) ? data.MealDailyAmount : 0m;
        var transportDailyAmount = data.TransportVariant == TransportVariant.Cash ? data.TransportDailyAmount : 0m;
        var regularBenefits = decimal.Round(data.RegularBenefitsMonthlyGross, 2, MidpointRounding.AwayFromZero);
        var incentiveLawCode = Trim(data.IncentiveLawCode);
        var externalPayrollCode = Trim(data.ExternalPayrollCode);
        var notes = Trim(data.Notes);

        var changed = SgkWorkplaceCompanyId != data.SgkWorkplaceCompanyId ||
                      SgkWorkplaceLocationId != data.SgkWorkplaceLocationId ||
                      ValidFrom != data.ValidFrom ||
                      ValidTo != data.ValidTo ||
                      WageBasis != data.WageBasis ||
                      SgkStatus != data.SgkStatus ||
                      !string.Equals(IncentiveLawCode, incentiveLawCode, StringComparison.Ordinal) ||
                      MealBenefitVariant != data.MealBenefitVariant ||
                      MealDailyAmount != mealDailyAmount ||
                      TransportVariant != data.TransportVariant ||
                      TransportDailyAmount != transportDailyAmount ||
                      RegularBenefitsMonthlyGross != regularBenefits ||
                      PaymentMethod != data.PaymentMethod ||
                      BankAccountId != data.BankAccountId ||
                      SickDaysReducePay != data.SickDaysReducePay ||
                      !string.Equals(ExternalPayrollCode, externalPayrollCode, StringComparison.Ordinal) ||
                      !string.Equals(Notes, notes, StringComparison.Ordinal);

        SgkWorkplaceCompanyId = data.SgkWorkplaceCompanyId;
        SgkWorkplaceLocationId = data.SgkWorkplaceLocationId;
        ValidFrom = data.ValidFrom;
        ValidTo = data.ValidTo;
        WageBasis = data.WageBasis;
        SgkStatus = data.SgkStatus;
        IncentiveLawCode = incentiveLawCode;
        MealBenefitVariant = data.MealBenefitVariant;
        MealDailyAmount = mealDailyAmount;
        TransportVariant = data.TransportVariant;
        TransportDailyAmount = transportDailyAmount;
        RegularBenefitsMonthlyGross = regularBenefits;
        PaymentMethod = data.PaymentMethod;
        BankAccountId = data.BankAccountId;
        SickDaysReducePay = data.SickDaysReducePay;
        ExternalPayrollCode = externalPayrollCode;
        Notes = notes;
        return changed;
    }

    /// <summary>Content rules that do not need sibling records (overlap is checked by the caller via the repository).</summary>
    public static Result Validate(EmployeePayrollProfileData data)
    {
        if (data.RegularBenefitsMonthlyGross is < 0m or > 999_999_999_999.99m)
        {
            return Error.Validation(PayrollDomainErrors.ProfileRegularBenefitsInvalid);
        }

        if (data.SgkWorkplaceCompanyId == Guid.Empty)
        {
            return Error.Validation(PayrollDomainErrors.ProfileSgkWorkplaceRequired);
        }

        if (data.ValidTo is { } validTo && validTo < data.ValidFrom)
        {
            return Error.Validation(PayrollDomainErrors.ProfileDateRangeInvalid);
        }

        if (data.MealDailyAmount < 0m || (RequiresMealAmount(data.MealBenefitVariant) && data.MealDailyAmount <= 0m))
        {
            return Error.Validation(PayrollDomainErrors.ProfileMealAmountInvalid);
        }

        if (data.TransportDailyAmount < 0m || (data.TransportVariant == TransportVariant.Cash && data.TransportDailyAmount <= 0m))
        {
            return Error.Validation(PayrollDomainErrors.ProfileTransportAmountInvalid);
        }

        if (Trim(data.ExternalPayrollCode) is { Length: > PayrollLimits.ExternalPayrollCodeMaxLength })
        {
            return Error.Validation(PayrollDomainErrors.ProfileExternalCodeTooLong);
        }

        if (Trim(data.Notes) is { Length: > PayrollLimits.NotesMaxLength })
        {
            return Error.Validation(PayrollDomainErrors.ProfileNotesTooLong);
        }

        return Result.Success();
    }

    /// <summary>Only these variants pay a daily amount (11.3.07-bordro.md "Yemek varyant kuralları"); InKind/None are informational or absent.</summary>
    public static bool RequiresMealAmount(MealBenefitVariant variant) =>
        variant is MealBenefitVariant.Cash or MealBenefitVariant.MealCardOnly or MealBenefitVariant.MealAndMarketCard;

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

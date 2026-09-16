using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Profiles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Domain-only tests for <see cref="EmployeePayrollProfile"/> (Faz 3 P2, 11.3.07-bordro.md "Bordro parametresi").</summary>
public sealed class EmployeePayrollProfileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid SgkWorkplaceCompanyId = Guid.NewGuid();

    private static EmployeePayrollProfileData Data(
        DateOnly? validFrom = null, DateOnly? validTo = null,
        MealBenefitVariant mealVariant = MealBenefitVariant.None, decimal mealDailyAmount = 0m,
        Guid? sgkWorkplaceCompanyId = null) =>
        new(
            sgkWorkplaceCompanyId ?? SgkWorkplaceCompanyId, null,
            validFrom ?? new DateOnly(2026, 1, 1), validTo,
            WageBasis.Monthly, SgkStatus.Normal, null,
            mealVariant, mealDailyAmount,
            TransportVariant.None, PayrollPaymentMethod.Bank, null,
            SickDaysReducePay: false, ExternalPayrollCode: null, Notes: null);

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data());

        result.IsSuccess.ShouldBeTrue();
        result.Value.EmployeeId.ShouldBe(EmployeeId);
        result.Value.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithoutSgkWorkplace_Fails()
    {
        var result = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data(sgkWorkplaceCompanyId: Guid.Empty));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.ProfileSgkWorkplaceRequired);
    }

    [Fact]
    public void Create_WithValidToBeforeValidFrom_Fails()
    {
        var result = EmployeePayrollProfile.Create(
            TenantId, EmployeeId, Data(validFrom: new DateOnly(2026, 3, 1), validTo: new DateOnly(2026, 1, 1)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.ProfileDateRangeInvalid);
    }

    [Theory]
    [InlineData(MealBenefitVariant.Cash)]
    [InlineData(MealBenefitVariant.MealCardOnly)]
    [InlineData(MealBenefitVariant.MealAndMarketCard)]
    public void Create_CashVariantWithoutAmount_Fails(MealBenefitVariant variant)
    {
        var result = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data(mealVariant: variant, mealDailyAmount: 0m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.ProfileMealAmountInvalid);
    }

    [Fact]
    public void Create_InKindVariant_NormalizesDailyAmountToZero()
    {
        // Ayni yemek is informational only (no daily amount tracked here, 11.3.07-bordro.md), so even a caller-sent
        // amount is dropped.
        var result = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data(mealVariant: MealBenefitVariant.InKind, mealDailyAmount: 100m));

        result.IsSuccess.ShouldBeTrue();
        result.Value.MealDailyAmount.ShouldBe(0m);
    }

    [Fact]
    public void Overlaps_SameRange_ReturnsTrue()
    {
        var profile = EmployeePayrollProfile.Create(
            TenantId, EmployeeId, Data(validFrom: new DateOnly(2026, 1, 1), validTo: new DateOnly(2026, 6, 30))).Value;

        profile.Overlaps(new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31)).ShouldBeTrue();
        profile.Overlaps(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)).ShouldBeFalse();
        profile.Overlaps(new DateOnly(2026, 7, 1), null).ShouldBeFalse();
    }

    [Fact]
    public void Overlaps_OpenEndedProfile_ExtendsToInfinity()
    {
        var profile = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data(validFrom: new DateOnly(2026, 1, 1))).Value;

        profile.Overlaps(new DateOnly(2030, 1, 1), null).ShouldBeTrue();
    }

    [Fact]
    public void Update_ChangesFields_ReturnsTrue()
    {
        var profile = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data()).Value;

        var updated = profile.Update(Data(mealVariant: MealBenefitVariant.Cash, mealDailyAmount: 150m));

        updated.IsSuccess.ShouldBeTrue();
        updated.Value.ShouldBeTrue();
        profile.MealBenefitVariant.ShouldBe(MealBenefitVariant.Cash);
        profile.MealDailyAmount.ShouldBe(150m);
    }

    [Fact]
    public void Update_WithSameData_ReturnsFalse()
    {
        var data = Data();
        var profile = EmployeePayrollProfile.Create(TenantId, EmployeeId, data).Value;

        var updated = profile.Update(data);

        updated.IsSuccess.ShouldBeTrue();
        updated.Value.ShouldBeFalse();
    }

    [Fact]
    public void Close_SetsValidTo()
    {
        var profile = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data(validFrom: new DateOnly(2026, 1, 1))).Value;

        var closed = profile.Close(new DateOnly(2026, 6, 30));

        closed.IsSuccess.ShouldBeTrue();
        profile.IsOpen.ShouldBeFalse();
        profile.ValidTo.ShouldBe(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void Close_BeforeValidFrom_Fails()
    {
        var profile = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data(validFrom: new DateOnly(2026, 3, 1))).Value;

        var closed = profile.Close(new DateOnly(2026, 1, 1));

        closed.IsFailure.ShouldBeTrue();
        closed.Error.Code.ShouldBe(PayrollDomainErrors.ProfileDateRangeInvalid);
    }
}

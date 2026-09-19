using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>Kart senseik-012 §1/§3: ücret oranı seçimi + serbest zaman 6 ay kullanım süresi (m.41).</summary>
public sealed class OvertimePayRateAndExpiryTests
{
    private static readonly DateOnly WorkDate = new(2026, 9, 14);

    private static OvertimeRequest Submit(OvertimePayRateKind? kind = null)
    {
        var result = kind is { } k
            ? OvertimeRequest.Submit(
                Guid.NewGuid(), Guid.NewGuid(), WorkDate, new TimeOnly(17, 0), 120, OvertimeDayKind.Workday, 0, null,
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null, k)
            : OvertimeRequest.Submit(
                Guid.NewGuid(), Guid.NewGuid(), WorkDate, new TimeOnly(17, 0), 120, OvertimeDayKind.Workday, 0, null,
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null);
        return result.Value;
    }

    [Fact]
    public void Submit_DefaultsToRate50_WithOnePointFiveMultiplier()
    {
        var request = Submit();

        request.PayRateKind.ShouldBe(OvertimePayRateKind.Rate50);
        request.PayRateMultiplier.ShouldBe(1.5m);
    }

    [Fact]
    public void Submit_Rate25_HasOnePointTwoFiveMultiplier()
    {
        var request = Submit(OvertimePayRateKind.Rate25);

        request.PayRateKind.ShouldBe(OvertimePayRateKind.Rate25);
        request.PayRateMultiplier.ShouldBe(1.25m);
    }

    [Fact]
    public void ChangePayRateKind_WhilePending_UpdatesKindAndMultiplier()
    {
        var request = Submit(OvertimePayRateKind.Rate50);

        var result = request.ChangePayRateKind(OvertimePayRateKind.Rate25);

        result.IsSuccess.ShouldBeTrue();
        request.PayRateKind.ShouldBe(OvertimePayRateKind.Rate25);
        request.PayRateMultiplier.ShouldBe(1.25m);
    }

    [Fact]
    public void ChangePayRateKind_AfterApproval_Fails()
    {
        var request = Submit();
        var approval = Guid.NewGuid();
        request.AttachApproval(approval);
        request.Approve(approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var result = request.ChangePayRateKind(OvertimePayRateKind.Rate25);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveDomainErrors.OvertimeNotPending);
    }

    private static OvertimeConversion ApprovedConversion(DateOnly? usedByDate)
    {
        var request = Submit();
        var approval = Guid.NewGuid();
        request.AttachApproval(approval);
        request.Approve(approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        return OvertimeConversion.ToTimeOff(request, OvertimeMinutesBasis.Payable, 1.5m, 450, Guid.NewGuid(), null, DateTime.UtcNow, null, usedByDate).Value;
    }

    [Fact]
    public void ToTimeOff_SetsUsedByDate_WhenProvided()
    {
        var conversion = ApprovedConversion(new DateOnly(2027, 3, 14));

        conversion.UsedByDate.ShouldBe(new DateOnly(2027, 3, 14));
        conversion.Status.ShouldBe(OvertimeConversionStatus.Posted);
    }

    [Fact]
    public void ExpireIfPastDue_BeforeDueDate_StaysPosted()
    {
        var conversion = ApprovedConversion(new DateOnly(2027, 3, 14));

        conversion.ExpireIfPastDue(new DateOnly(2027, 3, 14));

        conversion.Status.ShouldBe(OvertimeConversionStatus.Posted);
    }

    [Fact]
    public void ExpireIfPastDue_OneDayAfterDueDate_BecomesExpired()
    {
        var conversion = ApprovedConversion(new DateOnly(2027, 3, 14));

        conversion.ExpireIfPastDue(new DateOnly(2027, 3, 15));

        conversion.Status.ShouldBe(OvertimeConversionStatus.Expired);
    }

    [Fact]
    public void ExpireIfPastDue_NoUsedByDate_NeverExpires()
    {
        var conversion = ApprovedConversion(null);

        conversion.ExpireIfPastDue(new DateOnly(2099, 1, 1));

        conversion.Status.ShouldBe(OvertimeConversionStatus.Posted);
    }

    [Fact]
    public void ExpireIfPastDue_AlreadyExpired_IsIdempotent()
    {
        var conversion = ApprovedConversion(new DateOnly(2027, 3, 14));
        conversion.ExpireIfPastDue(new DateOnly(2027, 3, 15));

        conversion.ExpireIfPastDue(new DateOnly(2027, 3, 20));

        conversion.Status.ShouldBe(OvertimeConversionStatus.Expired);
    }

    // 6 ay kuralı ay-sonu kenar durumu: 31 Ağustos'tan 6 ay sonrası, Şubat'ın son gününe (28/29) düşürülür
    // (DateOnly.AddMonths, DateTime.AddMonths ile aynı "clip to month end" davranışını taşır).
    [Theory]
    [InlineData(2026, 8, 31, 2027, 2, 28)] // 2027 artık yıl değil.
    [InlineData(2027, 8, 31, 2028, 2, 29)] // 2028 artık yıl.
    public void UsedByDate_SixMonthsFromMonthEnd_ClipsToTargetMonthsLastDay(
        int fromYear, int fromMonth, int fromDay, int expectedYear, int expectedMonth, int expectedDay)
    {
        var convertedAt = new DateOnly(fromYear, fromMonth, fromDay);

        var usedByDate = convertedAt.AddMonths(6);

        usedByDate.ShouldBe(new DateOnly(expectedYear, expectedMonth, expectedDay));
    }
}

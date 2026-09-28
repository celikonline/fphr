using Sense.Api.SeedData;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Shouldly;
using Xunit;

namespace Sense.Tests.Unit.SeedData;

/// <summary>
/// A11 (izin modülü denetimi): the seeded overtime conversion shown under "İzne dönüşümler" was a Kind=Pay row
/// (2 sa, 0 gün, izin türü —). The demo employee's request must now be a real time-off conversion
/// (2 sa × 1,25 = 2 sa 30 dk, gün karşılığı, izin türü, ExtraGrant defter satırı). No database needed.
/// </summary>
public sealed class DemoOvertimeSeedTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid AnnualTypeId = Guid.NewGuid();
    private static readonly DateOnly WorkDate = new(2026, 9, 7);

    private static DataSeeder.DemoOvertime Build(DataSeeder.DemoOvertimeConversionMode mode) =>
        DataSeeder.BuildDemoOvertime(
            TenantId, EmployeeId, WorkDate, new TimeOnly(18, 0), 120, OvertimePayRateKind.Rate25, "Ay sonu kapanışı", mode, AnnualTypeId);

    [Fact]
    public void TimeOffConversion_ConvertsTwoHoursAtOnePointTwoFive_ToTwoHoursThirtyMinutesAndDays()
    {
        var seeded = Build(DataSeeder.DemoOvertimeConversionMode.TimeOff);

        var conversion = seeded.Conversion.ShouldNotBeNull();
        conversion.Kind.ShouldBe(OvertimeConversionKind.TimeOff);
        conversion.RateSnapshot.ShouldBe(1.25m);
        conversion.BasisMinutes.ShouldBe(120);
        conversion.ResultMinutes.ShouldBe(150);
        conversion.ResultDays.ShouldBeGreaterThan(0m);
        conversion.LeaveTypeId.ShouldBe(AnnualTypeId);
        seeded.Request.ConversionId.ShouldBe(conversion.Id);
    }

    [Fact]
    public void TimeOffConversion_PostsMatchingExtraGrantLedgerRow()
    {
        var seeded = Build(DataSeeder.DemoOvertimeConversionMode.TimeOff);

        var conversion = seeded.Conversion.ShouldNotBeNull();
        var grant = seeded.Grant.ShouldNotBeNull();
        grant.Type.ShouldBe(LeaveLedgerEntryType.ExtraGrant);
        grant.Amount.ShouldBe(conversion.ResultDays);
        grant.LeaveTypeId.ShouldBe(AnnualTypeId);
        conversion.LedgerEntryId.ShouldBe(grant.Id);
        conversion.UsedByDate.ShouldNotBeNull();
    }

    [Fact]
    public void PayConversion_HasNoLedgerRow()
    {
        var seeded = Build(DataSeeder.DemoOvertimeConversionMode.Pay);

        seeded.Conversion.ShouldNotBeNull().Kind.ShouldBe(OvertimeConversionKind.Pay);
        seeded.Grant.ShouldBeNull();
    }

    [Fact]
    public void NoConversion_LeavesTheRequestUnconverted()
    {
        var seeded = Build(DataSeeder.DemoOvertimeConversionMode.None);

        seeded.Conversion.ShouldBeNull();
        seeded.Grant.ShouldBeNull();
        seeded.Request.ConversionId.ShouldBeNull();
    }
}

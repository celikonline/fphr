using Sense.Modules.Analytics.Application.Kpis;
using Shouldly;
using Xunit;

namespace Sense.Modules.Analytics.Tests;

/// <summary>Regresyon: overtime-cost year/month eksik veya month=13 iken handler DateOnly hatasıyla 500 veriyordu.</summary>
public sealed class KpiPeriodValidatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(1999, 6)]
    [InlineData(2101, 6)]
    public void OvertimeCost_OutOfRange_IsInvalid(int year, int month) =>
        new GetOvertimeCostKpiValidator().Validate(new GetOvertimeCostKpiQuery(year, month)).IsValid.ShouldBeFalse();

    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 12)]
    [InlineData(2000, 6)]
    [InlineData(2100, 6)]
    public void OvertimeCost_InRange_IsValid(int year, int month) =>
        new GetOvertimeCostKpiValidator().Validate(new GetOvertimeCostKpiQuery(year, month)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(null, true)]
    [InlineData(2026, true)]
    [InlineData(0, false)]
    [InlineData(99999, false)]
    public void LeaveUsage_YearIsOptionalButBounded(int? year, bool valid) =>
        new GetLeaveUsageKpiValidator().Validate(new GetLeaveUsageKpiQuery(year)).IsValid.ShouldBe(valid);
}

using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Kart senseik-014 §3/§4: yeni tenant gece çalışması varsayılanı ve m.63 denkleştirme ayarı doğrulaması.</summary>
public sealed class ShiftSettingsEqualizationTests
{
    [Fact]
    public void CreateDefault_EnablesNightWorkWithLegalWindow()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        settings.NightWorkEnabled.ShouldBeTrue();
        settings.NightWorkStart.ShouldBe(new TimeOnly(20, 0));
        settings.NightWorkEnd.ShouldBe(new TimeOnly(6, 0));
        settings.NightWorkMaxMinutes.ShouldBe(450);
    }

    [Fact]
    public void CreateDefault_EqualizationDisabledWithTwoMonthDefault()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        settings.EqualizationEnabled.ShouldBeFalse();
        settings.EqualizationPeriodMonths.ShouldBe(2);
        settings.HasWrittenEqualizationAgreement.ShouldBeFalse();
    }

    [Fact]
    public void UpdateEqualization_TwoMonthsWithoutWrittenAgreement_Succeeds()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.UpdateEqualization(enabled: true, periodMonths: 2, anchorMonth: 1, hasWrittenAgreement: false);

        result.IsSuccess.ShouldBeTrue();
        settings.EqualizationEnabled.ShouldBeTrue();
        settings.EqualizationPeriodMonths.ShouldBe(2);
    }

    [Fact]
    public void UpdateEqualization_FourMonthsWithoutWrittenAgreement_Fails()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.UpdateEqualization(enabled: true, periodMonths: 4, anchorMonth: 1, hasWrittenAgreement: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.EqualizationWrittenAgreementRequired);
    }

    [Fact]
    public void UpdateEqualization_FourMonthsWithWrittenAgreement_Succeeds()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.UpdateEqualization(enabled: true, periodMonths: 4, anchorMonth: 1, hasWrittenAgreement: true);

        result.IsSuccess.ShouldBeTrue();
        settings.EqualizationPeriodMonths.ShouldBe(4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void UpdateEqualization_InvalidPeriodMonths_Fails(int invalidMonths)
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.UpdateEqualization(enabled: true, periodMonths: invalidMonths, anchorMonth: 1, hasWrittenAgreement: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.EqualizationPeriodMonthsInvalid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void UpdateEqualization_InvalidAnchorMonth_Fails(int invalidAnchor)
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.UpdateEqualization(enabled: true, periodMonths: 2, anchorMonth: invalidAnchor, hasWrittenAgreement: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.EqualizationAnchorMonthInvalid);
    }

    [Fact]
    public void UpdateEqualization_Disabled_SkipsAllValidation()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.UpdateEqualization(enabled: false, periodMonths: 99, anchorMonth: 0, hasWrittenAgreement: false);

        result.IsSuccess.ShouldBeTrue();
        settings.EqualizationEnabled.ShouldBeFalse();
    }
}

using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class ShiftSettingsFeaturesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IShiftSettingsRepository _settings = Substitute.For<IShiftSettingsRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();

    public ShiftSettingsFeaturesTests() => _tenant.TenantId.Returns(_tenantId);

    [Fact]
    public async Task GetSettings_NoRowYet_SeedsDefaultAndSaves()
    {
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns((ShiftSettings?)null);
        var handler = new GetShiftSettingsHandler(_settings, _tenant, _unitOfWork);

        var result = await handler.Handle(new GetShiftSettingsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MaxDailyMinutes.ShouldBe(ShiftSettings.DefaultMaxDailyMinutes);
        _settings.Received(1).Add(Arg.Any<ShiftSettings>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSettings_ExistingRow_DoesNotReseed()
    {
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        var handler = new GetShiftSettingsHandler(_settings, _tenant, _unitOfWork);

        await handler.Handle(new GetShiftSettingsQuery(), TestContext.Current.CancellationToken);

        _settings.DidNotReceive().Add(Arg.Any<ShiftSettings>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateSettings_ValidValues_Applies()
    {
        var existing = ShiftSettings.CreateDefault(_tenantId);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new UpdateShiftSettingsHandler(_settings, _tenant, _unitOfWork);

        var result = await handler.Handle(
            new UpdateShiftSettingsCommand(360, 2, 900, false, null, null, null, 30, true, false, 26, 3), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.MaxDailyMinutes.ShouldBe(360);
    }

    [Fact]
    public async Task UpdateSettings_InvalidValues_Fails()
    {
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        var handler = new UpdateShiftSettingsHandler(_settings, _tenant, _unitOfWork);

        var result = await handler.Handle(
            new UpdateShiftSettingsCommand(0, 2, 900, false, null, null, null, 30, true, false, 26, 3), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftSettingsInvalid);
    }
}

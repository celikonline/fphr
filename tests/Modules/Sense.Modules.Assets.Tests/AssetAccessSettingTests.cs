using System.Reflection;
using NSubstitute;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>
/// The tenant access setting "employees may see their own assets" (Home.ShowAssets) must be enforced on the server for
/// the self-service requests, not only hidden in the UI. Pure unit tests (no database).
/// </summary>
public sealed class AssetAccessSettingTests
{
    public static TheoryData<Type> SelfServiceRequests => new()
    {
        typeof(GetMyAssetsQuery),
        typeof(GetMyAssetHistoryQuery),
        typeof(AcknowledgeAssetCommand),
    };

    [Theory]
    [MemberData(nameof(SelfServiceRequests))]
    public void SelfServiceRequests_AreGatedByHomeShowAssets_AndKeepTheirPermission(Type request)
    {
        request.GetCustomAttributes<RequiresAccessSettingAttribute>().Select(a => a.Key).ShouldBe([AccessSettingKeys.HomeShowAssets]);
        request.GetCustomAttributes<RequiresPermissionAttribute>().ShouldHaveSingleItem().Permission.ShouldBe("assets.my.assets");
    }

    [Fact]
    public void HrFacingRequests_AreNotGatedByTheEmployeeSetting()
    {
        typeof(GetEmployeeAssetsQuery).GetCustomAttributes<RequiresAccessSettingAttribute>().ShouldBeEmpty();
        typeof(GetEmployeeAssetHistoryQuery).GetCustomAttributes<RequiresAccessSettingAttribute>().ShouldBeEmpty();
        typeof(ReturnAssetCommand).GetCustomAttributes<RequiresAccessSettingAttribute>().ShouldBeEmpty();
    }

    private static IAccessSettingsService Settings(bool enabled)
    {
        var settings = Substitute.For<IAccessSettingsService>();
        settings.IsEnabledForCurrentUserAsync(AccessSettingKeys.HomeShowAssets, Arg.Any<CancellationToken>()).Returns(enabled);
        return settings;
    }

    [Fact]
    public async Task GetMyAssets_WhenSettingOff_IsFeatureDisabled_AndTheHandlerIsNotReached()
    {
        var behaviour = new AccessSettingsAuthorizationBehaviour<GetMyAssetsQuery, Result<IReadOnlyList<AssetDto>>>(Settings(false));
        var reached = false;

        var result = await behaviour.Handle(new GetMyAssetsQuery(), () =>
        {
            reached = true;
            return Task.FromResult(Result.Success<IReadOnlyList<AssetDto>>([]));
        }, CancellationToken.None);

        reached.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.FeatureDisabled);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Args!["accessSetting"].ShouldBe(AccessSettingKeys.HomeShowAssets);
    }

    [Fact]
    public async Task GetMyAssetHistory_WhenSettingOff_IsFeatureDisabled()
    {
        var behaviour = new AccessSettingsAuthorizationBehaviour<GetMyAssetHistoryQuery, Result<IReadOnlyList<EmployeeAssetHistoryDto>>>(Settings(false));

        var result = await behaviour.Handle(
            new GetMyAssetHistoryQuery(), () => Task.FromResult(Result.Success<IReadOnlyList<EmployeeAssetHistoryDto>>([])), CancellationToken.None);

        result.Error.Code.ShouldBe(ErrorCodes.FeatureDisabled);
    }

    [Fact]
    public async Task AcknowledgeAsset_WhenSettingOff_IsFeatureDisabled_AndNothingIsAcknowledged()
    {
        var behaviour = new AccessSettingsAuthorizationBehaviour<AcknowledgeAssetCommand, Result>(Settings(false));
        var reached = false;

        var result = await behaviour.Handle(new AcknowledgeAssetCommand(Guid.NewGuid()), () =>
        {
            reached = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        reached.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.FeatureDisabled);
    }

    [Fact]
    public async Task GetMyAssets_WhenSettingOn_ReachesTheHandler()
    {
        var behaviour = new AccessSettingsAuthorizationBehaviour<GetMyAssetsQuery, Result<IReadOnlyList<AssetDto>>>(Settings(true));
        var reached = false;

        var result = await behaviour.Handle(new GetMyAssetsQuery(), () =>
        {
            reached = true;
            return Task.FromResult(Result.Success<IReadOnlyList<AssetDto>>([]));
        }, CancellationToken.None);

        reached.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
    }
}

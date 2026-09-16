using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>G-3-05 (gün açılışı TALIMAT'ı): davet kabulünde <c>privacyNoticeVersion</c>, ürün genelinde tek geçerli
/// <see cref="IdentityDefaults.KnownPrivacyNoticeVersion"/>'dan farklıysa reddedilir - kullanıcı eski bir sayfa görüyordur.</summary>
public sealed class AcceptInvitationValidatorTests
{
    private static AcceptInvitationValidator Validator() => new(Options.Create(new IdentityOptions()));

    [Fact]
    public void Validate_OutdatedPrivacyNoticeVersion_FailsWithOutdatedCode()
    {
        var command = new AcceptInvitationCommand("token", "P@ssword1234", PrivacyNoticeAccepted: true, PrivacyNoticeVersion: "2025-01", DeviceInfo: null, IpAddress: null);

        var result = Validator().Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == IdentityErrors.InvitationPrivacyNoticeOutdated);
    }

    [Fact]
    public void Validate_CurrentPrivacyNoticeVersion_Passes()
    {
        var command = new AcceptInvitationCommand("token", "P@ssword1234", PrivacyNoticeAccepted: true, PrivacyNoticeVersion: IdentityDefaults.KnownPrivacyNoticeVersion, DeviceInfo: null, IpAddress: null);

        var result = Validator().Validate(command);

        result.Errors.ShouldNotContain(e => e.ErrorMessage == IdentityErrors.InvitationPrivacyNoticeOutdated);
    }
}

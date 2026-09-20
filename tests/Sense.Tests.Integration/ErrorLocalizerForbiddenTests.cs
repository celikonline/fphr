using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Localization;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>Regresyon: izin adı taşımayan Forbidden hataları "'{permission}' izni gerekli" yer tutucusunu çözmeden gösteriyordu.</summary>
public sealed class ErrorLocalizerForbiddenTests
{
    private static ErrorLocalizer Localizer()
    {
        var resources = Substitute.For<IStringLocalizer<SharedResource>>();
        resources[Arg.Any<string>()].Returns(ci =>
        {
            var key = (string)ci[0];
            var value = key switch
            {
                "auth.forbidden" => "Bu işlem için '{permission}' izni gerekli.",
                "auth.forbidden_generic" => "Bu işlem için yetkiniz yok.",
                _ => key,
            };
            return new LocalizedString(key, value, resourceNotFound: value == key);
        });
        return new ErrorLocalizer(resources);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Forbidden_WithoutPermissionArg_UsesGenericMessage_NoPlaceholder()
    {
        var message = Localizer().Message(Error.Forbidden(ErrorCodes.Forbidden));

        message.ShouldBe("Bu işlem için yetkiniz yok.");
        message.ShouldNotContain("{");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Forbidden_WithPermissionArg_StillNamesThePermission()
    {
        var message = Localizer().Message(Error.Forbidden(ErrorCodes.Forbidden, ("permission", "identity.roles.manage")));

        message.ShouldBe("Bu işlem için 'identity.roles.manage' izni gerekli.");
    }
}

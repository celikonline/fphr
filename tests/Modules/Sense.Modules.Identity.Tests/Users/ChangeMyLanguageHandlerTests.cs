using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>FG-001: <c>PUT identity/me/language</c> — yalnız yapılandırılmış kültürlerin iki harfli dilleri kabul edilir.</summary>
public sealed class ChangeMyLanguageHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly User _user = User.Create(Guid.NewGuid(), "dil@example.com", "Dil", "Deneme", "tr").Value;

    public ChangeMyLanguageHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_user.Id);
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
    }

    private ChangeMyLanguageHandler Handler(params string[] supportedCultures) =>
        new(_currentUser, _users, Options.Create(supportedCultures.Length == 0
            ? new LocalizationOptions()
            : new LocalizationOptions { SupportedCultures = supportedCultures.ToList() }));

    [Theory]
    [InlineData("en", "en")]
    [InlineData(" EN ", "en")]
    [InlineData("tr", "tr")]
    public async Task Handle_SupportedLanguage_ChangesPreferredLanguage(string input, string expected)
    {
        var result = await Handler().Handle(new ChangeMyLanguageCommand(input), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.PreferredLanguage.ShouldBe(expected);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    [InlineData("en-US")]
    [InlineData("")]
    public async Task Handle_UnsupportedLanguage_FailsWithValidation_AndKeepsLanguage(string input)
    {
        var result = await Handler().Handle(new ChangeMyLanguageCommand(input), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.UserLanguageUnsupported);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _user.PreferredLanguage.ShouldBe("tr");
    }

    /// <summary>P36 hazırlığı: yapılandırmaya ar-SA eklenince "ar" kod değişikliği olmadan kabul edilir.</summary>
    [Fact]
    public async Task Handle_CultureAddedToConfiguration_IsAcceptedWithoutCodeChange()
    {
        var result = await Handler(Cultures.Turkish, Cultures.English, "ar-SA").Handle(new ChangeMyLanguageCommand("ar"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.PreferredLanguage.ShouldBe("ar");
    }

    [Fact]
    public async Task Handle_Unauthenticated_FailsWithUnauthorized()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await Handler().Handle(new ChangeMyLanguageCommand("en"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        _user.PreferredLanguage.ShouldBe("tr");
    }

    [Fact]
    public void ChangePreferredLanguage_NormalizesToLowerCase()
    {
        var user = User.Create(Guid.NewGuid(), "dil2@example.com", "Dil", "Deneme", "tr").Value;

        user.ChangePreferredLanguage(" EN ");

        user.PreferredLanguage.ShouldBe("en");
    }
}

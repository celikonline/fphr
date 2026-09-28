using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// FG-001: <c>PUT identity/me/language</c> uçtan uca (Dispatcher + pipeline + Postgres). Kaydedilen tercih <c>GET identity/me</c>
/// yanıtındaki <c>preferredLanguage</c> ile geri döner; desteklenmeyen dil 400 (Validation) ile reddedilir ve tercih değişmez.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MeLanguageTests : IdentityStackTestBase
{
    [Fact]
    public async Task ChangeMyLanguage_Supported_IsReturnedByGetMe()
    {
        var tenantId = await SeedTenantAsync("me-language");
        var userId = await SeedUserAsync(tenantId, "dil@acme.test", SystemRoleCodes.Employee);
        ActAs(tenantId, userId);
        (await ExecuteQueryAsync(new GetMeQuery())).Value.PreferredLanguage.ShouldBe("tr");

        (await ExecuteCommandAsync(new ChangeMyLanguageCommand("en"))).IsSuccess.ShouldBeTrue();

        (await ExecuteQueryAsync(new GetMeQuery())).Value.PreferredLanguage.ShouldBe("en");
    }

    [Fact]
    public async Task ChangeMyLanguage_Unsupported_ReturnsValidation_AndKeepsLanguage()
    {
        var tenantId = await SeedTenantAsync("me-language-unsupported");
        var userId = await SeedUserAsync(tenantId, "dil2@acme.test", SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        ShouldFailWith(await ExecuteCommandAsync(new ChangeMyLanguageCommand("xx")), IdentityErrors.UserLanguageUnsupported, ErrorType.Validation);

        (await ExecuteQueryAsync(new GetMeQuery())).Value.PreferredLanguage.ShouldBe("tr");
    }
}

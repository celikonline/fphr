using Microsoft.AspNetCore.RateLimiting;
using Sense.Modules.Identity.Api.Controllers;
using Sense.Modules.Workflow.Api.Controllers;
using Sense.Shared.Contracts.Configuration;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// Regresyon: kimlik doğrulamasız uçlar (giriş, e-posta onay linki ile karar, parola sıfırlama, MFA tamamlama)
/// parola/token tahminine karşı IP bazlı hız sınırlama politikasıyla işaretli olmalı.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AnonymousEndpointRateLimitTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(typeof(AuthController), nameof(AuthController.Login), RateLimitPolicyNames.AuthAttempt)]
    [InlineData(typeof(AuthController), nameof(AuthController.ForgotPassword), RateLimitPolicyNames.PublicAnonymousWrite)]
    [InlineData(typeof(AuthController), nameof(AuthController.ResetPassword), RateLimitPolicyNames.PublicAnonymousWrite)]
    [InlineData(typeof(AuthController), nameof(AuthController.CompleteMfa), RateLimitPolicyNames.PublicAnonymousWrite)]
    [InlineData(typeof(DecisionsController), nameof(DecisionsController.DecideByToken), RateLimitPolicyNames.AuthAttempt)]
    public void AnonymousEndpoint_HasRateLimitPolicy(Type controller, string action, string expectedPolicy)
    {
        var method = controller.GetMethod(action);
        method.ShouldNotBeNull();

        var attribute = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>().SingleOrDefault();

        attribute.ShouldNotBeNull($"{controller.Name}.{action} must declare [EnableRateLimiting]");
        attribute.PolicyName.ShouldBe(expectedPolicy);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void AuthAttemptPolicy_DefaultsToTenPerWindow()
    {
        new RateLimitingOptions().AuthAttempt.PermitLimit.ShouldBe(10);
    }
}

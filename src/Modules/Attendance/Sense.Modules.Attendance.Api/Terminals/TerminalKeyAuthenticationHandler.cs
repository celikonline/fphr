using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;

namespace Sense.Modules.Attendance.Api.Terminals;

/// <summary>
/// <c>X-Terminal-Key</c> şeması (Z5.1). Başlık yoksa sonuç yok (başka şemaya karışmaz); aynı istekte <c>Authorization</c> başlığı da varsa
/// ya da başlık birden çok değer taşıyorsa reddeder (kullanıcı ve terminal kimliği karışmasın). Doğrulama <see cref="ITerminalKeyAuthenticator"/>'da
/// (sabit süreli karşılaştırma). Başarısızlıkta 401 yalnız şema adını döner: gövde yok, kiracı/terminal bilgisi yok.
/// </summary>
public sealed class TerminalKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ITerminalKeyAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TerminalKeyDefaults.HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        if (values.Count != 1 || Request.Headers.ContainsKey(HeaderNames.Authorization))
        {
            return AuthenticateResult.Fail(TerminalKeyDefaults.InvalidKeyMessage);
        }

        var terminal = await authenticator.AuthenticateAsync(values[0], Context.RequestAborted).ConfigureAwait(false);
        return terminal is null
            ? AuthenticateResult.Fail(TerminalKeyDefaults.InvalidKeyMessage)
            : AuthenticateResult.Success(new AuthenticationTicket(TerminalPrincipal.Create(terminal, Scheme.Name), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = TerminalKeyDefaults.AuthenticationScheme;
        return Task.CompletedTask;
    }
}

/// <summary>Terminal kimliği ↔ <see cref="ClaimsPrincipal"/>.</summary>
public static class TerminalPrincipal
{
    public static ClaimsPrincipal Create(TerminalIdentity terminal, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(TerminalKeyDefaults.TerminalIdClaim, terminal.TerminalId.ToString()),
            new(TerminalKeyDefaults.TenantIdClaim, terminal.TenantId.ToString()),
            new(TerminalKeyDefaults.DirectionModeClaim, ((int)terminal.DirectionMode).ToString(CultureInfo.InvariantCulture)),
        };
        if (terminal.LocationId is { } location)
        {
            claims.Add(new Claim(TerminalKeyDefaults.LocationIdClaim, location.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    /// <summary>Yalnız bu şemayla doğrulanmış kimlikten okur; aksi halde null.</summary>
    public static TerminalIdentity? Read(ClaimsPrincipal principal)
    {
        var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType == TerminalKeyDefaults.AuthenticationScheme);
        if (identity is null
            || !Guid.TryParse(identity.FindFirst(TerminalKeyDefaults.TerminalIdClaim)?.Value, out var terminalId)
            || !Guid.TryParse(identity.FindFirst(TerminalKeyDefaults.TenantIdClaim)?.Value, out var tenantId)
            || !int.TryParse(identity.FindFirst(TerminalKeyDefaults.DirectionModeClaim)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode)
            || !Enum.IsDefined((TerminalDirectionMode)mode))
        {
            return null;
        }

        Guid? locationId = Guid.TryParse(identity.FindFirst(TerminalKeyDefaults.LocationIdClaim)?.Value, out var location) ? location : null;
        return new TerminalIdentity(terminalId, tenantId, (TerminalDirectionMode)mode, locationId);
    }
}

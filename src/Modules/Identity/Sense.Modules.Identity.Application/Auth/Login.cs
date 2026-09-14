using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tokens;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Auth;

/// <summary>
/// Giriş: e-posta + parola (+ isteğe bağlı kiracı slug'ı). Aynı e-posta birden fazla kiracıda olabilir;
/// slug verilmemişse ve birden fazla eşleşme varsa kiracı seçimi istenir.
/// </summary>
public sealed record LoginCommand(string Email, string Password, string? TenantSlug, string? DeviceInfo, string? IpAddress) : ICommand<LoginResultDto>;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IRoleRepository roles,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    ISecretGenerator secrets,
    ITokenService tokens,
    ITenantContextSetter tenantSetter,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ICommandHandler<LoginCommand, LoginResultDto>
{
    public async Task<Result<LoginResultDto>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var matches = await users.FindAcrossTenantsByEmailAsync(command.Email, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(command.TenantSlug))
        {
            var tenant = await tenants.GetBySlugAsync(command.TenantSlug.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
            matches = tenant is null ? [] : matches.Where(m => m.TenantId == tenant.Id).ToList();
        }

        if (matches.Count == 0)
        {
            return Error.Unauthorized(IdentityErrors.InvalidCredentials);
        }

        if (matches.Count > 1)
        {
            var choices = new List<TenantChoiceDto>();
            foreach (var (_, tenantId) in matches)
            {
                var t = await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
                if (t is not null && t.IsWritable)
                {
                    choices.Add(new TenantChoiceDto(t.Id, t.Slug, t.Name));
                }
            }

            // Parola doğrulanmadan kiracı listesi sızdırılmaz: ilk eşleşmede parola kontrolü yapılır.
            if (!hasher.Verify(matches[0].User.PasswordHash ?? string.Empty, command.Password))
            {
                return Error.Unauthorized(IdentityErrors.InvalidCredentials);
            }

            return LoginResultDto.SelectTenant(choices);
        }

        var (user, tid) = matches[0];
        var tenantEntity = await tenants.GetByIdAsync(tid, cancellationToken).ConfigureAwait(false);
        if (tenantEntity is null || !tenantEntity.IsWritable)
        {
            return Error.Unauthorized(IdentityErrors.TenantInactive);
        }

        using var scope = tenantSetter.BeginScope(tenantEntity.Id, tenantEntity.Slug);

        var canSignIn = user.CanSignIn(now);
        if (canSignIn.IsFailure)
        {
            return canSignIn.Error;
        }

        if (!hasher.Verify(user.PasswordHash!, command.Password))
        {
            user.RecordFailedAccess(now, new LockoutPolicy(options.Value.MaxFailedAccessAttempts, TimeSpan.FromMinutes(options.Value.LockoutMinutes)));
            return Error.Unauthorized(IdentityErrors.InvalidCredentials);
        }

        if (user.MfaEnabled)
        {
            // MFA akışı Faz 0 sonraki adımda tamamlanır; şimdilik durum bildirilir.
            return LoginResultDto.MfaRequired(secrets.NewToken());
        }

        user.RecordSuccessfulLogin(now);

        var roleCodes = (await roles.GetByIdsAsync(user.Roles.Select(r => r.RoleId), cancellationToken).ConfigureAwait(false))
            .Select(r => r.Code).ToList();

        var access = tokens.IssueAccessToken(user, tenantEntity.Id, tenantEntity.Slug, roleCodes, permissionVersion: 0);
        var rawRefresh = secrets.NewToken();
        var refresh = RefreshToken.Issue(tenantEntity.Id, user.Id, null, secrets.Hash(rawRefresh), now, tokens.RefreshTokenLifetime, command.DeviceInfo, command.IpAddress);
        refreshTokens.Add(refresh);

        return LoginResultDto.Success(new AuthTokensDto(access.Token, access.ExpiresAtUtc, rawRefresh, refresh.ExpiresAt));
    }
}

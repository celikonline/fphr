using FluentValidation;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tokens;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Auth;

/// <summary>Refresh token rotasyonu: eski token iptal, yenisi verilir; iptal edilmiş token yeniden kullanılırsa tüm aile kapatılır.</summary>
public sealed record RefreshTokenCommand(string RefreshToken, string? DeviceInfo, string? IpAddress) : ICommand<AuthTokensDto>;

public sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshTokenHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    ITenantRepository tenants,
    IRoleRepository roles,
    ISecretGenerator secrets,
    ITokenService tokens,
    ITenantContextSetter tenantSetter,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RefreshTokenCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await refreshTokens.GetByHashAsync(secrets.Hash(command.RefreshToken), cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Error.Unauthorized(IdentityErrors.RefreshTokenInvalid);
        }

        using var scope = tenantSetter.BeginScope(existing.TenantId);

        if (!existing.IsActive(now))
        {
            // Reuse detection: iptal edilmiş token tekrar geldi → aynı aileyi tamamen kapat.
            foreach (var t in await refreshTokens.GetFamilyAsync(existing.FamilyId, cancellationToken).ConfigureAwait(false))
            {
                t.Revoke(now);
            }

            // Handler hata döndüğü için UnitOfWorkBehaviour kaydetmez: aile iptali açıkça kalıcılaştırılır.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Error.Unauthorized(IdentityErrors.RefreshTokenReused);
        }

        var user = await users.GetByIdAsync(existing.UserId, cancellationToken).ConfigureAwait(false);
        var tenant = await tenants.GetByIdAsync(existing.TenantId, cancellationToken).ConfigureAwait(false);
        if (user is null || tenant is null || !Sense.Modules.Identity.Application.Tenants.CommercialLicenseRules.CanAccess(tenant, now))
        {
            return Error.Unauthorized(IdentityErrors.RefreshTokenInvalid);
        }

        var canSignIn = user.CanSignIn(now);
        if (canSignIn.IsFailure)
        {
            return canSignIn.Error;
        }

        var rawNew = secrets.NewToken();
        var newHash = secrets.Hash(rawNew);
        existing.Rotate(newHash, now);
        var replacement = RefreshToken.Issue(existing.TenantId, user.Id, existing.FamilyId, newHash, now, tokens.RefreshTokenLifetime, command.DeviceInfo, command.IpAddress);
        refreshTokens.Add(replacement);

        var roleCodes = (await roles.GetByIdsAsync(user.Roles.Select(r => r.RoleId), cancellationToken).ConfigureAwait(false)).Select(r => r.Code).ToList();
        var access = tokens.IssueAccessToken(user, tenant.Id, tenant.Slug, roleCodes, permissionVersion: 0);

        return new AuthTokensDto(access.Token, access.ExpiresAtUtc, rawNew, replacement.ExpiresAt);
    }
}

/// <summary>Çıkış: verilen refresh token (veya kullanıcının tüm oturumları) iptal edilir.</summary>
public sealed record LogoutCommand(string? RefreshToken, bool AllSessions) : ICommand;

public sealed class LogoutHandler(IRefreshTokenRepository refreshTokens, ISecretGenerator secrets, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (command.AllSessions && user.UserId is { } uid)
        {
            foreach (var t in await refreshTokens.GetActiveByUserAsync(uid, now, cancellationToken).ConfigureAwait(false))
            {
                t.Revoke(now);
            }

            return Result.Success();
        }

        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            var existing = await refreshTokens.GetByHashAsync(secrets.Hash(command.RefreshToken), cancellationToken).ConfigureAwait(false);
            existing?.Revoke(now);
        }

        return Result.Success();
    }
}

using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tokens;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
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
    IMfaChallengeTokenProtector mfaChallenge,
    IOptions<IdentityOptions> options,
    IIdentityUnitOfWork unitOfWork,
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
            // Kullanıcı yok: zamanlama farkı hesabın varlığını sızdırmasın diye sahte hash işlemi yapılır.
            _ = hasher.Hash(command.Password);
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

        using var scope = tenantSetter.BeginScope(tid, tenantEntity?.Slug);

        // Kullanıcı numaralandırmayı önlemek için durum hataları (davetli/kilitli/pasif/kiracı pasif) yalnızca parola
        // doğruysa döner; parola yanlışsa (veya hesabın parolası yoksa) her zaman aynı genel hata verilir.
        var passwordOk = user.PasswordHash is not null && hasher.Verify(user.PasswordHash, command.Password);
        if (user.PasswordHash is null)
        {
            _ = hasher.Hash(command.Password);
        }

        if (!passwordOk)
        {
            // Sayaç yalnız giriş yapabilecek (aktif, kilitli olmayan) hesaplar için artar. Handler başarısız (Error) döndüğü
            // için UnitOfWorkBehaviour SaveChanges yapmaz; sayaç/kilit açıkça kalıcılaştırılır.
            if (tenantEntity is { IsWritable: true } && user.CanSignIn(now).IsSuccess)
            {
                user.RecordFailedAccess(now, new LockoutPolicy(options.Value.MaxFailedAccessAttempts, TimeSpan.FromMinutes(options.Value.LockoutMinutes)));
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return Error.Unauthorized(IdentityErrors.InvalidCredentials);
        }

        if (tenantEntity is null || !Sense.Modules.Identity.Application.Tenants.CommercialLicenseRules.CanAccess(tenantEntity, now))
        {
            return Error.Unauthorized(IdentityErrors.TenantInactive);
        }

        var canSignIn = user.CanSignIn(now);
        if (canSignIn.IsFailure)
        {
            return canSignIn.Error;
        }

        if (user.MfaEnabled)
        {
            // Token verilmeden durur: mfaToken durumsuz (DataProtection, imzalı, 5 dk) — tamamlama CompleteMfaLoginHandler'da.
            return LoginResultDto.MfaRequired(mfaChallenge.Protect(user.Id, tenantEntity.Id));
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

/// <summary>Login'in `mfaRequired` (Status=<see cref="LoginStatuses.MfaRequired"/>) yanıtından sonraki adım: `mfaToken` + TOTP
/// kodu ile oturum tamamlanır. Hatalı kod, normal parola akışıyla aynı `RecordFailedAccess`/lockout sayacına dahildir.</summary>
public sealed record CompleteMfaLoginCommand(string MfaToken, string Code, string? DeviceInfo, string? IpAddress) : ICommand<LoginResultDto>;

public sealed class CompleteMfaLoginValidator : AbstractValidator<CompleteMfaLoginCommand>
{
    public CompleteMfaLoginValidator()
    {
        RuleFor(x => x.MfaToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
    }
}

public sealed class CompleteMfaLoginHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IRoleRepository roles,
    IRefreshTokenRepository refreshTokens,
    ISecretGenerator secrets,
    ITokenService tokens,
    ITenantContextSetter tenantSetter,
    IMfaChallengeTokenProtector mfaChallenge,
    ITotpService totp,
    IEncryptionProvider encryption,
    IOptions<IdentityOptions> options,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CompleteMfaLoginCommand, LoginResultDto>
{
    // Login.cs'in başarılı-giriş kuyruğuyla (token üretimi) kasıtlı olarak neredeyse aynı: LoginHandler'ı bu akışa
    // uyacak şekilde parçalamak (ör. ortak bir "IssueSession" yardımcı metodu) bu turun kapsamı dışında bir refactor —
    // küçük tekrar, mevcut davranışı bozma riskini almaktan daha ucuz.
    public async Task<Result<LoginResultDto>> Handle(CompleteMfaLoginCommand command, CancellationToken cancellationToken)
    {
        if (!mfaChallenge.TryUnprotect(command.MfaToken, out var userId, out var tenantId))
        {
            return Error.Unauthorized(IdentityErrors.MfaTokenExpired);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var tenantEntity = await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenantEntity is null || !Sense.Modules.Identity.Application.Tenants.CommercialLicenseRules.CanAccess(tenantEntity, now))
        {
            return Error.Unauthorized(IdentityErrors.TenantInactive);
        }

        using var scope = tenantSetter.BeginScope(tenantEntity.Id, tenantEntity.Slug);

        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.MfaEnabled || user.TotpSecretEncrypted is null)
        {
            return Error.Unauthorized(IdentityErrors.MfaTokenExpired);
        }

        var canSignIn = user.CanSignIn(now);
        if (canSignIn.IsFailure)
        {
            return canSignIn.Error;
        }

        if (!totp.ValidateCode(encryption.Decrypt(user.TotpSecretEncrypted), command.Code))
        {
            user.RecordFailedAccess(now, new LockoutPolicy(options.Value.MaxFailedAccessAttempts, TimeSpan.FromMinutes(options.Value.LockoutMinutes)));
            // Handler hata döndüğü için UnitOfWorkBehaviour kaydetmez: sayaç/kilit açıkça kalıcılaştırılır.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Error.Unauthorized(IdentityErrors.MfaInvalidCode);
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

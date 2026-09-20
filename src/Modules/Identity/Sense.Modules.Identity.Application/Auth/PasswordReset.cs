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
/// Şifremi unuttum: e-posta girilir, hesap(lar) varsa sıfırlama bağlantısı gönderilir. Kullanıcı numaralandırmasına
/// izin vermemek için var olmayan/pasif hesaplarda da aynı (boş) başarı yanıtı döner — davet akışındaki
/// SendInvitationCommand ile aynı token kalıbı (hash + expiry + tek kullanım).
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : ICommand;

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public sealed class ForgotPasswordHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordResetTokenRepository resetTokens,
    ISecretGenerator secrets,
    IIdentityMailer mailer,
    IFrontendLinks links,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ICommandHandler<ForgotPasswordCommand>
{
    public async Task<Result> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var validity = TimeSpan.FromMinutes(options.Value.PasswordResetValidityMinutes);
        var matches = await users.FindAcrossTenantsByEmailAsync(command.Email, cancellationToken).ConfigureAwait(false);

        foreach (var (user, tenantId) in matches)
        {
            // Pasif hesap veya henüz parola belirlenmemiş (davet bekleyen) kullanıcı sessizce atlanır.
            if (user.Status != UserStatus.Active || user.PasswordHash is null)
            {
                continue;
            }

            var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (tenant is null || !tenant.IsWritable)
            {
                continue;
            }

            var rawToken = secrets.NewToken();
            var token = PasswordResetToken.Issue(tenantId, user.Id, secrets.Hash(rawToken), now, validity);
            resetTokens.Add(token);

            await mailer.SendPasswordResetAsync(user.Email, user.FullName, links.PasswordReset(rawToken), token.ExpiresAt, cancellationToken)
                .ConfigureAwait(false);
        }

        // Eşleşme olmasa da (bilinmeyen e-posta) aynı başarı yanıtı döner.
        return Result.Success();
    }
}

/// <summary>Anonim: sıfırlama bağlantısındaki token ile yeni parola belirleme. Başarılı sıfırlama tüm oturumları kapatır.</summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand;

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator(IOptions<IdentityOptions> options)
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(options.Value.MinPasswordLength).WithMessage(IdentityErrors.PasswordTooShort)
            .Must(PasswordPolicy.IsStrongEnough).WithMessage(IdentityErrors.PasswordTooWeak);
    }
}

public sealed class ResetPasswordHandler(
    IPasswordResetTokenRepository resetTokens,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    ISecretGenerator secrets,
    ITenantContextSetter tenantSetter,
    IPermissionCacheInvalidator cache,
    TimeProvider clock) : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = await resetTokens.GetByTokenHashAsync(secrets.Hash(command.Token), cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return Error.NotFound(IdentityErrors.PasswordResetTokenNotFound);
        }

        if (!token.IsUsable(now))
        {
            return Error.Rule(IdentityErrors.PasswordResetTokenExpired);
        }

        using var scope = tenantSetter.BeginScope(token.TenantId);

        var user = await users.GetByIdAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        if (PasswordPolicy.ContainsEmailLocalPart(command.NewPassword, user.Email))
        {
            return Error.Validation(IdentityErrors.PasswordTooWeak);
        }

        user.SetPassword(hasher.Hash(command.NewPassword));
        token.MarkUsed(now);

        // Ele geçirilmiş bir hesabın açık kalan oturumları sıfırlamadan sonra kapatılır.
        foreach (var refresh in await refreshTokens.GetActiveByUserAsync(user.Id, now, cancellationToken).ConfigureAwait(false))
        {
            refresh.Revoke(now);
        }

        await cache.InvalidateUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

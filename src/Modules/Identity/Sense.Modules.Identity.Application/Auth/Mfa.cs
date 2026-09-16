using FluentValidation;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Auth;

/// <summary>
/// MFA (TOTP) self-servis: durum sorgusu → kayıt → doğrulama → (isteğe bağlı) kapatma. Login sırasındaki tamamlama
/// akışı için bkz. <see cref="CompleteMfaLoginCommand"/> (Login.cs). İki adımlı kayıt kalıbı: <see cref="EnrollMfaCommand"/>
/// secret'ı "bekleyen" olarak yazar (<see cref="User.MfaEnabled"/> hâlâ false), <see cref="VerifyMfaEnrollmentCommand"/>
/// kodu doğrulayınca etkinleştirir. Bu ara durumda ikinci bir enroll çağrısı önceki bekleyen secret'ın üstüne yazar
/// (kullanıcı QR'ı yeniden okutup baştan başlayabilir) — ayrı bir "pending" alanı/migration gerekmez.
/// </summary>
public sealed record GetMfaStatusQuery : IQuery<MfaStatusDto>;

/// <summary>Güvenlik ekranı açılışı (S-12): MFA açık mı, kayıt yarım mı kaldı (<see cref="MfaStatusDto.PendingEnrollment"/>).</summary>
public sealed class GetMfaStatusHandler(ICurrentUser currentUser, IUserRepository users) : IQueryHandler<GetMfaStatusQuery, MfaStatusDto>
{
    public async Task<Result<MfaStatusDto>> Handle(GetMfaStatusQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        return new MfaStatusDto(user.MfaEnabled, !user.MfaEnabled && user.TotpSecretEncrypted is not null);
    }
}

public sealed record EnrollMfaCommand : ICommand<MfaEnrollmentDto>;

public sealed class EnrollMfaHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    ITotpService totp,
    IEncryptionProvider encryption) : ICommandHandler<EnrollMfaCommand, MfaEnrollmentDto>
{
    public async Task<Result<MfaEnrollmentDto>> Handle(EnrollMfaCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        if (user.MfaEnabled)
        {
            return Error.Conflict(IdentityErrors.MfaAlreadyEnabled);
        }

        var secret = totp.GenerateSecret();
        user.BeginMfaEnrollment(encryption.Encrypt(secret));

        return new MfaEnrollmentDto(secret, totp.BuildOtpAuthUri(secret, user.Email, MfaDefaults.Issuer));
    }
}

public sealed record VerifyMfaEnrollmentCommand(string Code) : ICommand;

public sealed class VerifyMfaEnrollmentValidator : AbstractValidator<VerifyMfaEnrollmentCommand>
{
    public VerifyMfaEnrollmentValidator() => RuleFor(x => x.Code).NotEmpty();
}

public sealed class VerifyMfaEnrollmentHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    ITotpService totp,
    IEncryptionProvider encryption) : ICommandHandler<VerifyMfaEnrollmentCommand>
{
    public async Task<Result> Handle(VerifyMfaEnrollmentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        if (user.TotpSecretEncrypted is null || !totp.ValidateCode(encryption.Decrypt(user.TotpSecretEncrypted), command.Code))
        {
            return Error.Validation(IdentityErrors.MfaInvalidCode);
        }

        user.EnableMfa(user.TotpSecretEncrypted);
        return Result.Success();
    }
}

/// <summary>Self-servis MFA kapatma (kod ile). Yönetici tarafından sıfırlama için ayrı bir komut var: <c>ResetUserMfaCommand</c>
/// (UserCommands.cs, <c>identity.users.manage</c> izni, kendi hesabına 409).</summary>
public sealed record DisableMfaCommand(string Code) : ICommand;

public sealed class DisableMfaValidator : AbstractValidator<DisableMfaCommand>
{
    public DisableMfaValidator() => RuleFor(x => x.Code).NotEmpty();
}

public sealed class DisableMfaHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    ITotpService totp,
    IEncryptionProvider encryption) : ICommandHandler<DisableMfaCommand>
{
    public async Task<Result> Handle(DisableMfaCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.MfaEnabled || user.TotpSecretEncrypted is null
            || !totp.ValidateCode(encryption.Decrypt(user.TotpSecretEncrypted), command.Code))
        {
            return Error.Validation(IdentityErrors.MfaInvalidCode);
        }

        user.DisableMfa();
        return Result.Success();
    }
}

internal static class MfaDefaults
{
    /// <summary>otpauth:// URI'de authenticator uygulamasında gösterilen sağlayıcı adı. Türkçe karakter içermez (URI kodlama sorunlarından kaçınmak için).</summary>
    public const string Issuer = "SenseIK";
}

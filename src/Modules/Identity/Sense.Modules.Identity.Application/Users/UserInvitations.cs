using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Users;

/// <summary>Davet gönderme / yeniden gönderme: token üretilir, e-posta yollanır.</summary>
[RequiresPermission(IdentityPermissions.UsersInvite)]
public sealed record SendInvitationCommand(Guid UserId) : ICommand<Guid>;

public sealed class SendInvitationHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IInvitationRepository invitations,
    ISecretGenerator secrets,
    IIdentityMailer mailer,
    IFrontendLinks links,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ICommandHandler<SendInvitationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SendInvitationCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        if (user.Status != UserStatus.Invited)
        {
            return Error.Rule(IdentityErrors.InvitationUserActive);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var validity = TimeSpan.FromHours(options.Value.InvitationValidityHours);
        var rawToken = secrets.NewToken();
        var hash = secrets.Hash(rawToken);

        var invitation = await invitations.GetPendingByUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            invitation = Invitation.Create(tenant.TenantId, user.Id, user.Email, hash, now, validity, currentUser.UserId);
            invitations.Add(invitation);
        }
        else
        {
            var renewed = invitation.Renew(hash, now, validity);
            if (renewed.IsFailure)
            {
                return renewed.Error;
            }
        }

        var tenantEntity = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        await mailer.SendInvitationAsync(user.Email, user.FullName, tenantEntity?.Name ?? tenant.TenantSlug ?? string.Empty,
            links.InviteAccept(rawToken), invitation.ExpiresAt, cancellationToken).ConfigureAwait(false);

        return invitation.Id;
    }
}

[RequiresPermission(IdentityPermissions.UsersInvite)]
public sealed record RevokeInvitationCommand(Guid InvitationId) : ICommand;

public sealed class RevokeInvitationHandler(IInvitationRepository invitations) : ICommandHandler<RevokeInvitationCommand>
{
    public async Task<Result> Handle(RevokeInvitationCommand command, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByIdAsync(command.InvitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        invitation.Revoke();
        return Result.Success();
    }
}

/// <summary>Anonim: davet bağlantısındaki token ile davet önizlemesi.</summary>
public sealed record GetInvitationPreviewQuery(string Token) : IQuery<InvitationPreviewDto>;

public sealed class GetInvitationPreviewHandler(IInvitationRepository invitations, IUserRepository users, ITenantRepository tenants, ISecretGenerator secrets, ITenantContextSetter tenantSetter, TimeProvider clock)
    : IQueryHandler<GetInvitationPreviewQuery, InvitationPreviewDto>
{
    public async Task<Result<InvitationPreviewDto>> Handle(GetInvitationPreviewQuery query, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByTokenHashAsync(secrets.Hash(query.Token), cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        using var scope = tenantSetter.BeginScope(invitation.TenantId);
        var user = await users.GetByIdAsync(invitation.UserId, cancellationToken).ConfigureAwait(false);
        var tenant = await tenants.GetByIdAsync(invitation.TenantId, cancellationToken).ConfigureAwait(false);
        if (user is null || tenant is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        return new InvitationPreviewDto(user.Email, user.FirstName, user.LastName, tenant.Name, tenant.Slug, invitation.ExpiresAt,
            invitation.IsUsable(clock.GetUtcNow().UtcDateTime));
    }
}

/// <summary>
/// Anonim: daveti kabul et, parola belirle, KVKK onayı kaydet, hesabı etkinleştir.
/// <paramref name="PrivacyNoticeVersion"/>: kullanıcının onayladığı aydınlatma metni sürümü (ürün genelinde tek
/// sabit, bugün <c>"2026-09"</c>) — <see cref="PrivacyNoticeAccepted"/> true iken zorunlu, en fazla 20 karakter.
/// Faz 1'in <c>IConsentRecorder</c> sözleşmesi (S-3) veritabanı dilimiyle bağlanana kadar yalnız log +
/// <see cref="Sense.Modules.Identity.Domain.Invitations.InvitationAccepted"/> olay yüküne taşınır, kalıcı rıza
/// kaydına henüz yazılmaz (TALIMAT Faz 3 Tur 1 Backend #4).
/// </summary>
public sealed record AcceptInvitationCommand(string Token, string Password, bool PrivacyNoticeAccepted, string? PrivacyNoticeVersion, string? DeviceInfo, string? IpAddress)
    : ICommand<AuthTokensDto>;

public sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator(IOptions<IdentityOptions> options)
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(options.Value.MinPasswordLength).WithMessage(IdentityErrors.PasswordTooShort)
            .Must(PasswordPolicy.IsStrongEnough).WithMessage(IdentityErrors.PasswordTooWeak);
        RuleFor(x => x.PrivacyNoticeAccepted).Equal(true);
        RuleFor(x => x.PrivacyNoticeVersion).NotEmpty().MaximumLength(20).When(x => x.PrivacyNoticeAccepted);

        // G-3-05 (gün açılışı TALIMAT'ı): eski/önbelleklenmiş bir sayfadan gönderilen farklı bir sürüm sessizce
        // kabul edilmez - sayfa yenilenip metin yeniden onaylanmalı (Faz 9'un aynı kuralıyla aynı sabit değer).
        RuleFor(x => x.PrivacyNoticeVersion)
            .Equal(IdentityDefaults.KnownPrivacyNoticeVersion)
            .When(x => x.PrivacyNoticeAccepted)
            .WithMessage(IdentityErrors.InvitationPrivacyNoticeOutdated);
    }
}

public sealed class AcceptInvitationHandler(
    IInvitationRepository invitations,
    IUserRepository users,
    ITenantRepository tenants,
    IRoleRepository roles,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    ISecretGenerator secrets,
    ITokenService tokens,
    ITenantContextSetter tenantSetter,
    TimeProvider clock,
    ILogger<AcceptInvitationHandler> logger) : ICommandHandler<AcceptInvitationCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invitation = await invitations.GetByTokenHashAsync(secrets.Hash(command.Token), cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        using var scope = tenantSetter.BeginScope(invitation.TenantId);

        var accepted = invitation.Accept(now, command.PrivacyNoticeVersion);
        if (accepted.IsFailure)
        {
            return accepted.Error;
        }

        // Kalıcı rıza kaydı Faz 1'in IConsentRecorder implementasyonu bağlanınca (bkz. sınıf üstü açıklama); bu
        // turda yalnız denetlenebilirlik için log + InvitationAccepted olay yükü (Domain/Invitations/Invitation.cs).
        logger.LogInformation(
            "KVKK aydınlatma metni onayı: kullanıcı {UserId}, kiracı {TenantId}, metin sürümü {PrivacyNoticeVersion}.",
            invitation.UserId, invitation.TenantId, command.PrivacyNoticeVersion);

        var user = await users.GetByIdAsync(invitation.UserId, cancellationToken).ConfigureAwait(false);
        var tenant = await tenants.GetByIdAsync(invitation.TenantId, cancellationToken).ConfigureAwait(false);
        if (user is null || tenant is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        if (PasswordPolicy.ContainsEmailLocalPart(command.Password, user.Email))
        {
            return Error.Validation(IdentityErrors.PasswordTooWeak);
        }

        user.SetPassword(hasher.Hash(command.Password));
        user.RecordSuccessfulLogin(now);

        var roleCodes = (await roles.GetByIdsAsync(user.Roles.Select(r => r.RoleId), cancellationToken).ConfigureAwait(false)).Select(r => r.Code).ToList();
        var access = tokens.IssueAccessToken(user, tenant.Id, tenant.Slug, roleCodes, permissionVersion: 0);
        var rawRefresh = secrets.NewToken();
        var refresh = Domain.Tokens.RefreshToken.Issue(tenant.Id, user.Id, null, secrets.Hash(rawRefresh), now, tokens.RefreshTokenLifetime, command.DeviceInfo, command.IpAddress);
        refreshTokens.Add(refresh);

        return new AuthTokensDto(access.Token, access.ExpiresAtUtc, rawRefresh, refresh.ExpiresAt);
    }
}

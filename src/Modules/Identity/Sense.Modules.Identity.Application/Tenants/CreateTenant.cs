using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>Yeni şirketin ilk kullanıcısının (Owner) nasıl oluşturulacağı.</summary>
public enum TenantOwnerMode
{
    /// <summary>Owner hesabı davetli açılır; e-postadaki bağlantıyla kendi parolasını belirler.</summary>
    Invite = 0,

    /// <summary>Owner hesabı süper adminin verdiği parolayla doğrudan etkin açılır.</summary>
    Password = 1,
}

/// <summary>
/// Süper admin: yeni şirket hesabı (kiracı) oluşturur, sistem rolleri seed edilir, ilk kullanıcı Owner olur.
/// Owner/Admin joker izin setine sahip olduğu için izin kontrolü tek başına yeterli değildir: platform izinleri jokerlerden
/// hariç tutulur (<see cref="PlatformPermissions"/>) ve handler ayrıca <c>User.IsPlatformStaff</c> ister
/// (Impersonation komutlarındaki kalıpla aynı).
/// </summary>
[RequiresPermission(IdentityPermissions.PlatformTenantsManage)]
public sealed record CreateTenantCommand(
    string CompanyName,
    string Slug,
    string OwnerEmail,
    string OwnerFirstName,
    string OwnerLastName,
    TenantOwnerMode OwnerMode,
    string? Password,
    string? Phone,
    string? Language) : ICommand<CreateTenantResult>;

public sealed record CreateTenantResult(Guid TenantId, string Slug, Guid OwnerUserId, bool InvitationSent);

public sealed class CreateTenantValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantValidator(IOptions<IdentityOptions> options)
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(IdentityLimits.CompanyNameMaxLength);
        RuleFor(x => x.Slug).NotEmpty().Must(Tenant.IsValidSlug).WithMessage(IdentityErrors.TenantInvalidSlug);
        RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.OwnerFirstName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.OwnerLastName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.OwnerMode).IsInEnum();

        When(x => x.OwnerMode == TenantOwnerMode.Password, () =>
        {
            RuleFor(x => x.Password).Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(IdentityErrors.TenantOwnerPasswordRequired)
                .MinimumLength(options.Value.MinPasswordLength).WithMessage(IdentityErrors.PasswordTooShort)
                .Must(PasswordPolicy.IsStrongEnough!).WithMessage(IdentityErrors.PasswordTooWeak);
            RuleFor(x => x.Password)
                .Must((command, password) => !PasswordPolicy.ContainsEmailLocalPart(password!, command.OwnerEmail))
                .WithMessage(IdentityErrors.PasswordTooWeak)
                .When(x => !string.IsNullOrEmpty(x.Password));
        });
    }
}

public sealed class CreateTenantHandler(
    ITenantRepository tenants,
    IUserRepository users,
    IRoleRepository roles,
    IInvitationRepository invitations,
    IPasswordHasher hasher,
    ISecretGenerator secrets,
    IIdentityMailer mailer,
    IFrontendLinks links,
    IPermissionCatalog catalog,
    ITenantContextSetter tenantSetter,
    ICurrentUser currentUser,
    IIdentityUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ICommandHandler<CreateTenantCommand, CreateTenantResult>
{
    public async Task<Result<CreateTenantResult>> Handle(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        // Çağıranın kendi (platform) kiracısındaki kaydı: ambient tenant context henüz değiştirilmedi.
        var caller = await users.GetByIdAsync(callerId, cancellationToken).ConfigureAwait(false);
        if (caller is null || !caller.IsPlatformStaff)
        {
            return Error.Forbidden(IdentityErrors.PlatformStaffRequired);
        }

        var cfg = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var slug = command.Slug.Trim().ToLowerInvariant();

        if (PlatformTenant.IsPlatform(slug) || await tenants.SlugExistsAsync(slug, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(IdentityErrors.TenantSlugTaken);
        }

        var tenantResult = Tenant.Create(slug, command.CompanyName, now, cfg.TrialDays, cfg.DefaultLocale, cfg.DefaultTimeZone);
        if (tenantResult.IsFailure)
        {
            return tenantResult.Error;
        }

        var tenant = tenantResult.Value;
        tenants.Add(tenant);

        // Bu noktadan sonra yazılan tüm kiracı verileri yeni kiracıya ait olmalı. Kayıt bu kapsam İÇİNDE yapılır: çağıranın
        // ambient kiracısı platform olduğundan, kapsam kapandıktan sonra (UnitOfWorkBehaviour) yazmak çapraz-kiracı yazımı sayılırdı.
        using var scope = tenantSetter.BeginScope(tenant.Id, tenant.Slug);

        var seededRoles = TenantRoleSeeder.SeedSystemRoles(tenant.Id, catalog, roles);

        var userResult = User.Create(tenant.Id, command.OwnerEmail, command.OwnerFirstName, command.OwnerLastName,
            string.IsNullOrWhiteSpace(command.Language) ? cfg.DefaultLanguage : command.Language, command.Phone);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var owner = userResult.Value;
        // İlk hesap İK Yöneticisidir; Owner arka planda "son hesap sahibi kaldırılamaz" korumasını taşır.
        owner.SetRoles([seededRoles[SystemRoleCodes.Owner].Id, seededRoles[SystemRoleCodes.HR].Id]);

        if (command.OwnerMode == TenantOwnerMode.Password)
        {
            owner.SetPassword(hasher.Hash(command.Password!));
        }

        users.Add(owner);

        // Diğer modüller (Organization: birim tipleri, kök şirket, TR tatil takvimi) kiracı varsayılanlarını bu olayla hazırlar;
        // olay, kiracı kaydıyla aynı işlemde Identity outbox'una yazılır.
        outbox.Enqueue(new TenantProvisioned(tenant.Id, tenant.Name));

        Invitation? invitation = null;
        string? rawToken = null;
        if (command.OwnerMode == TenantOwnerMode.Invite)
        {
            invitation = AddOwnerInvitation(tenant.Id, owner, callerId, now, cfg, out rawToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (invitation is not null && rawToken is not null)
        {
            await mailer.SendInvitationAsync(owner.Email, owner.FullName, tenant.Name, links.InviteAccept(rawToken), invitation.ExpiresAt, cancellationToken)
                .ConfigureAwait(false);
        }

        return new CreateTenantResult(tenant.Id, tenant.Slug, owner.Id, invitation is not null);
    }

    private Invitation AddOwnerInvitation(Guid tenantId, User owner, Guid invitedBy, DateTime now, IdentityOptions cfg, out string rawToken)
    {
        rawToken = secrets.NewToken();
        var invitation = Invitation.Create(tenantId, owner.Id, owner.Email, secrets.Hash(rawToken), now, TimeSpan.FromHours(cfg.InvitationValidityHours), invitedBy);
        invitations.Add(invitation);
        return invitation;
    }
}

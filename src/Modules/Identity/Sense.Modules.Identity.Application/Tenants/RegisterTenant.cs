using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>Self-signup: yeni şirket hesabı (deneme), sistem rolleri seed edilir, ilk kullanıcı Owner olur.</summary>
public sealed record RegisterTenantCommand(
    string CompanyName,
    string Slug,
    string OwnerEmail,
    string OwnerFirstName,
    string OwnerLastName,
    string Password,
    string? Phone,
    string? Language) : ICommand<RegisterTenantResult>;

public sealed record RegisterTenantResult(Guid TenantId, string Slug, Guid OwnerUserId);

public sealed class RegisterTenantValidator : AbstractValidator<RegisterTenantCommand>
{
    public RegisterTenantValidator(IOptions<IdentityOptions> options)
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(IdentityLimits.CompanyNameMaxLength);
        RuleFor(x => x.Slug).NotEmpty().Must(Tenant.IsValidSlug).WithMessage(IdentityErrors.TenantInvalidSlug);
        RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.OwnerFirstName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.OwnerLastName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.Password).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(options.Value.MinPasswordLength).WithMessage(IdentityErrors.PasswordTooShort)
            .Must(PasswordPolicy.IsStrongEnough).WithMessage(IdentityErrors.PasswordTooWeak);
        RuleFor(x => x.Password)
            .Must((command, password) => !PasswordPolicy.ContainsEmailLocalPart(password, command.OwnerEmail))
            .WithMessage(IdentityErrors.PasswordTooWeak);
    }
}

public sealed class RegisterTenantHandler(
    ITenantRepository tenants,
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher hasher,
    IPermissionCatalog catalog,
    ITenantContextSetter tenantSetter,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ICommandHandler<RegisterTenantCommand, RegisterTenantResult>
{
    public async Task<Result<RegisterTenantResult>> Handle(RegisterTenantCommand command, CancellationToken cancellationToken)
    {
        var cfg = options.Value;
        if (!cfg.AllowSelfRegistration)
        {
            return Error.Forbidden(Shared.Kernel.Results.ErrorCodes.Forbidden);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var slug = command.Slug.Trim().ToLowerInvariant();

        if (await tenants.SlugExistsAsync(slug, cancellationToken).ConfigureAwait(false))
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

        // Bu noktadan sonra yazılan tüm kiracı verileri yeni kiracıya ait olmalı.
        using var scope = tenantSetter.BeginScope(tenant.Id, tenant.Slug);

        var seededRoles = SeedSystemRoles(tenant.Id, catalog, roles);

        var userResult = User.Create(tenant.Id, command.OwnerEmail, command.OwnerFirstName, command.OwnerLastName,
            string.IsNullOrWhiteSpace(command.Language) ? cfg.DefaultLanguage : command.Language, command.Phone);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var owner = userResult.Value;
        owner.SetPassword(hasher.Hash(command.Password));
        owner.SetRoles([seededRoles[SystemRoleCodes.Owner].Id]);
        users.Add(owner);

        return new RegisterTenantResult(tenant.Id, tenant.Slug, owner.Id);
    }

    /// <summary>Tüm sistem rollerini kataloğa göre izinleriyle oluşturur. Seed ve migrasyonlarda da kullanılır.</summary>
    public static IReadOnlyDictionary<string, Role> SeedSystemRoles(Guid tenantId, IPermissionCatalog catalog, IRoleRepository roles)
    {
        var result = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in SystemRoleDefinitions.All)
        {
            var role = Role.CreateSystem(tenantId, def.Code);
            var permissions = role.IsLocked
                ? catalog.All.Select(p => new RolePermission(role.Id, p.Key, (int)DataScope.Company)).ToList()
                : def.Resolve(role.Id, catalog.All).ToList();

            role.ReplacePermissionsUnchecked(permissions);
            roles.Add(role);
            result[def.Code] = role;
        }

        return result;
    }
}

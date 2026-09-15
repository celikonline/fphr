using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Identity.Domain.Impersonation;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Tokens;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext) : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ImpersonationGrant> ImpersonationGrants => Set<ImpersonationGrant>();
}

internal static class IdentityTables
{
    public const string Tenants = "tenants";
    public const string Users = "users";
    public const string UserRoles = "user_roles";
    public const string UserPermissionOverrides = "user_permission_overrides";
    public const string Roles = "roles";
    public const string RolePermissions = "role_permissions";
    public const string Invitations = "invitations";
    public const string RefreshTokens = "refresh_tokens";
    public const string ImpersonationGrants = "impersonation_grants";

    public const int EmailLength = 254;
    public const int SlugLength = 40;
    public const int HashLength = 128;
    public const int PermissionKeyLength = 150;
    public const int CodeLength = 50;
    public const int StampLength = 64;
    public const int LanguageLength = 10;
    public const int LocaleLength = 20;
    public const int TimeZoneLength = 60;
    public const int PhoneLength = 30;
    public const int TaxNumberLength = 20;
    public const string AccessSettingsColumn = "access_settings";
}

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable(IdentityTables.Tenants);
        b.HasKey(x => x.Id);
        b.Property(x => x.Slug).HasMaxLength(IdentityTables.SlugLength).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Name).HasMaxLength(Domain.IdentityLimits.CompanyNameMaxLength).IsRequired();
        b.Property(x => x.TaxNumber).HasMaxLength(IdentityTables.TaxNumberLength);
        b.Property(x => x.Locale).HasMaxLength(IdentityTables.LocaleLength);
        b.Property(x => x.TimeZone).HasMaxLength(IdentityTables.TimeZoneLength);
        b.Property(x => x.SettingsJson).HasColumnType(PersistenceDefaults.JsonbColumnType);
        b.Property(x => x.SuspendReason).HasMaxLength(Domain.IdentityLimits.ReasonMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(IdentityTables.CodeLength);

        // Erişim ayarları: tek jsonb kolon (owned/JSON column). Yeni anahtar eklemek migration istemez
        // (eksik alanlar C# varsayımına düşer); yeni bir ayar "bölümü" eklemek ayrı bir OwnsOne(...).ToJson() ister.
        b.OwnsOne(x => x.AccessSettings, a => a.ToJson(IdentityTables.AccessSettingsColumn));

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable(IdentityTables.Users);
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(IdentityTables.EmailLength).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(IdentityTables.EmailLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();
        b.Property(x => x.FirstName).HasMaxLength(Domain.IdentityLimits.NameMaxLength).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(Domain.IdentityLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(IdentityTables.PhoneLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(IdentityTables.CodeLength);
        b.Property(x => x.SecurityStamp).HasMaxLength(IdentityTables.StampLength).IsRequired();
        b.Property(x => x.PreferredLanguage).HasMaxLength(IdentityTables.LanguageLength);
        b.HasIndex(x => x.EmployeeId);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.FullName);

        b.OwnsMany(x => x.Roles, r =>
        {
            r.ToTable(IdentityTables.UserRoles);
            r.WithOwner().HasForeignKey(x => x.UserId);
            r.HasKey(x => new { x.UserId, x.RoleId });
            r.HasIndex(x => x.RoleId);
        });

        b.OwnsMany(x => x.PermissionOverrides, p =>
        {
            p.ToTable(IdentityTables.UserPermissionOverrides);
            p.WithOwner().HasForeignKey(x => x.UserId);
            p.HasKey(x => new { x.UserId, x.Permission });
            p.Property(x => x.Permission).HasMaxLength(IdentityTables.PermissionKeyLength);
        });

        b.Navigation(x => x.Roles).AutoInclude();
        b.Navigation(x => x.PermissionOverrides).AutoInclude();
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable(IdentityTables.Roles);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(IdentityTables.CodeLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(Domain.IdentityLimits.NameMaxLength);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.IsLocked);

        b.OwnsMany(x => x.Permissions, p =>
        {
            p.ToTable(IdentityTables.RolePermissions);
            p.WithOwner().HasForeignKey(x => x.RoleId);
            p.HasKey(x => new { x.RoleId, x.Permission });
            p.Property(x => x.Permission).HasMaxLength(IdentityTables.PermissionKeyLength);
            p.Property(x => x.ScopeIdsJson).HasColumnType(PersistenceDefaults.JsonbColumnType);
        });

        b.Navigation(x => x.Permissions).AutoInclude();
    }
}

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable(IdentityTables.Invitations);
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(IdentityTables.EmailLength).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(IdentityTables.HashLength).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(IdentityTables.CodeLength);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable(IdentityTables.RefreshTokens);
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(IdentityTables.HashLength).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
        b.HasIndex(x => x.FamilyId);
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(IdentityTables.HashLength);
        b.Property(x => x.DeviceInfo).HasMaxLength(Domain.IdentityLimits.DeviceInfoMaxLength);
        b.Property(x => x.IpAddress).HasMaxLength(IdentityTables.CodeLength);
    }
}

public sealed class ImpersonationGrantConfiguration : IEntityTypeConfiguration<ImpersonationGrant>
{
    public void Configure(EntityTypeBuilder<ImpersonationGrant> b)
    {
        b.ToTable(IdentityTables.ImpersonationGrants);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(Domain.IdentityLimits.ReasonMaxLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(IdentityTables.CodeLength);
        b.HasIndex(x => x.PlatformUserId);
        b.HasIndex(x => x.Status);
        b.Ignore(x => x.DomainEvents);
    }
}

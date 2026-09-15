using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// Organization module DbContext. Schema stays "org" - unchanged from the pre-existing Faz 0 skeleton
/// (Sense.Modules.Organization.Api.OrganizationModule) - even though the permission-key module prefix
/// is "organization" (see Sense.Modules.Organization.Contracts.OrganizationPermissions for why).
/// </summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "org";

    public override string Schema => SchemaName;

    public DbSet<OrgUnitType> OrgUnitTypes => Set<OrgUnitType>();

    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<OrgUnitApprovalPolicyAssignment> ApprovalPolicyAssignments => Set<OrgUnitApprovalPolicyAssignment>();
}

internal static class OrganizationTables
{
    public const string OrgUnitTypes = "org_unit_types";
    public const string OrgUnits = "org_units";
    public const string Companies = "companies";
    public const string ApprovalPolicyAssignments = "org_unit_approval_policy_assignments";

    public const int CodeLength = 50;
    public const int NameLength = 200;
    public const int RequestTypeLength = 50;
}

public sealed class OrgUnitTypeConfiguration : IEntityTypeConfiguration<OrgUnitType>
{
    public void Configure(EntityTypeBuilder<OrgUnitType> b)
    {
        b.ToTable(OrganizationTables.OrgUnitTypes);
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(OrganizationTables.CodeLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(OrganizationTables.NameLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.SortOrder }).IsUnique();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> b)
    {
        b.ToTable(OrganizationTables.OrgUnits);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(OrganizationTables.NameLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ParentId, x.Name });
        b.HasIndex(x => new { x.TenantId, x.TypeId });
        b.HasIndex(x => x.ManagerEmployeeId);
        b.HasIndex(x => x.LocationId);

        // Same-module, same-schema referential integrity; deliberately no navigation property on the
        // domain entity (aggregates stay independent - Type/Parent are referenced by Guid only).
        b.HasOne<OrgUnitType>().WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable(OrganizationTables.Companies);
        b.HasKey(x => x.Id);

        b.Property(x => x.JointEmploymentGroupCode).HasMaxLength(OrganizationTables.CodeLength);

        b.HasIndex(x => new { x.TenantId, x.OrgUnitId }).IsUnique();
        b.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OrgUnitApprovalPolicyAssignmentConfiguration : IEntityTypeConfiguration<OrgUnitApprovalPolicyAssignment>
{
    public void Configure(EntityTypeBuilder<OrgUnitApprovalPolicyAssignment> b)
    {
        b.ToTable(OrganizationTables.ApprovalPolicyAssignments);
        b.HasKey(x => x.Id);

        b.Property(x => x.RequestType).HasMaxLength(OrganizationTables.RequestTypeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.OrgUnitId, x.RequestType }).IsUnique();
        b.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.DomainEvents);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
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

    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();

    public DbSet<HolidayCalendarEntry> HolidayCalendarEntries => Set<HolidayCalendarEntry>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
}

internal static class OrganizationTables
{
    public const string OrgUnitTypes = "org_unit_types";
    public const string OrgUnits = "org_units";
    public const string Companies = "companies";
    public const string ApprovalPolicyAssignments = "org_unit_approval_policy_assignments";
    public const string HolidayCalendars = "holiday_calendars";
    public const string HolidayCalendarEntries = "holiday_calendar_entries";
    public const string Locations = "locations";
    public const string CostCenters = "cost_centers";

    public const int CodeLength = 50;
    public const int NameLength = 200;
    public const int RequestTypeLength = 50;
    public const int AddressLength = 500;

    /// <summary>HolidayCalendar's own country field (looser: not strictly validated as ISO alpha-2).</summary>
    public const int HolidayCalendarCountryCodeLength = 10;

    /// <summary>ISO 3166-1 alpha-2 country code length (e.g. "TR"), used by Location.</summary>
    public const int CountryCodeLength = 2;

    /// <summary>Location.SelfCheckInCode (S-31 QR self-servis check-in): fixed 8-char code.</summary>
    public const int SelfCheckInCodeLength = 8;
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
        // Not unique: renumbering several levels in one SaveChanges would trip a non-deferrable unique index mid-update.
        b.HasIndex(x => new { x.TenantId, x.SortOrder });

        b.Ignore(x => x.IsCompanyLevel);
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

        // LocationId was reserved (no FK) by roadmap item #4; this item (#6) adds the Location entity
        // and wires the FK now that the referenced table exists. Same module/schema as OrgUnit, so -
        // unlike ManagerEmployeeId (Employee.Contracts) - this gets a real DB constraint.
        b.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable(OrganizationTables.Locations);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(OrganizationTables.NameLength).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(OrganizationTables.CountryCodeLength).IsRequired();
        b.Property(x => x.Address).HasMaxLength(OrganizationTables.AddressLength);
        b.Property(x => x.SgkIsyeriSicilNo).HasMaxLength(OrganizationLimits.SgkIsyeriSicilNoMaxLength);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.SelfCheckInCode).HasMaxLength(OrganizationTables.SelfCheckInCodeLength).IsRequired();

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.Name });
        b.HasIndex(x => new { x.TenantId, x.SelfCheckInCode }).IsUnique();

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

        // Faz 2 (TB-10, "Şirket profili"): legal/registry + operating-hours fields, all optional.
        b.Property(x => x.RegistrationNumber).HasMaxLength(OrganizationLimits.RegistrationNumberMaxLength);
        b.Property(x => x.TaxNumber).HasMaxLength(OrganizationLimits.TaxNumberMaxLength);
        b.Property(x => x.Address).HasMaxLength(OrganizationTables.AddressLength);
        b.Property(x => x.City).HasMaxLength(OrganizationLimits.CityMaxLength);
        b.Property(x => x.Country).HasMaxLength(OrganizationLimits.CountryMaxLength);
        b.Property(x => x.OperatingHoursStart).HasColumnType("time without time zone");
        b.Property(x => x.OperatingHoursEnd).HasColumnType("time without time zone");
        b.Property(x => x.WorkingDaysCsv).HasMaxLength(OrganizationLimits.WorkingDaysCsvMaxLength).HasColumnName("working_days");
        b.Property(x => x.LogoUrl).HasMaxLength(OrganizationLimits.LogoUrlMaxLength);
        b.Ignore(x => x.WorkingDays);

        b.HasIndex(x => new { x.TenantId, x.OrgUnitId }).IsUnique();
        b.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);

        // Same-module, same-schema FK now that HolidayCalendar exists (roadmap item #5); this column
        // was a reserved Guid? with no FK when Company was created by item #4. Restrict: a calendar
        // still assigned to a company cannot be hard-deleted (there is no delete endpoint yet anyway).
        b.HasOne<HolidayCalendar>().WithMany().HasForeignKey(x => x.HolidayCalendarId).OnDelete(DeleteBehavior.Restrict);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class HolidayCalendarConfiguration : IEntityTypeConfiguration<HolidayCalendar>
{
    public void Configure(EntityTypeBuilder<HolidayCalendar> b)
    {
        b.ToTable(OrganizationTables.HolidayCalendars);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(OrganizationTables.NameLength).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(OrganizationTables.HolidayCalendarCountryCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Name });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class HolidayCalendarEntryConfiguration : IEntityTypeConfiguration<HolidayCalendarEntry>
{
    public void Configure(EntityTypeBuilder<HolidayCalendarEntry> b)
    {
        b.ToTable(OrganizationTables.HolidayCalendarEntries);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(OrganizationTables.NameLength).IsRequired();
        b.Property(x => x.Date).HasColumnType("date").IsRequired();

        b.HasIndex(x => new { x.TenantId, x.HolidayCalendarId });

        // Same-module, same-schema referential integrity; deliberately no navigation property on the
        // domain entity (see OrgUnitConfiguration's identical note for Type/Parent).
        b.HasOne<HolidayCalendar>().WithMany().HasForeignKey(x => x.HolidayCalendarId).OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> b)
    {
        b.ToTable(OrganizationTables.CostCenters);
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(OrganizationLimits.CostCenterCodeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(OrganizationTables.NameLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

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

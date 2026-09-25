using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

internal static class CompanyEventTables
{
    public const string CompanyEvents = "company_events";
    public const string CompanyEventCategories = "company_event_categories";
    public const string CompanyEventInvitees = "company_event_invitees";
    public const int LocationLength = 300;
    public const int OnlineUrlLength = 500;
    public const int CategoryNameLength = 60;
    public const int ColorLength = 7;
    public const int TitleLength = 200;
    public const int DescriptionLength = 2_000;
    public const int EnumLength = 30;
}

public sealed class CompanyEventConfiguration : IEntityTypeConfiguration<CompanyEvent>
{
    public void Configure(EntityTypeBuilder<CompanyEvent> b)
    {
        b.ToTable(CompanyEventTables.CompanyEvents);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(CompanyEventTables.TitleLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(CompanyEventTables.DescriptionLength);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(CompanyEventTables.EnumLength).IsRequired();

        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(CompanyEventTables.EnumLength);
        b.Property(x => x.Location).HasMaxLength(CompanyEventTables.LocationLength);
        b.Property(x => x.OnlineUrl).HasMaxLength(CompanyEventTables.OnlineUrlLength);
        b.Ignore(x => x.EndDate);

        b.HasMany(x => x.Invitees).WithOne().HasForeignKey(x => x.CompanyEventId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Invitees).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => new { x.TenantId, x.EventDate });
        b.HasIndex(x => new { x.TenantId, x.StartsAt });
        b.HasIndex(x => new { x.TenantId, x.CategoryId });
        b.HasIndex(x => new { x.TenantId, x.VisibilityOrgUnitId, x.EventDate });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CompanyEventInviteeConfiguration : IEntityTypeConfiguration<CompanyEventInvitee>
{
    public void Configure(EntityTypeBuilder<CompanyEventInvitee> b)
    {
        b.ToTable(CompanyEventTables.CompanyEventInvitees);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.CompanyEventId, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
    }
}

public sealed class CompanyEventCategoryConfiguration : IEntityTypeConfiguration<CompanyEventCategory>
{
    public void Configure(EntityTypeBuilder<CompanyEventCategory> b)
    {
        b.ToTable(CompanyEventTables.CompanyEventCategories);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(CompanyEventTables.CategoryNameLength).IsRequired();
        b.Property(x => x.Color).HasMaxLength(CompanyEventTables.ColorLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

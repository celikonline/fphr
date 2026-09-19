using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

internal static class CompanyEventTables
{
    public const string CompanyEvents = "company_events";
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

        b.HasIndex(x => new { x.TenantId, x.EventDate });
        b.HasIndex(x => new { x.TenantId, x.VisibilityOrgUnitId, x.EventDate });

        b.Ignore(x => x.DomainEvents);
    }
}

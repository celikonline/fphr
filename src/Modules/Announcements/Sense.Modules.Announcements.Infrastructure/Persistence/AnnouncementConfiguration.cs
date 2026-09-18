using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

internal static class AnnouncementsTables
{
    public const string Announcements = "announcements";
    public const string AnnouncementReadRecords = "announcement_read_records";

    public const int TitleLength = 200;
    public const int ContentLength = 10_000;
    public const int EnumLength = 30;
    public const int TargetAudienceLength = 200;
}

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.ToTable(AnnouncementsTables.Announcements);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(AnnouncementsTables.TitleLength).IsRequired();
        b.Property(x => x.Content).HasMaxLength(AnnouncementsTables.ContentLength).IsRequired();
        b.Property(x => x.TargetAudience).HasMaxLength(AnnouncementsTables.TargetAudienceLength);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.HasIndex(x => new { x.TenantId, x.Status, x.PublishedAt });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AnnouncementReadRecordConfiguration : IEntityTypeConfiguration<AnnouncementReadRecord>
{
    public void Configure(EntityTypeBuilder<AnnouncementReadRecord> b)
    {
        b.ToTable(AnnouncementsTables.AnnouncementReadRecords);
        b.HasKey(x => x.Id);

        b.HasIndex(x => new { x.TenantId, x.AnnouncementId, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
    }
}

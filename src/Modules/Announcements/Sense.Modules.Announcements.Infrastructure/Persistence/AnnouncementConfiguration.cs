using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Attachments;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

internal static class AnnouncementsTables
{
    public const string Announcements = "announcements";
    public const string AnnouncementReadRecords = "announcement_read_records";
    public const string AnnouncementAttachments = "announcement_attachments";
    public const string Recognitions = "recognitions";

    public const int TitleLength = 200;
    public const int EnumLength = 30;
    public const int TargetAudienceLength = 200;
    public const int FileNameLength = 255;
    public const int StorageKeyLength = 500;
    public const int ContentTypeLength = 150;
}

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.ToTable(AnnouncementsTables.Announcements);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(AnnouncementsTables.TitleLength).IsRequired();
        // Zengin metin (HTML) içerik: DB sütunu AnnouncementsLimits.ContentMaxLength ile aynı sınırı taşır (Application
        // katmanı da aynı sabitle doğrular) - açıkça belirtilmezse ModuleDbContext'in "string özniteliğine
        // HasMaxLength yoksa 500 karakter" konvansiyonu (PersistenceDefaults.DefaultStringLength) devreye girer ve
        // HTML içeriği hem EF doğrulamasında hem sütunda kırpar.
        b.Property(x => x.Content).HasMaxLength(AnnouncementsLimits.ContentMaxLength).IsRequired();
        b.Property(x => x.TargetAudience).HasMaxLength(AnnouncementsTables.TargetAudienceLength);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.HasIndex(x => new { x.TenantId, x.Status, x.PublishedAt });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AnnouncementAttachmentConfiguration : IEntityTypeConfiguration<AnnouncementAttachment>
{
    public void Configure(EntityTypeBuilder<AnnouncementAttachment> b)
    {
        b.ToTable(AnnouncementsTables.AnnouncementAttachments);
        b.HasKey(x => x.Id);

        b.Property(x => x.FileName).HasMaxLength(AnnouncementsTables.FileNameLength).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(AnnouncementsTables.StorageKeyLength).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(AnnouncementsTables.ContentTypeLength).IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.AnnouncementId });
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

public sealed class RecognitionConfiguration : IEntityTypeConfiguration<Recognition>
{
    public void Configure(EntityTypeBuilder<Recognition> b)
    {
        b.ToTable(AnnouncementsTables.Recognitions);
        b.HasKey(x => x.Id);

        b.Property(x => x.Message).HasMaxLength(AnnouncementsLimits.RecognitionMessageMaxLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.HasIndex(x => new { x.TenantId, x.ReceiverEmployeeId });
    }
}

using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

public class AnnouncementsDbContext : ModuleDbContext, IAnnouncementsUnitOfWork
{
    public const string SchemaName = "announcements";

    public AnnouncementsDbContext(DbContextOptions<AnnouncementsDbContext> options)
        : base(options, SchemaName)
    {
    }

    public override string Schema => SchemaName;

    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementReadRecord> AnnouncementReadRecords => Set<AnnouncementReadRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ApplyConfigurations(builder);
    }

    private static void ApplyConfigurations(ModelBuilder builder)
    {
        // Announcement configuration
        builder.Entity<Announcement>(b =>
        {
            b.ToTable("Announcements");

            b.HasKey(x => x.Id);

            b.Property(x => x.TenantId)
                .IsRequired();

            b.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            b.Property(x => x.Content)
                .IsRequired();

            b.Property(x => x.CreatedAt)
                .IsRequired();

            b.Property(x => x.PublishedAt);

            b.Property(x => x.Status)
                .HasConversion<string>();

            b.HasIndex(x => new { x.TenantId, x.CreatedAt })
                .HasName("IX_Announcements_TenantId_CreatedAt");

            b.HasIndex(x => new { x.TenantId, x.PublishedAt })
                .HasName("IX_Announcements_TenantId_PublishedAt");
        });

        // AnnouncementReadRecord configuration
        builder.Entity<AnnouncementReadRecord>(b =>
        {
            b.ToTable("AnnouncementReadRecords");

            b.HasKey(x => x.Id);

            b.Property(x => x.TenantId)
                .IsRequired();

            b.Property(x => x.AnnouncementId)
                .IsRequired();

            b.Property(x => x.EmployeeId)
                .IsRequired();

            b.Property(x => x.ReadAt)
                .IsRequired();

            b.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasName("IX_AnnouncementReadRecords_TenantId_EmployeeId");

            b.HasIndex(x => new { x.TenantId, x.AnnouncementId })
                .HasName("IX_AnnouncementReadRecords_TenantId_AnnouncementId");
        });
    }
}

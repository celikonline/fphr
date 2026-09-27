using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.Preferences;
using Sense.Modules.Notifications.Domain.PushDevices;
using Sense.Modules.Notifications.Domain.UserNotifications;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Notifications.Infrastructure.Persistence;

/// <summary>Notifications module DbContext (in-app notifications, e-mail delivery queue/log, preferences). Schema "notifications".</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), INotificationsUnitOfWork
{
    public const string SchemaName = "notifications";

    public override string Schema => SchemaName;

    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();

    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();

    public DbSet<PushDeviceToken> PushDeviceTokens => Set<PushDeviceToken>();

    Task INotificationsUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);
}

internal static class NotificationsTables
{
    public const string UserNotifications = "user_notifications";
    public const string Deliveries = "notification_deliveries";
    public const string Preferences = "notification_preferences";
    public const string PushDeviceTokens = "push_device_tokens";
}

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.ToTable(NotificationsTables.UserNotifications);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TemplateKey).HasMaxLength(NotificationsLimits.TemplateKeyMaxLength).IsRequired();
        b.Property(x => x.Title).HasMaxLength(NotificationsLimits.TitleMaxLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(NotificationsLimits.BodyMaxLength).IsRequired();
        b.Property(x => x.ActionUrl).HasMaxLength(NotificationsLimits.ActionUrlMaxLength);
        b.Ignore(x => x.IsRead);

        // Bell/list: newest first per user, unread counts.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.TenantId, x.UserId, x.ReadAt });

        // Idempotency under integration event re-delivery.
        b.HasIndex(x => new { x.TenantId, x.SourceEventId, x.TemplateKey, x.UserId }).IsUnique();
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable(NotificationsTables.Deliveries);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TemplateKey).HasMaxLength(NotificationsLimits.TemplateKeyMaxLength).IsRequired();
        b.Property(x => x.Channel).HasMaxLength(NotificationsLimits.ChannelMaxLength).IsRequired();
        b.Property(x => x.RecipientAddress).HasMaxLength(NotificationsLimits.AddressMaxLength).IsRequired();
        b.Property(x => x.RecipientName).HasMaxLength(NotificationsLimits.RecipientNameMaxLength).IsRequired();
        b.Property(x => x.AddressSource).HasMaxLength(NotificationsLimits.AddressSourceMaxLength).IsRequired();
        b.Property(x => x.Culture).HasMaxLength(NotificationsLimits.CultureMaxLength).IsRequired();
        b.Property(x => x.ModelJson).HasColumnType(PersistenceDefaults.JsonbColumnType).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(NotificationsLimits.StatusMaxLength);
        b.Property(x => x.LastError).HasMaxLength(NotificationsLimits.ErrorMaxLength);

        // Worker polling (cross-tenant): pending rows that are due.
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });

        b.HasIndex(x => new { x.TenantId, x.SourceEventId, x.TemplateKey, x.Channel, x.RecipientAddress }).IsUnique();
    }
}

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable(NotificationsTables.Preferences);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TemplateKey).HasMaxLength(NotificationsLimits.TemplateKeyMaxLength).IsRequired();
        b.Property(x => x.Channel).HasMaxLength(NotificationsLimits.ChannelMaxLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.UserId, x.TemplateKey, x.Channel }).IsUnique();
    }
}

public sealed class PushDeviceTokenConfiguration : IEntityTypeConfiguration<PushDeviceToken>
{
    public void Configure(EntityTypeBuilder<PushDeviceToken> b)
    {
        b.ToTable(NotificationsTables.PushDeviceTokens);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Token).HasMaxLength(NotificationsLimits.PushTokenMaxLength).IsRequired();
        b.Property(x => x.Platform).HasMaxLength(NotificationsLimits.PushPlatformMaxLength).IsRequired();
        b.Property(x => x.DeviceName).HasMaxLength(NotificationsLimits.DeviceNameMaxLength);

        // A token belongs to one device; only one active (non-revoked) owner at a time.
        b.HasIndex(x => x.Token).IsUnique().HasFilter("revoked_at IS NULL");

        // Worker fan-out: this user's active devices.
        b.HasIndex(x => new { x.TenantId, x.UserId }).HasFilter("revoked_at IS NULL");
    }
}

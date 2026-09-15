using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Notifications;
using Sense.Modules.Notifications.Domain.Preferences;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Notifications.Infrastructure.Persistence;

/// <summary>Notifications module DbContext. Schema "notifications".</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "notifications";

    public override string Schema => SchemaName;

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();
}

internal static class NotificationsTables
{
    public const string Notifications = "notifications";
    public const string Preferences = "notification_preferences";
    public const int EnumCodeLength = 30;
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable(NotificationsTables.Notifications);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TemplateKey).HasMaxLength(NotificationLimits.TemplateKeyMaxLength).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(NotificationsTables.EnumCodeLength);
        b.Property(x => x.ArgsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Link).HasMaxLength(NotificationLimits.LinkMaxLength);

        // Bell list/unread count: always by recipient, newest first.
        b.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.ReadAt });
        b.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.OccurredAt });

        b.Ignore(x => x.IsRead);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable(NotificationsTables.Preferences);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TemplateKey).HasMaxLength(NotificationLimits.TemplateKeyMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.UserId, x.TemplateKey }).IsUnique();

        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>Design-time context for `dotnet ef migrations add` (connection: SENSE_DATABASE env var or local default).</summary>
public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    private const string ConnectionStringVariable = "SENSE_DATABASE";
    private const string LocalConnectionString = "Host=127.0.0.1;Port=5432;Database=senseik;Username=postgres;Password=postgres";
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } cs ? cs : LocalConnectionString;
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, NotificationsDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new NotificationsDbContext(options, new TenantContext());
    }
}

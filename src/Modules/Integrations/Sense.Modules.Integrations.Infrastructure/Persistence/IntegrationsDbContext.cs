using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Integrations.Application;
using Sense.Modules.Integrations.Domain;
using Sense.Modules.Integrations.Domain.Webhooks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Integrations.Infrastructure.Persistence;

/// <summary>Integrations module DbContext (Faz 8 #61 - webhook subscriptions + delivery log). Schema "integrations".</summary>
public sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IIntegrationsUnitOfWork
{
    public const string SchemaName = "integrations";

    public override string Schema => SchemaName;

    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    public DbSet<WebhookDeliveryLog> WebhookDeliveries => Set<WebhookDeliveryLog>();
}

internal static class IntegrationsTables
{
    public const string WebhookSubscriptions = "webhook_subscriptions";
    public const string WebhookDeliveries = "webhook_deliveries";
    public const int EventTypeCodeLength = 60;
}

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<string>> EventTypesComparer = new(
        (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
        v => v.ToList());

    public void Configure(EntityTypeBuilder<WebhookSubscription> b)
    {
        b.ToTable(IntegrationsTables.WebhookSubscriptions);
        b.HasKey(x => x.Id);

        b.Property(x => x.Url).HasMaxLength(IntegrationsLimits.UrlMaxLength).IsRequired();
        b.Property(x => x.Secret).HasMaxLength(IntegrationsLimits.SecretLength).IsRequired();
        b.Property(x => x.IsActive).IsRequired();

        b.Property(x => x.EventTypes)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>())
            .Metadata.SetValueComparer(EventTypesComparer);
        b.Property(x => x.EventTypes).HasColumnName("event_types").HasColumnType(PersistenceDefaults.JsonbColumnType).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.IsActive });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class WebhookDeliveryLogConfiguration : IEntityTypeConfiguration<WebhookDeliveryLog>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryLog> b)
    {
        b.ToTable(IntegrationsTables.WebhookDeliveries);
        b.HasKey(x => x.Id);

        b.Property(x => x.EventType).HasMaxLength(IntegrationsTables.EventTypeCodeLength).IsRequired();
        b.Property(x => x.Error).HasMaxLength(IntegrationsLimits.DeliveryErrorMaxLength);

        b.HasIndex(x => new { x.TenantId, x.SubscriptionId, x.CreatedAt });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// TB-2: KVKK denetim izi. Append-only — hiçbir zaman güncellenmez/silinmez (yazıcı yalnız Add yapar).
/// Bu tur yalnızca Identity'nin kendi <c>IAuditLogged</c> agregatlarını (User, Role, ...) kapsar;
/// diğer modüllerin denetim izi ayrı bir sonraki turun işidir (bkz. status/faz-01/TALIMAT.md tur 3).
/// </summary>
public sealed class AuditLogEntry : ITenantEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>İşlemi yapan kullanıcı; sistem/job kaynaklı kayıtlarda null.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Görünen ad/e-posta (kullanıcı silinse bile okunur kalsın diye ayrıca saklanır, FK değildir).</summary>
    public required string Actor { get; set; }

    /// <summary><see cref="AuditActions"/> sabitlerinden biri.</summary>
    public required string Action { get; set; }

    /// <summary>Değişen agregat türü (ör. "User", "Role").</summary>
    public required string Resource { get; set; }

    public string? ResourceId { get; set; }

    /// <summary>İnsan tarafından okunabilir özet; <c>identity.audit.sensitive_view</c> yoksa API katmanında maskelenir.</summary>
    public required string Details { get; set; }
}

/// <summary>Sözleşmede (S-1) sabitlenen eylem adları — FE bu tam string'leri bekler, değiştirilmez.</summary>
public static class AuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Login = "login";
    public const string Invite = "invite";
    public const string RoleChange = "role_change";
}

internal static class AuditLogTables
{
    public const string TableName = "audit_log_entries";
    public const int ActorMaxLength = 256;
    public const int ActionMaxLength = 30;
    public const int ResourceMaxLength = 100;
    public const int ResourceIdMaxLength = 64;
    public const int DetailsMaxLength = 2000;
}

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable(AuditLogTables.TableName);
        b.HasKey(x => x.Id);
        b.Property(x => x.Actor).HasMaxLength(AuditLogTables.ActorMaxLength).IsRequired();
        b.Property(x => x.Action).HasMaxLength(AuditLogTables.ActionMaxLength).IsRequired();
        b.Property(x => x.Resource).HasMaxLength(AuditLogTables.ResourceMaxLength).IsRequired();
        b.Property(x => x.ResourceId).HasMaxLength(AuditLogTables.ResourceIdMaxLength);
        b.Property(x => x.Details).HasMaxLength(AuditLogTables.DetailsMaxLength).IsRequired();

        // Grid standart sıralaması "en yeni önce"; (tenant_id, occurred_at desc) tek indeksle hem tenant
        // izolasyonunu hem de sıralamayı karşılar.
        b.HasIndex(x => new { x.TenantId, x.OccurredAt }).IsDescending(false, true);
    }
}

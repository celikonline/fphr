using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Shared.Kernel.Domain;

namespace Sense.Shared.Infrastructure.Persistence.Audit;

/// <summary>
/// senseik-049: Identity dışındaki modüllerin (Employee/Leave/Workflow, ileride başkaları) kendi şemasında sahip
/// olduğu denetim izi tablosu. Identity'nin kendi <c>Sense.Modules.Identity.Infrastructure.Persistence.AuditLogEntry</c>
/// tipi (senseik-018) KASITLI OLARAK bu tipe taşınmadı - EF migration snapshot'ı CLR tam adına göre eşleştiği için
/// taşımak "tabloyu sil + yeniden oluştur" migration'ı üretir (83 demo denetim kaydı dahil veri kaybı riski).
/// Bunun yerine her yeni modül OutboxMessage/InboxMessage ile aynı desende (bkz. ModuleDbContext.OnModelCreating)
/// bu paylaşımlı tipi KENDİ şemasında ayrı bir tabloya (ör. employee.audit_log_entries) map eder - append-only,
/// hiçbir zaman güncellenmez/silinmez. Identity ile aynı yapı bilerek kopyalanmıştır (bkz. o dosyanın doc comment'i).
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

    /// <summary>Değişen agregat türü (ör. "Employee", "LeaveRequest", "OvertimeRequest", "ApprovalRequest").</summary>
    public required string Resource { get; set; }

    public string? ResourceId { get; set; }

    /// <summary>
    /// İnsan tarafından okunabilir özet. Action=update ve agregat <see cref="IAuditLogged.AuditedFields"/>
    /// tanımlıyorsa "{Resource} update: Alan: eski → yeni; ..." biçiminde alan bazlı diff içerir (senseik-049);
    /// aksi halde eski davranış "{Resource} {action}". identity.audit.sensitive_view yoksa API katmanında maskelenir.
    /// </summary>
    public required string Details { get; set; }

    /// <summary>
    /// Eylemin sisteme hangi yoldan ulaştığı (<see cref="Sense.Shared.Contracts.Context.AuditSources"/> - Web/Api/Import/System).
    /// Actor/ActorUserId gibi maskelenmez (E-01).
    /// </summary>
    public string? Source { get; set; }
}

/// <summary>Sözleşmede sabitlenen eylem adları - FE bu tam string'leri bekler (Identity'nin AuditActions'ıyla aynı değerler, bkz. AuditLogEntry'nin doc comment'i).</summary>
public static class AuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
}

internal static class AuditLogTables
{
    public const string TableName = "audit_log_entries";
    public const int ActorMaxLength = 256;
    public const int ActionMaxLength = 30;
    public const int ResourceMaxLength = 100;
    public const int ResourceIdMaxLength = 64;
    public const int DetailsMaxLength = 2000;
    public const int SourceMaxLength = 30;
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
        b.Property(x => x.Source).HasMaxLength(AuditLogTables.SourceMaxLength);

        // Grid standart sıralaması "en yeni önce"; (tenant_id, occurred_at desc) tek indeksle hem tenant
        // izolasyonunu hem de sıralamayı karşılar (Identity'nin AuditLogEntryConfiguration'ıyla aynı desen).
        b.HasIndex(x => new { x.TenantId, x.OccurredAt }).IsDescending(false, true);
    }
}

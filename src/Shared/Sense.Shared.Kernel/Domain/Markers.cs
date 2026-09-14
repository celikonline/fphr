namespace Sense.Shared.Kernel.Domain;

/// <summary>Kiracıya ait tablo; EF global query filter ve interceptor bu arayüze göre çalışır.</summary>
public interface ITenantEntity
{
    Guid TenantId { get; }
}

/// <summary>
/// Tüm tablolarda bulunan denetim kolonları. SaveChanges interceptor'ı:
/// Added → CreatedAt (UTC) + CreatedUserId; Modified → ModifiedDate (UTC) + ModifiedUserId. Created* alanları sonradan değişmez.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }

    Guid? CreatedUserId { get; set; }

    DateTime? ModifiedDate { get; set; }

    Guid? ModifiedUserId { get; set; }
}

/// <summary>Fiziksel silme yerine işaretleme; "SoftDelete" adlı query filter uygulanır.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAt { get; set; }

    Guid? DeletedUserId { get; set; }
}

/// <summary>Değişiklikleri alan bazında denetim kaydına (audit log) yazılır.</summary>
public interface IAuditLogged
{
    /// <summary>Denetim kaydında maskelenecek hassas alan adları.</summary>
    static virtual IReadOnlySet<string> SensitiveFields => new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>KVKK gereği anonimleştirilebilen aggregate.</summary>
public interface IAnonymizable
{
    void Anonymize();
}

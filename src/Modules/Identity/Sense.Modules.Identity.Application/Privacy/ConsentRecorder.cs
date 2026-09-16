using Sense.Shared.Contracts.Persistence;
using Sense.Shared.Contracts.Privacy;
using Sense.Shared.Kernel;

namespace Sense.Modules.Identity.Application.Privacy;

/// <summary>
/// TB-3 tur 1 (DB'siz dilim): <c>identity.consent_records</c> satırının uygulama-katmanı karşılığı - bkz.
/// docs/implementation/consent-record-sozlesmesi.md. Append-only: bir kayıt asla güncellenmez/silinmez, yalnızca
/// aynı (TenantId, SubjectType, SubjectId, ConsentType) için yeni bir kayıt geldiğinde öncekinin
/// <see cref="SupersededAt"/> alanı doldurularak "artık aktif değil" işaretlenir.
/// Identity.Domain yerine burada (Application) durur çünkü <see cref="Sense.Shared.Contracts.Privacy.ConsentSubjectType"/>/
/// <see cref="RecordConsent"/> Shared.Contracts'ta tanımlı ve Identity.Domain yalnızca Shared.Kernel'e bağımlı kalır
/// (diğer Identity.Domain portlarıyla aynı katman kuralını bozmamak için) - EF/migration geldiğinde gerekirse
/// Domain'e taşınabilir, bu bir sözleşme değişikliği değildir (Sense.Shared.Contracts.Privacy.IConsentRecorder aynı kalır).
/// </summary>
public sealed class ConsentRecord
{
    private ConsentRecord()
    {
    }

    public Guid Id { get; private init; }

    public Guid TenantId { get; private init; }

    public ConsentSubjectType SubjectType { get; private init; }

    public Guid SubjectId { get; private init; }

    public string ConsentType { get; private init; } = string.Empty;

    public string NoticeVersion { get; private init; } = string.Empty;

    public DateTimeOffset AcceptedAt { get; private init; }

    public string? IpAddress { get; private init; }

    /// <summary>Null = hâlâ aktif; dolu = aynı özne+tür için daha yeni bir kayıt geldiği an.</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    /// <summary>Null = geri alınmamış; dolu = öznenin bu rızayı geri aldığı an (G-1-02).</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public bool IsActive => SupersededAt is null && WithdrawnAt is null;

    /// <summary>G-1-09: ispat kolonları (tür/sürüm/özne) boş veya varsayılan olamaz.</summary>
    public static ConsentRecord Create(RecordConsent request) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = request.TenantId,
        SubjectType = request.SubjectType,
        SubjectId = Guard.NotDefault(request.SubjectId),
        ConsentType = Guard.NotEmpty(request.ConsentType),
        NoticeVersion = Guard.NotEmpty(request.NoticeVersion),
        AcceptedAt = request.AcceptedAt,
        IpAddress = request.IpAddress,
    };

    /// <summary>Idempotent: birden fazla çağrılırsa ilk <see cref="SupersededAt"/> korunur.</summary>
    public void Supersede(DateTimeOffset at) => SupersededAt ??= at;

    /// <summary>Idempotent geri alma: birden fazla çağrılırsa ilk <see cref="WithdrawnAt"/> korunur (G-1-02).
    /// Geri alma yeni kayıt açmaz; bu satır append-only kalır, yalnızca yaşam döngüsü damgası doldurulur.</summary>
    public void Withdraw(DateTimeOffset at) => WithdrawnAt ??= at;
}

/// <summary>
/// EF hazır olduğunda (AddAuditLog migration'ından sonra ayrı bir istek - identity.consent_records) bu portu
/// Identity.Infrastructure implement edecek. Bu turda hiçbir implementasyon/DI kaydı yok (ADR-0011: Identity EF
/// modeli dondu) - <see cref="ConsentRecorder"/> bu yüzden bilinçli olarak IdentityModule'de kayıtlı değil.
/// </summary>
public interface IConsentRecordRepository
{
    Task<ConsentRecord?> FindActiveAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId, string consentType, CancellationToken ct);

    void Add(ConsentRecord record);
}

/// <summary>
/// Sense.Shared.Contracts.Privacy.IConsentRecorder'ın iş kuralı: aynı (TenantId, SubjectType, SubjectId, ConsentType)
/// için aktif bir kayıt varsa, yeni kayıt eklenmeden önce eskisi supersede edilir (append-only "en son kayıt
/// aktiftir" modeli). <see cref="repository"/>'nin gerçek implementasyonu (EF) gelene kadar bu sınıf DI'a
/// kaydedilmez - bkz. <see cref="IConsentRecordRepository"/>'nin doc yorumu.
/// </summary>
public sealed class ConsentRecorder(IConsentRecordRepository repository, IUnitOfWork unitOfWork) : IConsentRecorder
{
    public async Task<Guid> RecordAsync(RecordConsent request, CancellationToken ct)
    {
        var active = await repository.FindActiveAsync(request.TenantId, request.SubjectType, request.SubjectId, request.ConsentType, ct);
        active?.Supersede(request.AcceptedAt);

        var record = ConsentRecord.Create(request);
        repository.Add(record);
        await unitOfWork.SaveChangesAsync(ct);
        return record.Id;
    }

    public async Task<bool> HasActiveConsentAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId, string consentType, CancellationToken ct) =>
        await repository.FindActiveAsync(tenantId, subjectType, subjectId, consentType, ct) is not null;

    /// <summary>Idempotent: aktif kayıt yoksa hiçbir şey değiştirmeden (SaveChanges dahil) <see langword="false"/>
    /// döner. Geri alma yeni bir kayıt açmaz - yeniden rıza için ayrı bir <see cref="RecordAsync"/> çağrısı gerekir.</summary>
    public async Task<bool> WithdrawAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId, string consentType, DateTimeOffset withdrawnAt, CancellationToken ct)
    {
        var active = await repository.FindActiveAsync(tenantId, subjectType, subjectId, consentType, ct);
        if (active is null)
        {
            return false;
        }

        active.Withdraw(withdrawnAt);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}

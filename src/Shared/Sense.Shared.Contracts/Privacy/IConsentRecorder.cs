namespace Sense.Shared.Contracts.Privacy;

/// <summary>
/// KVKK/aydınlatma ve rıza kayıtları için ortak sözleşme (K-2: tek sahip Faz 1). Faz 3
/// (<c>AcceptInvitationCommand.PrivacyNoticeAccepted</c>) ve Faz 9 (aday rızası) bu arayüze bağlanır;
/// implementasyon <c>identity.consent_records</c> şemasında durur (bkz. <c>docs/implementation/consent-record-sozlesmesi.md</c>).
/// </summary>
public interface IConsentRecorder
{
    /// <summary>Yeni bir rıza kaydı oluşturur ve kaydın kimliğini döner.</summary>
    Task<Guid> RecordAsync(RecordConsent request, CancellationToken ct);

    /// <summary>Belirtilen özne için verilen rıza türünde, henüz geri alınmamış aktif bir kayıt olup olmadığını sorgular.</summary>
    Task<bool> HasActiveConsentAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId, string consentType, CancellationToken ct);

    /// <summary>Aktif bir rıza kaydı varsa geri alır ve <see langword="true"/> döner; aktif kayıt yoksa hiçbir şey
    /// değiştirmeden <see langword="false"/> döner (idempotent, hata fırlatmaz). Geri alma yeni bir kayıt açmaz;
    /// yeniden rıza için ayrı bir <see cref="RecordAsync"/> çağrısı gerekir.</summary>
    Task<bool> WithdrawAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId, string consentType, DateTimeOffset withdrawnAt, CancellationToken ct);
}

/// <summary>Bir rıza kaydı isteği.</summary>
/// <param name="TenantId">Kiracı bağlamı; her rıza kaydı bir kiracıya aittir.</param>
/// <param name="SubjectType">Rızayı veren öznenin türü.</param>
/// <param name="SubjectId">Öznenin kimliği (User/Employee/Candidate tablosundaki id).</param>
/// <param name="ConsentType"><see cref="ConsentType"/> sabitlerinden biri.</param>
/// <param name="NoticeVersion">Onaylanan aydınlatma/metin sürümü (ör. "2026-01").</param>
/// <param name="AcceptedAt">Rızanın verildiği an.</param>
/// <param name="IpAddress">Rızanın verildiği isteğin IP adresi; bilinmiyorsa null.</param>
public sealed record RecordConsent(
    Guid TenantId,
    ConsentSubjectType SubjectType,
    Guid SubjectId,
    string ConsentType,
    string NoticeVersion,
    DateTimeOffset AcceptedAt,
    string? IpAddress);

/// <summary>Rıza kaydının ait olduğu özne türü.</summary>
public enum ConsentSubjectType
{
    /// <summary>Kimlik doğrulamalı platform kullanıcısı (identity.users).</summary>
    User = 1,

    /// <summary>Çalışan.</summary>
    Employee = 2,

    /// <summary>Başvuru sahibi/aday (Recruitment).</summary>
    Candidate = 3,
}

/// <summary>Bilinen rıza türü sabitleri; serbest metin yerine bu sabitler kullanılır ki sorgulanabilir kalsın.</summary>
public static class ConsentType
{
    /// <summary>KVKK aydınlatma metninin okunduğunun/anlaşıldığının kabulü (bilgilendirme, açık rıza değil).</summary>
    public const string KvkkNotice = "kvkk_notice";

    /// <summary>KVKK md. 5/2 kapsamına girmeyen işlemler için açık rıza.</summary>
    public const string ExplicitConsent = "explicit_consent";

    /// <summary>Pazarlama/bilgilendirme iletişimi izni.</summary>
    public const string Marketing = "marketing";

    /// <summary>Aday havuzunda tutulmaya rıza (Faz 9 başvuru dışı süreçler için).</summary>
    public const string CandidatePool = "candidate_pool";
}

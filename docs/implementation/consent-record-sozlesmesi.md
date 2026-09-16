# Ortak `ConsentRecord` sözleşmesi (TB-3, K-2) — Faz 1 sahipliği

KVKK/aydınlatma ve rıza kayıtları için **tek sahip Faz 1**'dir (K-2). Faz 3
(`AcceptInvitationCommand.PrivacyNoticeAccepted` — bugün doğrulanıp atılıyor, kalıcı iz bırakmıyor)
ve Faz 9 (aday rızası) bu sözleşmeye bağlanacak; kendi modüllerinde ayrı bir rıza tablosu **açmazlar**.

Bu tur yalnızca arayüz + sözleşme belgesi yayınlanır (`Sense.Shared.Contracts.Privacy`);
implementasyon (EF entity, `identity.consent_records` tablosu, migration) **sonraki bir turda** gelir.
Faz 3/9 bu aradan önce `IConsentRecorder`'a karşı kodlayabilir — implementasyon gelene kadar
DI kapsamında bir kayıt yoksa çözümleme (`GetRequiredService<IConsentRecorder>`) başarısız olur;
bu, implementasyon tamamlanmadan üretime alınmayacağının doğal bir işaretidir.

## Konum

`src/Shared/Sense.Shared.Contracts/Privacy/IConsentRecorder.cs`

```csharp
public interface IConsentRecorder
{
    Task<Guid> RecordAsync(RecordConsent request, CancellationToken ct);
    Task<bool> HasActiveConsentAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId, string consentType, CancellationToken ct);
}

public sealed record RecordConsent(
    Guid TenantId,
    ConsentSubjectType SubjectType,
    Guid SubjectId,
    string ConsentType,
    string NoticeVersion,
    DateTimeOffset AcceptedAt,
    string? IpAddress);

public enum ConsentSubjectType { User = 1, Employee = 2, Candidate = 3 }

public static class ConsentType
{
    public const string KvkkNotice = "kvkk_notice";
    public const string ExplicitConsent = "explicit_consent";
    public const string Marketing = "marketing";
    public const string CandidatePool = "candidate_pool";
}
```

## Tasarım kararları

- **Şema:** `identity.consent_records` — yeni bir modül/şema açılmaz. İzin ve kullanıcı/çalışan
  referansları zaten Identity'de durduğu için (User/Employee tabloları), rıza kayıtları da orada
  tutulur. Aday (Candidate) rızaları da aynı tabloda `SubjectType = Candidate` ile tutulur;
  Recruitment modülü kendi `candidate_id`'sini `SubjectId` olarak geçirir — Identity, Candidate
  tablosuna FK **kurmaz** (modüller arası bağımlılık sadece `*.Contracts` üzerinden), `SubjectId`
  serbest `Guid` referans olarak kalır.
- **Append-only, immutable:** Bir rıza kaydı asla güncellenmez/silinmez. "Rıza geri alma"
  (`ConsentType` bazında) ayrı bir kayıt türüyle (ör. gelecekte `RevokedAt` alanlı ikinci kayıt veya
  `revoke_...` action'ı) modellenir — bu tur kapsamında değil, `HasActiveConsentAsync` şimdilik
  "bu tür için en az bir kayıt var mı" sorgusuna karşılık gelir; geri alma modeli eklendiğinde bu
  sorgunun anlamı "en son kayıt geri alınmamış" olacak şekilde genişleyecektir (implementasyon
  notunda ayrıca işlenecek, arayüz imzası **değişmeyecek**).
- **`ConsentType` neden serbest `string`, `enum` değil:** Faz 3/9 dışında ileride yeni rıza türleri
  (ör. çerez izni, biyometrik veri izni) eklenebilir; her ekleme Identity'de yeniden derleme/deploy
  gerektirmesin diye sabitler (`const string`) kullanıldı — DB'de serbest metin olarak durur ama
  üreten taraf her zaman bu sabitlerden birini kullanmalıdır (yeni sabit eklerken Faz 1'e **bloker**
  açılır, tek taraflı yeni sabit üretilmez).
- **`NoticeVersion`:** aydınlatma/sözleşme metninin hangi sürümünün onaylandığını tutar (ör.
  `"2026-01"`); metin değiştiğinde eski rızalar geçersiz sayılmaz ama hangi sürüme rıza verildiği
  denetlenebilir kalır (KVKK ispat yükü).
- **`IpAddress`:** HTTP isteği dışında (ör. arka plan job, sistem migrasyonu) null olabilir.

## Faz 3/9 için beklenen kullanım

- Faz 3, `AcceptInvitationCommand` içinde `PrivacyNoticeAccepted == true` ise
  `IConsentRecorder.RecordAsync(new RecordConsent(tenantId, ConsentSubjectType.User, userId,
  ConsentType.KvkkNotice, noticeVersion, now, ipAddress), ct)` çağırır — bugünkü "doğrula ve at" davranışı
  kalıcı hale gelir.
- Faz 9, başvuru formunda adayın işaretlediği izinler için (aydınlatma + varsa aday havuzu rızası)
  aynı çağrıyı `ConsentSubjectType.Candidate` ile yapar.
- İkisi de implementasyon main'e girene kadar bu servisi DI'a **kaydetmeye çalışmaz** (kendi
  modülünde sahte/geçici bir `IConsentRecorder` implementasyonu yazmaz — sözleşme tek sahiplidir).

## Açık iş (sonraki Faz 1 turu)

- `identity.consent_records` EF entity + config + migration (`MIGRATION-ISTEGI.md`'ye ADR-0005'e göre eklenecek).
- `ConsentRecorder : IConsentRecorder` implementasyonu (Identity.Infrastructure).
- Rıza geri alma modelinin netleştirilmesi (bu belge güncellenecek).

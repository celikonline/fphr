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

## Tur 1 güncellemesi: `ConsentRecord` domain sınıfı + iş kuralı implementasyonu (EF hariç)

`src/Modules/Identity/Sense.Modules.Identity.Application/Privacy/ConsentRecorder.cs`: `ConsentRecord` sınıfı
(append-only, `SupersededAt` ile "artık aktif değil" işaretlenir) ve `IConsentRecorder`'ı implement eden
`ConsentRecorder` servisi — iş kuralı: `RecordAsync` önce aynı (TenantId, SubjectType, SubjectId, ConsentType)
için aktif bir kayıt var mı bakar (`IConsentRecordRepository.FindActiveAsync`), varsa yeni kaydın `AcceptedAt`
anıyla supersede eder, sonra yeni kaydı ekler. `IConsentRecordRepository` bu turda **implementasyonsuz** bir port
(Identity.Domain değil, Identity.Application'da durur — Shared.Contracts.Privacy tiplerine bağımlı olduğu için,
bkz. dosyanın başındaki yorum) — EF/migration gelmeden `ConsentRecorder` IdentityModule'de **kayıtlı değildir**,
bilinçli bir karar (DI çözümlemesi implementasyon tamamlanana kadar başarısız olmalı, bkz. yukarısı).
3 birim test: `tests/Modules/Sense.Modules.Identity.Tests/Privacy/ConsentRecorderTests.cs` (NSubstitute ile
`IConsentRecordRepository`/`IUnitOfWork` sahte, gerçek DB yok).

## Beklenen `identity.consent_records` kolonları (EF/migration henüz yok — `AddAuditLog` main'e girdikten sonra ayrı bir `MIGRATION-ISTEGI.md` girdisi olacak)

| Kolon | Tip | Null | Not |
|---|---|---|---|
| `id` | `uuid` | hayır | PK |
| `tenant_id` | `uuid` | hayır | `ModuleDbContext` tenant filtresi + otomatik index |
| `subject_type` | `smallint` | hayır | `ConsentSubjectType` enum (1=User, 2=Employee, 3=Candidate) |
| `subject_id` | `uuid` | hayır | FK yok (modüller arası bağımlılık yalnız `*.Contracts`) |
| `consent_type` | `varchar(50)` | hayır | `ConsentType` sabitlerinden biri (serbest metin, bkz. yukarısı) |
| `notice_version` | `varchar(20)` | hayır | ör. `"2026-09"` |
| `accepted_at` | `timestamp with time zone` | hayır | |
| `ip_address` | `varchar(45)` | evet | IPv6 için yeterli uzunluk; job/migrasyon kaynaklı kayıtlarda null |
| `superseded_at` | `timestamp with time zone` | evet | null = hâlâ aktif |

**İndeksler:** `(tenant_id, subject_type, subject_id, consent_type)` — `FindActiveAsync`'in birincil erişim yolu
(pratikte `WHERE ... AND superseded_at IS NULL` ile daraltılacak, kısmi index bir seçenek).

## Açık iş (sonraki Faz 1 turu)

- `identity.consent_records` EF entity + config + migration (`MIGRATION-ISTEGI.md`'ye ADR-0005'e göre eklenecek,
  yukarıdaki kolon listesiyle).
- `IConsentRecordRepository : ...` EF implementasyonu (Identity.Infrastructure) + `ConsentRecorder`'ın
  IdentityModule'de DI kaydı.
- Rıza geri alma modelinin netleştirilmesi (bu belge güncellenecek).

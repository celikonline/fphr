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
    Task<bool> WithdrawAsync(Guid tenantId, ConsentSubjectType subjectType, Guid subjectId, string consentType, DateTimeOffset withdrawnAt, CancellationToken ct);
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
- **Append-only, immutable (G-1-01 netleştirmesi):** "Append-only" şu anlama gelir: **ispat
  kolonları** (`consent_type`, `notice_version`, `accepted_at`, `ip_address`, özne alanları
  `subject_type`/`subject_id`) bir satır yazıldıktan sonra hiçbir zaman güncellenmez veya silinmez.
  Yalnız iki **yaşam döngüsü damgası** — `superseded_at` (yeni bir rıza aynı türde geldiğinde) ve
  `withdrawn_at` (özne rızayı geri aldığında) — satırın kendisinde **null → dolu** yönünde, **bir
  kez**, idempotent şekilde yazılır (`ConsentRecord.Supersede`/`Withdraw`, ikinci çağrı ilk değeri
  korur). Bu iki damgadan biri doluysa kayıt artık aktif değildir (`IsActive = SupersededAt is null
  && WithdrawnAt is null`); "geri alma" satırı silmez veya değiştirmez, yalnızca `withdrawn_at`'i
  doldurur — ispat amaçlı geçmiş, geri alınmış olsa bile sorgulanabilir kalır. Geri alma **yeni bir
  kayıt açmaz**; öznenin yeniden rıza vermesi ayrı bir `RecordAsync` çağrısıyla yeni bir satır
  oluşturur (`superseded_at`/`withdrawn_at` dolu olan eski satırlar sorgulanabilir kalmaya devam
  eder — hiçbiri silinmez).
- **`ConsentType` neden serbest `string`, `enum` değil:** Faz 3/9 dışında ileride yeni rıza türleri
  (ör. çerez izni, biyometrik veri izni) eklenebilir; her ekleme Identity'de yeniden derleme/deploy
  gerektirmesin diye sabitler (`const string`) kullanıldı — DB'de serbest metin olarak durur ama
  üreten taraf her zaman bu sabitlerden birini kullanmalıdır (yeni sabit eklerken Faz 1'e **bloker**
  açılır, tek taraflı yeni sabit üretilmez).
- **`NoticeVersion`:** aydınlatma/sözleşme metninin hangi sürümünün onaylandığını tutar (ör.
  `"2026-01"`); metin değiştiğinde eski rızalar geçersiz sayılmaz ama hangi sürüme rıza verildiği
  denetlenebilir kalır (KVKK ispat yükü).
- **`IpAddress`:** HTTP isteği dışında (ör. arka plan job, sistem migrasyonu) null olabilir.
- **G-1-04 notu (`Employee` öznesi ve KVKK hukuki dayanağı):** `ConsentSubjectType.Employee` rızaları
  **yalnızca gerçekten isteğe bağlı** işlemler için kullanılır (ör. şirket içi anket/etkinlik verisi
  paylaşımı, isteğe bağlı iletişim tercihleri). Bordro, SGK bildirimi, özlük dosyası gibi
  **iş sözleşmesinin ifası veya kanuni yükümlülük** kapsamındaki işleme KVKK md. 5/2-c (sözleşmenin
  kurulması/ifası) veya md. 5/2-ç (kanuni yükümlülük) dayanağına göre yapılır ve bu işlemler
  `IConsentRecorder.HasActiveConsentAsync` ile **kapılanmaz** — rıza yokluğu bordro/SGK/özlük
  işlemesini engellemez veya durdurmaz. Bu ayrım, "rıza her veri işleme için gerekir" yanlış
  varsayımını önlemek için burada açıkça belirtilir; ihlali (bordro akışının rızaya bağlanması)
  Faz 1'e bloker olarak bildirilir.

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

## Tur 1 (ikinci dilim): geri alma (G-1-02) + domain değişmezleri (G-1-09)

- `ConsentRecord.WithdrawnAt` (`DateTimeOffset?`) + idempotent `Withdraw(DateTimeOffset at)`;
  `IsActive => SupersededAt is null && WithdrawnAt is null`.
- `IConsentRecorder.WithdrawAsync(tenantId, subjectType, subjectId, consentType, withdrawnAt, ct)`:
  `ConsentRecorder` implementasyonu `FindActiveAsync` → aktif kayıt varsa `Withdraw` + `SaveChangesAsync` →
  `true`; aktif kayıt yoksa hiçbir şey değiştirmeden (SaveChanges dahil) `false` (idempotent, hata
  fırlatmaz). Geri alma yeni kayıt açmaz; yeniden rıza ayrı bir `RecordAsync` çağrısıyla yeni aktif
  satır üretir. 4 yeni birim test (aktif kaydı geri alır + kaydeder; aktif kayıt yoksa `false` +
  `SaveChanges` çağrılmaz; geri alma sonrası `RecordAsync` yeni aktif kayıt üretir).
- `ConsentRecord.Create` (G-1-09): `Sense.Shared.Kernel.Guard` ile ispat kolonları doğrulanır —
  `ConsentType`/`NoticeVersion` boş/yalnız boşluk olamaz (`Guard.NotEmpty`), `SubjectId` `Guid.Empty`
  olamaz (`Guard.NotDefault`) — modülün mevcut domain hata kalıbı (`Tenant`/`User` sınıflarıyla aynı
  `Guard` sınıfı). 4 yeni birim test: `ConsentRecordCreateInvariantTests`.
- Yalnız ekleme yapıldı; `RecordAsync`/`HasActiveConsentAsync` imzaları ve `ConsentRecorder`'ın henüz
  DI'a kayıtlı olmama durumu **değişmedi**.

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
| `superseded_at` | `timestamp with time zone` | evet | null = hâlâ aktif; aynı özne+tür için yeni kayıt geldiğinde doldurulur |
| `withdrawn_at` | `timestamp with time zone` | evet | null = geri alınmamış; `IConsentRecorder.WithdrawAsync` ile doldurulur (G-1-02) |

**İndeksler:**
- `(tenant_id, subject_type, subject_id, consent_type)` — `FindActiveAsync`'in genel erişim yolu.
- **Kısmi unique index (G-1-11):**
  `(tenant_id, subject_type, subject_id, consent_type) WHERE superseded_at IS NULL AND withdrawn_at IS NULL`
  — aynı özne+tür için aynı anda en fazla **bir** aktif kayıt olmasını veritabanı seviyesinde garanti
  eder (uygulama katmanındaki "önce supersede et, sonra ekle" kuralına ek güvence; yarış durumunda
  ikinci INSERT unique ihlaliyle reddedilir).

## Açık iş (sonraki Faz 1 turu)

- `identity.consent_records` EF entity + config + migration — istek `status/faz-01/MIGRATION-ISTEGI.md`'ye
  bu turda yazıldı (`AddConsentRecords`, ADR-0005), yukarıdaki kolon/index listesiyle. `IdentityDbContext`'e
  bu tur **dokunulmadı**.
  - **⚠️ Not (PC-B not-bloker):** yukarıdaki kolon listesi (S-3/TB-3 sözleşmesi, doc/`MIGRATION-ISTEGI.md`
    ile birebir) ADR-0011 EF dondurma öncesi taslaktı; ADR-0011 kuyruğu artık boşaldı (PANO: `AuditLogEntries`
    dahil 6/7 main'de) — bu, `AddConsentRecords`'un da aynı ADR-0013 kalıbıyla (izole EF grubu commit'i) bir
    sonraki turda yazılabileceği anlamına gelir, ayrıca bir onay gerekmez.
- `IConsentRecordRepository : ...` EF implementasyonu (Identity.Infrastructure) + `ConsentRecorder`'ın
  IdentityModule'de DI kaydı — migration main'e girdikten sonra.
- Rıza geri alma modeli bu turda netleşti (`WithdrawnAt`/`WithdrawAsync`, yukarısı); açık kalan tek şey EF
  tarafı.

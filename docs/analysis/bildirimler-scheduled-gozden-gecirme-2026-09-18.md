# Bildirimler: scheduled gönderim gözden geçirmesi ve teslim hattı düzeltmesi (2026-09-18)

Kapsam: `Sense.Modules.Notifications` (Domain/Application/Infrastructure/Contracts), `EmailDeliveryProcessor` teslim
hattı ve bunun `Sense.Shared.Infrastructure` transaction altyapısıyla ilişkisi.
Kaynak tasarım dokümanları: `kolayik-video/11.3.13-bildirim-ana-sayfa-duyuru-destek-ui.md` (Notifications tasarımı),
`kolayik-video/11.3.11-ats-ishe-alim.md` (gecikmeli aday e-postaları — `ScheduledAt`'in asıl tüketicisi).

## 1. Tek cümlelik hüküm

Scheduled (gecikmeli/planlı) bildirim tasarımda tanımlı ama kodda **hiç yok**; bunu şimdi spekülatif olarak inşa etmek
yerine, incelemenin ortaya çıkardığı ve scheduled gönderim geldiğinde etkisi katlanacak **mevcut bir teslim hattı
hatası** (tek transaction içinde toplu SMTP gönderimi → geri sarmada mükerrer e-posta) düzeltildi.

## 2. Düzeltilen hata — batch transaction mükerrer e-posta gönderiyordu

**Belirti.** `EmailDeliveryProcessor.ProcessAsync` due satırların tamamını (varsayılan `BatchSize` = 20) **tek bir
transaction** içinde işliyordu: her satır için SMTP gönderimi ve `SaveChangesAsync` aynı transaction'ın içindeydi,
commit ise batch'in sonundaydı.

**Neden hata.** SMTP gönderimi geri alınamaz, transaction geri alınabilir. Batch'in ortasında transaction geri
sarıldığında (worker kapanması, geçici DB hatası) o ana kadar **gerçekten gönderilmiş** e-postaların `Sent` durumu da
geri sarılıyor, satırlar `Pending` kalıyor ve bir sonraki poll'da alıcıya **ikinci kez** gidiyorlardı. Etki tek satırla
sınırlı değil: batch'teki tek bir aksaklık, o batch'te zaten teslim edilmiş **tüm** satırları mükerrer gönderime
sokuyordu.

Durumu ağırlaştıran iki altyapı gerçeği:

- `ModuleDbContext.ExecuteInTransactionAsync` (`src/Shared/.../Persistence/ModuleDbContext.cs:78-93`) delegate'i
  `Database.CreateExecutionStrategy()` ile sarıyor; geçici hatada **delegate baştan tekrar çalıştırılıyor**.
- `EnableRetryOnFailure(3)` açık (`src/Shared/.../DependencyInjection/InfrastructureServiceCollectionExtensions.cs:148`),
  yani retrying strategy gerçekten devrede.

Ayrıca `FOR UPDATE SKIP LOCKED` ile alınan 20 satırın kilidi, batch'in tüm SMTP I/O'su boyunca tutuluyordu.

**Kanıt (teorik değil).** Yazılan regresyon testi önce **eski kod üzerinde** koşturuldu ve gerçek PostgreSQL
(Testcontainers) üzerinde başarısız oldu:

```
deliveries["first@acme.test"].Status should be Sent but was Pending
```

Yani zaten gönderilmiş e-postanın satırı `Pending`'e dönüyor → mükerrer gönderim yolu açık.

**Düzeltme.** Transaction batch başına değil **satır başına**:

- Kilitsiz bir tarama yalnızca due `Id` listesini çekiyor (LINQ; ham SQL'e gerek kalmadı, mevcut
  `(Status, NextAttemptAt)` index'ini kullanıyor).
- Her satır kendi transaction'ında `FOR UPDATE SKIP LOCKED` ile yeniden sahipleniliyor (`DeliverySql.ClaimById`);
  satırı başka bir worker tutuyorsa veya satır artık due değilse sorgu boş dönüyor ve o id atlanıyor.
- Çok-worker güvenliği (aynı satırı iki worker göndermez) korunuyor; uzun transaction ve toplu tekrar-gönderim
  ortadan kalkıyor.
- Sıralama `next_attempt_at, id` oldu; `Id` Guid v7 (zaman sıralı) olduğu için eşit zaman damgalarında da FIFO
  deterministik.

**Kalan artık risk (bilinçli, kapatılmadı).** Tek bir satırın transaction'ı geçici DB hatasıyla replay edilirse **o tek**
e-posta tekrar gidebilir. Blast radius 20 satırdan 1 satıra indi ama sıfır değil. Tamamen kapatmak için gönderimden
*önce* attempt'i commit eden iki fazlı rezervasyon gerekir (`Attempts`/`NextAttemptAt` ileri alınıp commit → SMTP →
sonucu ayrı transaction'da yaz). Bu, `NotificationDelivery`'nin retry muhasebesini ve mevcut backoff testlerinin
beklentilerini değiştirdiği için ayrı bir kalem olarak bırakıldı.

## 3. Scheduled bildirimler: tasarım ↔ kod farkı

Tasarım iki ayrı "scheduled" kavramı tanımlıyor; **ikisi de kodda yok**.

### 3.1 Tek seferlik gecikmeli gönderim (`ScheduledAt`)

`11.3.13` gecikmeli gönderimin `NotificationDelivery.ScheduledAt` ile yönetileceğini söylüyor (asıl tüketici: ATS'te
"hemen / N gün sonra" gönderilen aday e-postaları, `11.3.11` — örn. 5 gün gecikmeli red e-postası). Kodda:

| Tasarımda | Kodda |
| --- | --- |
| `NotificationDelivery.ScheduledAt` | Yok. Yalnız `NextAttemptAt` var, constructor'da `now`'a sabit. |
| `CandidateEmail.Status(… Scheduled/Cancelled …)` | `NotificationDeliveryStatus`'ta `Cancelled` yok (`Pending/Sent/Failed/Skipped`). |
| `Email.CancelScheduledOnStatusChange` (planlı e-postayı iptal) | `Cancel()` metodu yok; repository'de `Add`/`ExistsAsync` dışında bir şey yok (id ile bulma yok). |
| `CandidateEmail.NotificationDeliveryId?` | `NotificationPublisher.PublishAsync` `Task` dönüyor — oluşturduğu delivery id'sini **geri vermiyor**, çağıran saklayamıyor. |
| Modüller arası tetikleme | `Notifications.Contracts` yalnız şablon anahtarı/kanal/izin sabitleri içeriyor; gönderim veya iptal için dışarıya açık **hiçbir port yok**. |

Not: teslim mekanizması aslında ileri tarihe hazır — `WHERE status=Pending AND next_attempt_at<=now` sorgusu gelecek
tarihli satırı doğal olarak beklet(ir); eksik olan, çağıranın ileri tarih verebileceği bir API ve `NextAttemptAt`'in
retry ile paylaşılmasından doğan anlam karmaşası.

Ek tasarım sorusu: dedup anahtarı `(TenantId, SourceEventId, TemplateKey, Channel, RecipientAddress)` integration
event'in tekrar teslimine karşı doğru, ama "planlanmış e-postayı gönderilmeden önce düzenle/yeniden planla"
senaryosunu düşünmüyor (repository güncelleme/silme sunmuyor).

### 3.2 Digest (`DigestMode`)

`11.3.13` `NotificationPreference{…, DigestMode(Instant/Daily)}` diyor; `NotificationPreference` yalnız `Enabled`
tutuyor. Biriktirip günde bir gönderen bir mekanizma da yok. Şu an bunu tetikleyecek bir talep yok: tek `Reminder`
kategorisi olan `documents.expiry_reminder` zaten kaynak modülde cron ile üretilip tek olayda özet sayı taşıyor.

## 4. Neden `ScheduledAt` şimdi yazılmadı

Tüketicisi olan Recruitment `CandidateEmail`/`EmailTemplate` kalemleri tasarımda `(PLAN-BLOCK)` işaretli, yani henüz
açılmamış bir faz. Çağıranı olmayan alan/port/migration üretmek CLAUDE.md'deki "hipotetik gelecek için tasarlama"
kuralına aykırı olurdu. Yukarıdaki fark tablosu, o faz açıldığında tasarımın girdisi olarak bırakılıyor.

## 5. Doğrulama

- `Sense.Modules.Notifications.Tests` **42/42** yeşil (yeni regresyon testi dahil).
- `Sense.Tests.Architecture` **52/52** yeşil.
- Yeni test (`Processor_AbortedMidBatch_KeepsTheDeliveryItAlreadySent_SoItIsNotSentTwice`) eski kodda **kırmızı**,
  düzeltmeyle **yeşil** olduğu ayrıca doğrulandı — gerçek regresyon testi.
- Değişiklik 2 dosyayla sınırlı: `EmailDeliveryProcessor.cs` ve `NotificationPublishingIntegrationTests.cs`.

## 6. Sonraki tur için açık kalemler

1. Tek satır replay'ini kapatan iki fazlı rezervasyon (bkz. §2 artık risk) — `NotificationDelivery` retry muhasebesini
   değiştirir, mevcut backoff testleri güncellenir.
2. Recruitment aday e-postası fazı açıldığında §3.1 tablosundaki beş kalem (ScheduledAt, Cancelled, publisher'ın id
   dönmesi, Contracts iptal portu, dedup/güncelleme stratejisi) tek pakette ele alınmalı.
3. `DigestMode` — tetikleyen bir ihtiyaç doğmadan açılmamalı.

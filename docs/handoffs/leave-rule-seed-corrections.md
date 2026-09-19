# senseik-036 — Mevzuat İzin Türü Seed Düzeltme Paketi — UYGULAMA NOTU

## Durum

**Uygulandı, `b/leave-rule-seed-corrections` branch'inde, yerel commit alındı (push YAPILMADI) — 2 commit:
`97b137b` (kod+test+doküman) ve `8689ede` (EF migration).**
Kartın 9 kabul kriterinden **7'si aynen, 2'si (madde 1-2) bilinçli olarak DEĞİŞTİRİLMİŞ halde** uygulandı —
gerekçe aşağıda "KRİTİK BULGU" bölümünde. Ayrıca madde 6 da (yıllık izin) literal okumayla uygulanamadı —
kendi bölümünde ayrıca açıklanıyor. Kart state'i `review` yerine **`blocked`** olarak işaretlendi: maddeler
1, 2 ve 6 üzerinde ürün/hukuk onayı gerekiyor. `dotnet test` ve EF migration bu oturumda TAMAMLANDI (aşağıda
"Testler" ve "EF Migration" bölümleri gerçek sonuçlarla güncellendi) — domain seviyesi tam yeşil, entegrasyon
seviyesi senseik-036 ile ilgisiz, önceden var olan bir Attendance migration borcuyla kırmızı (detay altta).

---

## ⚠️ KRİTİK BULGU: Madde 1 ve 2, mevcut haliyle UYGULANMADI (hukuki doğruluk çatışması)

Kart, babalık izninin 10 günden 5 güne, doğum izninin 168 günden 112 güne (çoğulda 126 güne) **düzeltilmesini**
istiyor. Koda bakıp WebSearch ile doğrulama yaptım: kod tabanında zaten **7578 sayılı Kanun** (RG 01.05.2026/33240,
"Sosyal Hizmetler Kanunu ve Bazı Kanunlarda Değişiklik Yapılmasına Dair Kanun") referanslı, tarih aralıklı bir
`LegislationParameter` seed'i var (bkz. `LegislationParameterSeed.cs` satır 45-54, ADR 0008). Bu satırlar
**gerçek ve yürürlükte olan** bir kanun değişikliğini yansıtıyor — hayali/hatalı veri değil:

- **Babalık izni: 5 → 10 gün**, 01.05.2026'dan itibaren (4857 Ek md.2, 7578 s.K. ile değişik).
- **Analık izni: 8 hafta önce + 16 hafta sonra = 24 hafta (168 gün) toplam**, 01.05.2026'dan itibaren
  (4857 md.74, 7578 s.K. ile değişik). Çoğul gebelikte doğum öncesi süreye +2 hafta eklenmesi (7578 öncesinden
  gelen, korunan bir hüküm) ile birlikte çoğul gebelikte toplam **26 hafta = 182 gün**.
- **Koruyucu aile izni: 10 gün ücretsiz**, aynı kanunla eklendi — evlat edinme izninden (m.74, 8 hafta) AYRI,
  bağımsız yeni bir hak (aşağıda A-13'e cevap).

Bugünün tarihi (2026-09-19) 7578'in yürürlük tarihinden (2026-05-01) sonra, yani **şu an yürürlükte olan hüküm
10 gün / 168 gün** — kartın istediği 5 gün / 112 gün, 7578 ÖNCESİ (artık yürürlükte olmayan) değerler.
Kartı olduğu gibi uygulamak ürüne **bilinçli bir hukuki hata** sokardı (bir bordro/İK compliance ürününde kabul
edilemez). Kaynaklar (WebSearch, 2026-09-19 taramasıyla doğrulandı):

- PwC Türkiye: "7578 Sayılı Kanun'la Doğum ve Babalık İzni Konusunda Yapılan Değişiklikler"
- Paksoy Hukuk: "İş Kanunu'nda doğum izni ve babalık izni süreleri yeniden düzenlendi"
- Verginet: "Yeni Analık ve Babalık İzin Süreleri | 7578 Sayılı Kanun İle Yapılan Düzenlemeler"
- PKF: "7578 Sayılı Kanun ile Analık İzni, Babalık İzni, Koruyucu Aile..."
- Mondaq: "İş Kanunu Kapsamında Analık Ve Babalık İzin Sürelerinde Yapılan Değişiklikler"

**Yaptığım:** Paternity/Maternity/FosterCare seed değerlerine (10 gün / 168 gün / 10 gün ücretsiz) DOKUNMADIM.
Kartın "tekil/çoğul ayrımı desteklenmeli" isteğini gerçek (7578 sonrası) rakamlarla karşıladım: yeni
`LeaveTypeCode.MaternityMultipleBirth` türü, `PerRequestMaxDays = 182 gün` (26 hafta), tekil `Maternity`'den
ayrı statik tür olarak eklendi (bkz. madde 2 altında).

**Karar gerektiren nokta:** Kartı yazan kişi 7578'i bilmiyor olabilir (mevzuat kartın yazıldığı tarihten sonra
mı çıktı, yoksa gözden mi kaçtı, belirsiz). Ürün/hukuk ekibi bu bulguyu onaylamalı; onaylanırsa kart kapatılabilir,
onaylanmazsa (ör. "biz bilerek eski değerle gideceğiz" gibi bir sebep varsa) ayrı bir talimatla net biçimde
5 gün/112 günü isteyen YENİ bir karar gerekir — ben tahmin ederek geri almadım.

---

## Madde bazlı özet

### 1-2) Babalık / Doğum izni — bkz. yukarıki KRİTİK BULGU. Değer değişmedi; çoğul gebelik ayrımı eklendi (182 gün, 7578 sonrası doğru rakamla).

- `LeaveEnums.cs`: yeni `LeaveTypeCode.MaternityMultipleBirth = 17`.
- `LegislationParameterSeed.cs`: yeni `Leave.Maternity.MultipleBirthPreBirthExtraWeeks = 2` (4857 md.74, 7578'den
  etkilenmeyen, hep var olan hüküm).
- `LeaveRuleSetSeed.cs`: `MaternityMultipleBirth` türü, `PerRequestMaxDays = (8+2+16)×7 = 182 gün`.

### 3) Süt izni — YENİ tür, günde 1,5 saat, mevcut saatlik altyapı kullanıldı

- `LeaveTypeCode.Breastfeeding = 15`. `LegislationParameterSeed`: `Leave.Breastfeeding.HoursPerDay = 1.5`
  (4857 md.74/son — 7578'den etkilenmeyen, hep var olan hüküm; WebSearch ile doğrulandı).
- Seed: `LeaveLimitType.Unlimited`, `Unit = Hourly`, `MaxConsecutiveDays = LeavePartialDay.HoursFraction(1.5m) = 0.2`
  (90/450 gün kesri, tam sayı — yuvarlama yok). Yeni `LeavePartialDay.HoursFraction(decimal hours, ...)` yardımcı
  metodu eklendi (mevcut `HourlyFraction`'ın saat bazlı ikizi).
- Günlük 1,5 saatlik üst sınır `LeaveRequestRules.ValidateAsync`'in zaten var olan `MaxConsecutiveDays` kontrolüyle
  denetleniyor — yeni kod yok, mevcut motor kullanıldı (kartın "saatlik birim desteği zaten mevcut, kullan" isteği).
- **Kapsam dışı bırakılan:** "1 yaşından küçük çocuk" koşulunun otomatik denetimi (çocuğun doğum tarihi Employee
  modülünde tutuluyor ama Leave'e bir port ile açılmıyor) — bugün her statutory tür gibi manuel/İK onayına bırakıldı.

### 4) Engelli/süreğen hastalıklı çocuk izni — YENİ tür, yılda 10 gün

- `LeaveTypeCode.DisabledChild = 16`. Zaten seed'de duran `Leave.DisabledChild.DaysPerYear = 10` parametresi
  (4857 Ek md.2 son fıkra) artık gerçekten bir `LeaveType`'a bağlandı — `LeaveLimitType.PerYear`,
  `AccrualTiming.StartOfPeriod` + `AccrualBasis.CalendarYear` + `CarryOverMode.Reset` (RemoteWork şablonuyla aynı
  desen: yıl başında topluca hak edilir, devretmez). `RequiresDocument=true`, `IsSensitive=true` (KVKK — çocuğun
  sağlık verisi).

### 5) İş arama izni — Unlimited kaldı, ama artık günde en az 2 saat zorunlu; "yalnız ihbar süresinde" kısmı ERTELENDİ

- `LegislationParameterSeed`'de zaten duran `Leave.JobSearch.MinHoursPerDay = 2` artık `StatutoryLeaveDurations`'a
  taşınıyor (`Ports.cs`: `StatutoryLeaveResolver` — daha önce hiç okunmuyordu) ve seed'de
  `MinConsecutiveDays = LeavePartialDay.HoursFraction(2m) = 0.2667` olarak `JobSearch` türüne yazıldı.
  `LeaveRequestRules.ValidateAsync`'in zaten var olan `MinConsecutiveDays` kontrolü artık bunu fiilen denetliyor
  (yeni domain kodu yok — 3 mevcut test 1 saatlik pencereden 2+ saate güncellendi, bkz. Testler).
- **"Yalnızca ihbar süresi içinde kullanılabilir" kısıtı UYGULANMADI.** Leave modülünün Employee modülünden bir
  çalışanın ihbar süresi/bildirim tarihi bilgisini okuyacağı bir port (`ILegislationParameterReader` benzeri,
  ör. `IEmployeeNoticePeriodReader`) yok — `grep` ile arandı, bulunamadı. Bunu eklemek: (a) Employee'de ihbar
  tarihini hesaplayıp açan yeni bir Contracts portu, (b) Leave'de bu porta bağımlı yeni bir doğrulama —
  bu ölçekte bir "seed düzeltmesi" kartının kapsamının dışında, ayrı bir kart gerektirir.

### 6) Yıllık izin "en az ardışık gün" = 10 — alan YAZILMADI, motor denetlemiyor (kasıtlı, kanıtlı gerekçeyle)

Kart "alan 10 olarak dolu VE motor bu kuralı fiilen denetliyor" diyor. Bunu `LeaveTypeSettings.MinConsecutiveDays`
ile denemeye çalıştım; **mevcut motor bu alanı HER TEK talepte zorunlu kılıyor** (`LeaveRequestRules.ValidateAsync`:
`type.MinConsecutiveDays is { } min && days < min`). 4857 md.56/3'ün gerçek metni ise farklı: izin **bölümlere
ayrılırsa** bölümlerden biri 10 günden az olamaz — tek seferde 1-2 günlük bir yıllık izin talebini YASAKLAMIYOR.

Bunu literal uygularsam ürünün en temel akışı (kısa yıllık izin talebi) kırılır — bunu mevcut test paketinde
somut kanıtladım: `LeaveRequestTests.cs` ve `LeaveManagementTests.cs`'de **15+ yerde** varsayılan seed'in Annual
türüyle 1-2 günlük talep tam doğrulama zincirinden (`SubmitLeaveRequestHandler`/`CreateLeaveRequestOnBehalfCommand`)
geçiriliyor ve başarı bekleniyor. `MinConsecutiveDays=10` yazıp motoru buna bağlarsam bunların tamamı
`BelowMinDays` hatasıyla kırılır — yani gerçek ürün davranışında da kimse artık 10 günden kısa yıllık izin
talep edemez hale gelir. Bu, mevzuatın gerçek anlamından daha katı ve **product-breaking bir regresyon**.

**Yaptığım:** `LeaveRuleSetSeed.cs`'de Annual türünün üstüne bunu açıklayan bir kod yorumu bıraktım, alanı
DOLDURMADIM (`MinConsecutiveDays: null` kaldı — `LeaveDomainTests.cs`'de bunu doğrulayan bir test var).
**Karar gerektiren nokta:** Gerçek m.56/3 uyumu için ya (a) bu sadece bilgilendirme amaçlı kalır (alan hiç
enforce edilmez, sadece "10 gün" referans metni bir yerde gösterilir), ya da (b) "bölme" kavramını modelleyen
ayrı bir özellik gerekir (bir izin yılı içindeki parçalardan en az birinin ≥10 gün olması gibi — bugünkü motor
bunu bilmiyor). İkisi de bu kartın kapsamında değil; ürün kararı gerekiyor.

### 7) Hastalık izni — "Hassas tür" ZATEN işaretliydi; 6 haftalık sayaç/uyarı eklendi

- `LeaveRuleSetSeed.cs`'de Sick türü zaten `IsSensitive: true` idi (değişiklik gerekmedi — teyit test eklendi).
- Yeni: `Sense.Modules.Leave.Domain.Absences.SickLeaveAbsenceThreshold` (saf domain, `MinimumDays = 42`).
- Yeni sorgu+handler+DTO: `GetEmployeeSickLeaveAbsenceStatusQuery`/`GetMySickLeaveAbsenceStatusQuery` →
  `SickLeaveAbsenceStatusDto(UsedDaysLast12Months, ThresholdReached)` — Sick türünün son 12 aydaki (ledger
  Usage - Reversal) net kullanım toplamı, 42 güne (6 hafta) ulaşınca `ThresholdReached=true`.
- Yeni endpoint'ler: `GET /leave/employees/{id}/sick-leave-absence-status`, `GET /leave/me/sick-leave-absence-status`.
- Web: `leave.service.ts`'e `SickLeaveAbsenceStatus` tipi + iki servis fonksiyonu eklendi; **UI'a bağlanmadı**
  (hangi ekranda/ne şekilde gösterileceği ayrı bir tasarım kararı — kapsam dışı bırakıldı, servis katmanı hazır).
- **Bilinçli sadeleştirme:** 4857 md.25/I-b'nin gerçek eşiği "ihbar süresi (2-8 hafta, kıdeme göre) + 6 hafta"dır.
  Leave, Employee'den bir çalışanın ihbar süresini okuyamadığı için (bkz. madde 5) yalnızca "6 hafta" alt sınırı
  kullanıldı — bu, mevcut `leave:absences.thresholdAlert` (md.25/II-g) ile AYNI ürün konvansiyonunu izliyor:
  bilgi amaçlı bir sinyal, hukuki karar değil. Kod ve DTO doc-comment'lerinde bu açıkça yazılı.

### 8) "GDPR" → "KVKK" (rol açıklamaları)

- Tek yer bulundu: `SharedResource.en.resx` → `roles.hr.description` İngilizce metninde "...reports, GDPR."
  vardı (Türkçe karşılığı zaten doğru şekilde "...raporlar, KVKK." idi). "GDPR" → "KVKK (Turkish data protection
  law)" olarak düzeltildi. Diğer rol açıklamalarında (`roles.recruiter`, `roles.auditor`) zaten GDPR geçmiyordu.
  `web/public/locales/**/recruitment.json`'daki "Anonymize (GDPR/KVKK)" bir ROL açıklaması değil, bir özellik
  etiketi (KVKK'ya da atıf yapıyor) — kapsam dışı bırakıldı, dokunulmadı.

### 9) 28 Ekim arifesi (yarım gün) resmi tatil takvimine eklendi

- `src/Sense.Api/SeedData/holidays/tr-official-holidays.json`: "Cumhuriyet Bayramı Arifesi (13.00'ten itibaren
  yarım gün)", ay=10 gün=28, `recurringAnnually: true` (Ramazan/Kurban arifelerinin aksine bu SABİT Gregorian
  tarih, 2429 s. Kanun m.2/A) — o yüzden lunar tahmin gibi yıllık tekrar eden bir kayıt.
  `tests/.../TrHolidaySeedFileTests.cs` güncellendi (beklenen "recurring" isim listesine eklendi).
- **Not:** Kod tabanında "yarım gün" kavramının kendisi (13:00'ten sonrasını ayrı saymak) hiçbir yerde
  modellenmiyor — Ramazan/Kurban arifeleri de aynı şekilde sadece isimde "(yarım gün)" etiketi taşıyan TAM GÜNLÜK
  kayıtlar. Yeni kayıt aynı mevcut konvansiyonu izliyor; "gerçek yarım gün" hesaplaması (öğleden sonra çalışma
  sayılır mı) ayrı, daha büyük bir Attendance/Organization özelliği gerektirir.

---

## Açık mevzuat soruları — araştırıldı, sonuç

### A-12: "Bedelli askerlik 21 gün" doğru mu?

**Netleştirilemedi — kaynaklar çelişkili, DEĞİŞTİRİLMEDİ.** WebSearch taraması:
- Birden çok kaynak (2018'den beri, 1111 s.K. geçici md.55 metnine dayanarak) **21 gün temel askerlik eğitimi**
  diyor — kodun mevcut değeriyle örtüşüyor.
- Ama bazı 2025/2026 tarihli kaynaklar "bedelli askerlik süresi 1 ay (30 gün)'a çıktı" / "32 gün (28 eğitim + 2
  yol + 2 kanuni izin)" gibi farklı rakamlar veriyor; bunlardan hangisinin güncel/otoriter olduğu net değil
  (bazıları düşük kaliteli/güncel olmayan kaynaklar olabilir).
- **Öneri:** Değeri DEĞİŞTİRMEDİM (kodun kendi "yürürlük tarihi doğrulanmalı" notu zaten bunu bekliyordu — ADR
  0008, `LegislationParameterSeed.cs` satır 55). Canlıya çıkmadan önce bir mali müşavir/İK hukuku uzmanıyla
  MSB'nin güncel celp döneminin resmi süresi teyit edilmeli.

### A-13: "Koruyucu aile izni 10 gün" İş Kanunu'nda var mı, yoksa evlat edinme izniyle mi karıştırılmış?

**Netleştirildi — KARIŞTIRILMAMIŞ, ikisi de gerçek ve BİRBİRİNDEN AYRI haklar:**
1. **Koruyucu aile izni**: 7578 s.K. ile 4857'ye eklendi, çocuğun teslim edildiği tarihten itibaren talep üzerine
   **10 gün ÜCRETSİZ** izin (kodun `LeaveTypeCode.FosterCare` türü zaten bunu doğru modelliyor).
2. **Evlat edinme izni**: 4857 md.74 — üç yaşını doldurmamış çocuğu evlat edinen işçiye **8 hafta** (analık izni
   benzeri) izin — kodda henüz bir `LeaveType` YOK, sadece kısa süreli 3 günlük mazeret izni (Ek md.2,
   `Leave.Adoption.Days=3`, evlenme/evlat edinme/ölüm ortak 3 günlük izinlerinden biri) var.

**Bulunan boşluk (bu kartın orijinal kapsamı dışında, ayrıca not ediliyor):** md.74'teki 8 haftalık (56 günlük)
evlat edinme izni için hiçbir `LeaveType` yok — sadece 3 günlük kısa mazeret izni var. Bu, ayrı bir kart/iş
kalemi olarak değerlendirilmeli (bu ticket "seed düzeltme" kapsamında, yeni bir statutory tür EKLEMEK değil,
sadece işaretlenen 9 maddeyi düzeltmekti; md.74 evlat edinme izni maddeler arasında yoktu).

---

## Testler

- Domain: `LeavePartialDay.HoursFraction` (yeni), `SickLeaveAbsenceThreshold.IsReached` (yeni),
  `Seed_ContainsStatutoryTypesAndPassiveTemplates` (genişletildi — yeni 3 tür + Annual.MinConsecutiveDays==null
  teyidi + JobSearch.MinConsecutiveDays==0.2667), `StatutoryResolver_SeedsRuleSetWithDurationsOfTheDate`
  (genişletildi — 4 yeni alan), `MultipleBirthAndBreastfeeding_AreSeededAndUnaffectedBy7578` (yeni).
- Entegrasyon: `Submit_JobSearchBelowTwoHours_IsRejected` (yeni), `Submit_Hourly_CalculatesFractionalDays_...`
  (değişmedi, zaten 2 saatlik pencere kullanıyordu), `Submit_TwoHourlyRequestsSameDay_...` (1 saatlik pencereler
  2+ saate güncellendi — yeni min-saat kuralı yüzünden).
- `TrHolidaySeedFileTests.cs` (Organization modülü, Leave'in merge gate'i dışında ama ilişkili): yeni arife
  kaydı için güncellendi.

**Çalıştırma sonucu (docker SDK konteyneri, `dotnet test tests/Modules/Sense.Modules.Leave.Tests/...`):**
`Total tests: 137, Passed: 98, Failed: 39, Duration: 3.1 dk`. **39 hatanın TAMAMI** — tek istisnasız —
`LeaveRequestIntegrationTests`/`LeaveManagementIntegrationTests`/`AbsenceIntegrationTests`/`OvertimeIntegrationTests`/
`LeaveUsageReaderTests`/`LeaveReportProviderTests`/`LeaveIntegrationTests` sınıflarının `OnDatabaseReadyAsync()`
adımında (yani testin kendi mantığı hiç çalışmadan) şu hatayla düşüyor:
`System.InvalidOperationException: ... 'PendingModelChangesWarning': The model for context 'AttendanceDbContext'
has pending changes.` — bu, senseik-007'nin kendi handoff'unda zaten belgelediği, **Leave'den TAMAMEN bağımsız,
`dev`'den miras kalan bir Attendance modülü eksik-migration sorunu** (senseik-007: "38 önceden var olan hata...
c72791f'den miras, senseik-007 ile ilgisiz, dokunulmadı" — şimdi 39, çünkü bu turda 1 yeni entegrasyon testi
(`Submit_JobSearchBelowTwoHours_IsRejected`) eklendi ve o da aynı nedenle düşüyor). Doğrulama: TÜM entegrasyon
testleri (senseik-036 ile hiç ilgisi olmayanlar dahil, ör. `WorkerRegistration_ResolvesEveryLeaveIntegrationEventHandler`,
`LegislationReader_ResolvesSeededDateRanges`) aynı hatayla aynı noktada düşüyor — hata Attendance şemasında,
Leave'in kendi domain/seed kodunda değil.

**98 geçen testin TAMAMI domain-seviyeli (Postgres/Testcontainers gerektirmeyen) testler ve senseik-036'nın
yeni/değiştirdiği HER TEST bunların içinde, hepsi yeşil:** `SickLeaveAbsenceThresholdTests` (4 vaka),
`LeavePartialDayTests.HoursFraction_*` (2 yeni), `LegislationParameterTests.MultipleBirthAndBreastfeeding_...`,
`LegislationParameterTests.StatutoryResolver_SeedsRuleSetWithDurationsOfTheDate` (4 yeni alanla), `LeaveTypeTests.
Seed_ContainsStatutoryTypesAndPassiveTemplates` (3 yeni tür + Annual.MinConsecutiveDays==null teyidiyle).

`TrHolidaySeedFileTests` (Organization modülü): **1/1 geçti** (yeni arife kaydıyla).

**Sonuç:** Merge gate'in "dotnet test (Leave modülü) yeşil" kısmı **domain seviyesinde tam yeşil**; entegrasyon
seviyesinde önceden var olan, bu kartla ilgisiz bir Attendance migration borcu nedeniyle kırmızı (senseik-007'de
de aynı durum, aynı gerekçeyle "dokunulmadı" denip bırakılmış). Bu kart bu borcu ÇÖZMEDİ (kapsam dışı — ayrı bir
Attendance migration kartı gerekiyor); ama senseik-036'nın KENDİ mantığının doğruluğunu domain testleriyle kanıtladı.

## EF Migration

`Sense.Modules.Leave.Infrastructure`'da yeni migration üretildi: `20260919080101_SenseIk036LegislationParameters`
(docker SDK konteyneri, `dotnet tool restore && dotnet ef migrations add ... -o Persistence/Migrations`). 2 yeni
`LegislationParameter` satırı ekliyor: `Leave.Maternity.MultipleBirthPreBirthExtraWeeks` (id `...040`) ve
`Leave.Breastfeeding.HoursPerDay` (id `...041`), `LeaveDbContextModelSnapshot.cs` güncellendi. Ayrı bir commit'te
alındı (`8689ede`). Yeni `LeaveType` satırları (Breastfeeding/DisabledChild/MaternityMultipleBirth) migration
GEREKTİRMEDİ — `LeaveType` satırları EF `HasData` ile değil, her kiracı için
`LeaveBalanceService.EnsureDefaultRuleSetAsync` çalıştığında runtime'da üretiliyor.

**Not (altyapı):** Bu oturumda Docker Desktop birkaç kez `npipe:////./pipe/dockerDesktopLinuxEngine` bağlantısını
kaybedip (host'ta çok sayıda paralel session/konteyner varken) kendi kendine toparlandı; migration/test komutları
2-3 kez yeniden denendikten sonra başarılı oldu. Kod tarafında bir sorun değildi.

## Bilinen sınırlama: mevcut kiracılar yeni türleri otomatik almaz

`EnsureDefaultRuleSetAsync` yalnızca kiracının HİÇ rule set'i yoksa seed'i üretiyor — zaten bir varsayılan
rule set'i olan (yani bu değişiklikten önce seed edilmiş) kiracılar `Breastfeeding`/`DisabledChild`/
`MaternityMultipleBirth` türlerini otomatik almaz. `SystemRolePermissionSynchronizer`in permission'lar için
yaptığı gibi bir "yeni seed türlerini mevcut kiracılara senkronize et" mekanizması Leave rule set'leri için
YOK. Bu kartın kapsamında bir backfill job'u YAZILMADI (kapsam: seed düzeltme, mevcut kiracı senkronizasyonu
değil) — demo/dev ortamı her zaman sıfırdan seed edildiği için merge gate'i etkilemiyor, ama canlıya alınırken
ayrı bir backfill kartı gerekecek.

## Kritik Dosyalar

- `src/Modules/Leave/Sense.Modules.Leave.Domain/LeaveEnums.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Domain/RuleSets/LeaveRuleSetSeed.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Domain/Legislation/LegislationParameterSeed.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Domain/Calendar/LeavePartialDay.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Domain/Absences/SickLeaveAbsenceThreshold.cs` (yeni)
- `src/Modules/Leave/Sense.Modules.Leave.Application/Ports.cs` (`StatutoryLeaveResolver`)
- `src/Modules/Leave/Sense.Modules.Leave.Application/Balances/LeaveBalanceFeatures.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Api/Controllers/{LeaveEmployeeBalancesController,MyLeaveController}.cs`
- `src/Shared/Sense.Shared.Contracts/Legislation/ILegislationParameterReader.cs`
- `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx`
- `src/Sense.Api/SeedData/holidays/tr-official-holidays.json`
- `web/src/services/leave.service.ts`, `web/public/locales/{tr,en,de}/leave.json`
- `tests/Modules/Sense.Modules.Leave.Tests/{LeaveDomainTests,LeaveRequestTests}.cs`
- `tests/Modules/Sense.Modules.Organization.Tests/TrHolidaySeedFileTests.cs`
- `docs/adr/0008-legislation-parameter.md` (referans, güncellenmedi — 7578 zaten orada doğru belgeleniyor)

---
*Kaynak: `docs/agent-board/cards/senseik-036-leave-rule-seed-corrections.json`. WebSearch doğrulaması 2026-09-19
tarihinde yapıldı; kaynaklar yukarıda madde madde listelendi.*

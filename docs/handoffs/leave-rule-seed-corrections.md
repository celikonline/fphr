# senseik-036 — Mevzuat İzin Türü Seed Düzeltme Paketi — UYGULAMA NOTU

Dal: `b/leave-rule-seed-corrections` (dev üstüne rebase edildi). Durum: **review** — babalık/doğum/bedelli askerlik/koruyucu aile
kalemleri hukuki karar nedeniyle **bilinçli olarak ertelendi** (aşağıda).

## Hukuki karar (kart kapsamı daraltıldı)

Kart, babalık iznini 10→5 gün, doğum iznini 168→112 gün (çoğulda 126) çekmeyi istiyordu. Roadmap dokümanları (faz1b #22, 11.7) ve
`LegislationParameterSeed` 7578 sayılı Kanun'a (RG 01.05.2026/33240: 16 hafta doğum sonrası / 10 gün babalık) dayanıyor ve kartla
çelişiyor; hukuki onay yok. Karar: **mevcut babalık (10 gün), doğum (8+16 hafta = 168 gün), bedelli askerlik (21 gün) ve koruyucu aile
(10 gün ücretsiz) değerlerine dokunulmadı.** İlk turdaki çoğul gebelik türü (`MaternityMultipleBirth`, 182 gün) de bu ertelenen doğum
kaleminin parçası olduğu için **geri alındı** (enum değeri, seed, yasal parametre, i18n anahtarları kaldırıldı).

Onay gelirse yapılacaklar: `LegislationParameterSeed` içindeki Paternity/Maternity satırlarının tarih aralığını revize eden yeni bir
migration (ADR 0008 tarih aralıklı model), çoğul gebelik için ayrı tür (kartın önerdiği 126 gün ya da 7578 sonrası 182 gün — hangisinin
geçerli olduğu hukuk onayına bağlı). Kodda kullanılmayan sabit eklenmedi; değerler yalnızca bu notta.

## Sevk edilenler

| Kalem | Uygulama |
|---|---|
| Süt izni (yeni tür) | `LeaveTypeCode.Breastfeeding`, `PerRequest` (tek gün, en çok 1 gün), `Hourly`, `MaxConsecutiveDays = 1,5 saat = 0,2 gün kesri` (yasal parametre `Leave.Breastfeeding.HoursPerDay`, yeni satır id `...042`). "<1 yaş" koşulu otomatik denetlenmiyor (çocuk doğum tarihi Leave'e port ile açık değil). |
| Engelli/süreğen hastalıklı çocuk izni | `LeaveTypeCode.DisabledChild`, `PerYear`, 10 gün/yıl (mevcut `Leave.DisabledChild.DaysPerYear`), takvim yılı başında hak edilir, devretmez, belge zorunlu, hassas tür. |
| İş arama izni | "Limitsiz" kalktı: `PerRequest` (tek gün), `MinConsecutiveDays` = günde en az 2 saat (0,2667 gün; parametre `Leave.JobSearch.MinHoursPerDay` artık `StatutoryLeaveResolver` ile okunuyor). **"Yalnızca ihbar süresi içinde" kısıtı uygulanmadı**: Employee modülünde ihbar süresi/bildirim başlangıcı verisi yok (sonlandırma yalnız etkin son iş gününü kaydeder), Leave'e okuma portu da yok. Ayrı kart gerekir (Employee'de ihbar başlangıcı + Contracts portu). |
| Yıllık izin "en az ardışık gün" = 10 (m.56/3) | Alan 10 ile seed'lendi ve motor denetliyor — ama **talep başına minimum olarak değil** (bu her kısa yıllık izni yasaklar ve 15+ mevcut testi/ürün akışını kırar; m.56/3 bölünen izinde bir parçanın ≥10 gün olmasını ister). `AnnualLeaveSplitRule` + `LeaveRequestRules`: kısa (<10 gün) bir self-servis talep, kalan hakkı 10 günlük bir bloğa yetmeyecek hale getiriyorsa ve dönemde henüz ≥10 günlük bir talep yoksa `leave.errors.annual_minimum_block` ile reddedilir. Bakiye zaten bir bloğa yetmiyorsa (açılış bakiyesi, kısmi hak ediş) kural devreye girmez; İK adına giriş (`requireSelfService:false`) işverenin bölme kararı sayıldığından bypass. Modellenmeyenler: "en çok 3 bölüm" sınırı. Önizlemede uyarı olarak görünür. |
| Hastalık izni | "Hassas tür" zaten işaretliydi (teyit testi eklendi). 6 hafta (42 gün) devamsızlık sayacı: `SickLeaveAbsenceThreshold`, `GetEmployeeSickLeaveAbsenceStatusQuery` / `GetMySickLeaveAbsenceStatusQuery` (son 12 ay net Sick kullanımı), uçlar `GET /leave/employees/{id}/sick-leave-absence-status` ve `GET /leave/me/sick-leave-absence-status`, web `leave.service.ts` fonksiyonları. Yalnız 6 hafta alt sınırı (ihbar süresi eklenmedi — aynı veri boşluğu); bilgi amaçlı; **UI'a bağlanmadı**. |
| GDPR → KVKK | `SharedResource.en.resx` `roles.hr.description`. |
| 28 Ekim arifesi | `tr-official-holidays.json`, yıllık tekrar eden kayıt, adı "(13.00'ten itibaren yarım gün)". Kod tabanında "gerçek yarım gün" modeli yok; Ramazan/Kurban arifeleri gibi isim etiketli tam günlük kayıt. |

## Mevcut kiracılar

`EnsureDefaultRuleSetAsync` yalnızca hiç kural seti olmayan kiracıyı seed'ler; yeni türler mevcut kiracılara kendiliğinden ulaşmazdı.
`LeaveRuleSetSeedSync` (domain) + `LeaveBalanceRecalculationJob` (Worker, açılışın 1 dk sonrası ve her gün 01:00 UTC) sistem-seed kural
setlerine eksik türleri (Breastfeeding, DisabledChild) ekler ve **yalnızca hâlâ eski seed değerinde** olan alanları yükseltir
(Annual `MinConsecutiveDays` boşsa 10; JobSearch hâlâ Limitsiz + min boşsa yeni kural). İK'nın özelleştirdiği değerlere ve
`IsSystemSeed=false` (kopya/özel) setlere dokunmaz; idempotent; eklenen PerYear türün hak ediş satırları aynı çalıştırmada üretilir.
Bilinen sınırlama: yeni satır yalnızca Worker çalışınca gelir (API'den önce Worker'ın bir kez dönmesi gerekir).

## i18n

Yeni tür adları: `SharedResource{,.en}.resx` (`leave.type.breastfeeding`, `leave.type.disabled_child`), web `leave.json` tr/en/de
(`type_*` + `code.*`), mobil `LEAVE_TYPE_NAMES`, web izin türü diyaloğu kod listesi. Yeni hata: `leave.errors.annual_minimum_block`
(resx tr/en).

## Migration

`20260919123123_SenseIk036LeaveRuleSeedCorrections`: tek `legislation_parameters` satırı (`Leave.Breastfeeding.HoursPerDay`, id
`00000008-0000-0000-0000-000000000042`; dev'deki severance satırları 040/041'i kullandığı için ilk turun 040/041 migration'ı atıldı).
LeaveType satırları HasData değil, runtime'da üretildiği için migration gerektirmiyor. `dotnet ef migrations has-pending-model-changes`:
"No changes have been made to the model since the last migration."

## Doğrulama (dürüst)

Docker kapalıydı; Testcontainers/entegrasyon testleri **çalıştırılmadı**.
- `dotnet build` temiz (0 hata): Sense.Api, Sense.Migrator, Sense.Worker, Leave.Tests, Organization.Tests.
- Leave.Tests konteyner gerektirmeyen 127 test yeşil (yeni: `AnnualLeaveSplitRuleTests`, `LeaveRuleSetSeedSyncTests`, genişletilmiş
  seed/legislation testleri, süt/engelli çocuk/iş arama/hastalık assert'leri). `TrHolidaySeedFileTests` 2/2 yeşil.
- Web: `tsc --noEmit` ve `eslint` (leave bileşenleri + servis) temiz; `vitest run src/components/leave src/lib` 189/189. Mobil `tsc`
  bu ortamda çalışmıyor (TS7 + mobil tsconfig `paths` TS5090, değişiklikten bağımsız); mobil değişiklik bir birleşim tipine iki değer +
  iki harita satırı.
- **Yazılıp çalıştırılamayan entegrasyon testleri:** yeni `Submit_AnnualLeave_RejectsShortRequestThatStrandsTheMinimumBlock_ButAllowsOthers`
  ve `Submit_JobSearchBelowTwoHours_IsRejected`; m.56/3 nedeniyle güncellenen `Submit_Approve_PostsUsageAtStartDate_...` (6 gün → 4 gün,
  kalan tam 10) ve `RequestableTypes_Preview_...` (6 günlük önizleme artık `annual_minimum_block` uyarısı verir). Bunlar konteyner
  açılınca ilk iş çalıştırılmalı. Önceki turda belgelenen 39 Attendance `PendingModelChangesWarning` entegrasyon hatası dev'den miras,
  bu karttan bağımsız.

## Ertelenen / açık kalemler

1. Babalık 5 gün, doğum 112/126 gün (kartın orijinal maddeleri) — hukuk onayı bekliyor; roadmap/7578 ile çelişiyor.
2. Çoğul gebelik ayrımı — doğum kaleminin parçası olarak ertelendi.
3. Bedelli askerlik 21 gün, koruyucu aile 10 gün ücretsiz — açık mevzuat soruları, değişmedi. md.74 8 haftalık evlat edinme izni için ayrı
   bir tür yok (yeni kart önerisi).
4. İş arama izninin "yalnızca ihbar süresinde" kısıtı ve hastalık sayacının ihbar süresi bileşeni — Employee'de ihbar süresi verisi/portu yok.
5. Süt izni "<1 yaş" otomatik denetimi; 6 hafta sayacının UI'a bağlanması; yıllık izinde "en çok 3 bölüm".

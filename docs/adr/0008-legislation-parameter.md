# ADR 0008 — `LegislationParameter`: tarih aralıklı mevzuat parametresinin yeri

**Durum:** Önerildi — 2026-09-15 (PC-B önerir; PC-A ile birlikte kabul edilecek, bkz. paralel plan §9)

## Bağlam
Leave (yıllık izin kademeleri, yaş istisnası, babalık/doğum/koruyucu aile süreleri, bedelli askerlik), Fazla mesai (270 saat, serbest zaman 6 ay, FM onayının 30 gün önceden geri alınması), Payroll (Faz 3: asgari ücret, SGK tavanı, saat bölen 225) ve Employee (iş güvencesi 30 işçi/6 ay, toplu çıkarma eşikleri — bugün `EmployeeLegislationOptions`) aynı türden değerleri okuyor. Değerler **yürürlük tarihine bağlı**: 7578 s.K. (RG 01.05.2026/33240) ile babalık izni 5 → 10 gün, doğum sonrası analık 8 → 16 hafta oldu. Referans tarih olayın tarihidir (30.04.2026 doğumda babalık 5, 01.05.2026'da 10 gün), bu yüzden "güncel değer" ayarı yetmez (11.3.5, 11.6).

## Seçenekler
1. **Her modül kendi ayarı** (`IOptions<…>`): tarih aralığı yok, aynı değer birden çok yerde, 7578 gibi değişiklik her modülde ayrı iş. Reddedildi.
2. **`Sense.Shared.Infrastructure` içinde ortak tablo**: ADR 0003'ün "modül başına şema/DbContext" kuralını deler; Shared'ın migration'ı yok. Reddedildi.
3. **Ayrı `Legislation` modülü** (şema `legislation`, 5 proje): doğru uzun vadeli yer, ancak bugün tek tüketicisi Leave olan bir tablo için modül iskeleti + sıcak dosya kayıtları maliyeti; yönetim ekranı/seed sürümleme ihtiyacı Faz 3 (Payroll) ile netleşecek.
4. **Okuma portu `Sense.Shared.Contracts`'ta, veri + seed tek sahip modülde (şimdilik Leave)** — önerilen.

## Karar (önerilen)
- **Port:** `Sense.Shared.Contracts.Legislation.ILegislationParameterReader`
  - `FindAsync(key, asOf, country = "TR")` → `LegislationParameterValue(Key, Country, DecimalValue?, ValueJson?, EffectiveFrom, EffectiveTo?, Source)`; `ListAsync(keys, country)` bellek içi çoklu tarih değerlendirmesi için.
  - Uzantılar: `GetDecimalAsync`, `GetDecimalOrDefaultAsync`, `GetAsync<T>` (JSON), `Resolve(values, key, asOf)`.
  - Anahtar sabitleri `LegislationParameterKeys` (`Leave.Paternity.Days`, `Overtime.ConsentRevocationNoticeDays` …). Tüketici modül yalnız `Sense.Shared.Contracts`'a bağımlıdır; sahip modül değişirse tüketici kodu değişmez.
- **Veri:** tek tablo `leave.legislation_parameters` — `Id, Key, Country (ISO-2), DecimalValue numeric(18,4)?, ValueJson jsonb?, EffectiveFrom date, EffectiveTo date? (dahil), Source (kanun/madde/RG tarih-sayı), CreatedAt…`; benzersiz `(Country, Key, EffectiveFrom)`. **Kiracı verisi değildir** (tenant filtresi yok, sistem geneli). Aralıklar aynı anahtar için çakışmaz (test ile korunur).
- **Sahip (şimdilik):** Leave modülü — ilk ve en yoğun tüketici, 7578 değişikliğinin ilk gerçek örneği; `LegislationParameterReader` Leave.Infrastructure'da, `AddLeaveServices` ile hem API'ye hem Worker'a kaydedilir.
- **Seed:** EF `HasData` ile migration içinde (`InitialLeave`), seed sürümü `2026.05`; değerler yalnız 11.3.5/11.6'da yazanlar, her satırda `Source`:

  | Anahtar | Değer | Geçerlilik | Kaynak |
  |---|---|---|---|
  | `Leave.Annual.Tier1.Days` / `Tier2.AfterYears`,`Tier2.Days` / `Tier3.AfterYears`,`Tier3.Days` | 14 / >5 → 20 / ≥15 → 26 | 2003-06-10 → | 4857 md.53 |
  | `Leave.Annual.YoungAgeMax` / `OldAgeMin` / `AgeExceptionMinDays` | 18 / 50 / 20 | 2003-06-10 → | 4857 md.53 |
  | `Leave.Annual.UndergroundExtraDays` | 4 | 2003-06-10 → | 4857 md.53 |
  | `Leave.Paternity.Days` | 5 | 2016-02-10 → 2026-04-30 | 4857 Ek md.2 (6663 s.K.) |
  | `Leave.Paternity.Days` | 10 | 2026-05-01 → | 7578 s.K. |
  | `Leave.Maternity.PreBirthWeeks` | 8 | 2003-06-10 → | 4857 md.74 |
  | `Leave.Maternity.PostBirthWeeks` | 8 / 16 | → 2026-04-30 / 2026-05-01 → | 4857 md.74; 7578 s.K. |
  | `Leave.Marriage.Days`, `Leave.Adoption.Days`, `Leave.Bereavement.Days` | 3 | 2016-02-10 → | 4857 Ek md.2 |
  | `Leave.DisabledChild.DaysPerYear` | 10 | 2016-02-10 → | 4857 Ek md.2 |
  | `Leave.FosterCare.Days` | 10 (ücretsiz) | 2026-05-01 → | 7578 s.K. |
  | `Leave.PaidMilitaryService.Days` | 21 | 2019-06-26 → | 1111 s.K. geçici md.55 |
  | `Leave.JobSearch.MinHoursPerDay` | 2 | 2003-06-10 → | 4857 md.27 |
  | `Overtime.AnnualLimitHours` / `Overtime.CompTimeExpiryMonths` | 270 / 6 | 2003-06-10 → | 4857 md.41 |
  | `Overtime.ConsentRevocationNoticeDays` | 30 | 2017-08-25 → | Fazla Çalışma Yönetmeliği md.9 (RG 25.08.2017/30165) |

  7578 öncesi satırların `EffectiveFrom` değerleri ilgili kanunun RG tarihidir; bordro düzeyi kullanımdan önce hukuk teyidi gerekir (11.6 "doğrulanmalı" notları geçerli). Doğum sonrası ücretsiz izin süresi (`Leave.PostnatalUnpaid.MaxMonths`) çelişkili kaynak nedeniyle **seed edilmedi**.
- **Tüketim kuralı:** Leave kural seti seed'i (`Örnek Türk İş Kanunu kuralı`) süreleri seed anındaki tarihle okur; bakiye motoru yıllık izin yasal alt sınırını (`StatutoryAnnualLeave`) tahakkuk tarihine göre uygular. Domain katmanı port'u görmez; Application `StatutoryLeaveResolver` ile değer kayıtlarına çevirir.

## Sonuçlar
- 7578 benzeri değişiklik = yeni satır (önceki satırın `EffectiveTo`'su kapanır) + migration/seed sürümü; kod değişmez.
- Leave'e ait olmayan anahtarlar (Overtime/Payroll/Employee) Leave şemasında durur — kabul edilen geçici durum. **Taşıma yolu:** Payroll (Faz 3) başladığında ya da yönetim ekranı (`TenantLegislationOverride`, "yeni seed sürümü var" karşılaştırması) gerektiğinde tablo aynı yapı ve Id'lerle `legislation` şemasına/modülüne taşınır; port ve anahtarlar aynı kalır.
- PC-A: `EmployeeLegislationOptions` eşikleri bu port'a geçirilebilir (anahtarlar henüz seed'de yok; eklenecek anahtarlar `Employee.JobSecurity.*` önekiyle).
- Kiracı bazlı sapma (`TenantLegislationOverride{ParameterId, OverriddenValue, ValidFrom}`, 11.6) bu ADR'ın kapsamı dışında; eklendiğinde reader önce kiracı override'ını, sonra sistem değerini döndürür.

## Açık karar noktaları (PC-A ile)
1. Sahip modül: Leave (öneri) mi, şimdiden küçük `Legislation` modülü mü?
2. Employee eşikleri port'a ne zaman taşınır (E5a iş güvencesi paketi)?

# Analitik ve Raporlama: PO değerlendirmesi ve kararlar (2026-09-18)

Kapsam: `Sense.Modules.Analytics`, `Sense.Modules.Reporting`, `web/src/pages/reports.tsx`, ilgili Contracts portları
(Employee/Leave/Attendance/Organization) ve yol haritası kalemleri #70, #99, #100 (11.7) ile 11.3.12 tasarımı.
Kaynak durum dokümanları: `kolayik-video/faz7-durum-takibi.md` (bayat), `implementation/faz10-analytics-kpi-sozlesmesi.md`,
`implementation/faz10-ilk-surum-yayin-kontrol-listesi.md`.

## 0. Uygulama durumu (2026-09-18, aynı gün içinde)

Bu dokümandaki kararların çoğu **aynı gün paralel bir oturumda** hayata geçirildi (bkz. güncellenmiş
`faz7-durum-takibi.md`) — bu değerlendirmenin planlamasından bağımsız, eşzamanlı ilerleyen bir çalışma. Durum:

- ✅ **K1/W1-W8 (sentetik veri temizliği)** — `reports.tsx` baştan yazıldı, kategori+parametre modelli rapor
  kataloğu ve `workforce-kpi-panel.tsx` (yalnız gerçek veri, dataviz sabit renk kuralı, boş durumlar) geldi.
- ✅ **K7 (Reporting genişlemesi, dar kapsam)** — `IReportProvider`'a kategori/parametre eklendi;
  `employee.list`, `leave.balance.summary`, `leave.requests`, `finance.salaries` provider'ları eklendi
  (`IEmployeeReportReader`, Leave/Employee `Reporting/` altyapısı, `OrgUnitHierarchy`).
  Detay: `docs/implementation/faz4-raporlar-revizyonu.md` (aynı oturum).
- ✅ **R1 (kırık e-posta linki)** — `ReportMailer` artık `/app/reports?execution=` üretiyor (var olan rotaya).
- ✅ **W9 (nav "Analitik" kutusu kendine gidiyordu)** — bu oturumda düzeltildi: `/app/reports`'a yönlendiriyor,
  `phase:7` (yapılmadı) etiketi kaldırıldı, izin kapısı eklendi.
- ✅ **A1 (kariyer oranı formülü)** — bu oturumda düzeltildi: terfi/(terfi+yatay geçiş), 11.3.12 ile uyumlu.
- ✅ **A2 (N+1 maaş sorgusu)** — bu oturumda düzeltildi: `IEmployeeSalaryLookup.GetCurrentSalariesAsync` toplu okuma.
- ✅ **K6/A7 (HRSpecialist analytics.\* eksik)** — bu oturumda eklendi.
- ✅ **W10 (ölü `reports.unit.run` izni)** — bu oturumda gerçek uçların kontrol ettiği izinlere bağlandı.
- ✅ **K5 (küçük hücre gizleme)** — bu oturumda eklendi, ilk yazılan tasarımdan bir düzeltmeyle: 11.3.12'nin kuralı
  yalnız birim kırılımına değil kimlik ifşa riski taşıyan **her** KPI kırılımına uygulanıyor (kariyer, turnover,
  cinsiyet, birim×cinsiyet). `KpiBreakdownDto.Count`/`Share` artık `N>0 ve N<5` olan hücrelerde `null` (`WhenWritingNull`
  ile API'den hiç çıkmıyor), `IsSuppressed=true`; sıfır hücre gizlenmiyor (kimseyi işaret etmiyor). Web tarafı
  (`workforce-kpi-panel.tsx`) gizli hücreleri "Gizli (< 5)" olarak gösteriyor, grafiklerde dilim/çubuk çizmiyor.
  Eşik şu an sabit (`MinCellSize=5`); `TenantSettings.Analytics.MinCellSize` (tenant bazlı yapılandırma) backlog'da kaldı.
- ⏳ **K3 (KpiSnapshot kalıcı katmanı)**, **K2 (sentetik veri lint kapısı)**, **K9 (çapraz analiz önkoşul portları)** —
  henüz açılmadı, bu turun kapsamı dışında kaldı.
- Doğrulama notu: `web/` `tsc --noEmit` + `eslint` + `vite build` temiz; `Sense.Modules.Analytics.Tests` (23/23,
  küçük hücre gizleme testi dahil), `Sense.Modules.Identity.Tests` (UnitManagerGrants/SystemRoleDefinitions
  filtresiyle 15/15) ve `Sense.Tests.Architecture` (52/52, `Modules_TalkOnlyThroughContracts` dahil) yeşil;
  `Sense.Api` tüm modüllerle derleniyor. İlk koşuda `Announcements` modülünde başka bir oturumun süregelen (bu
  incelemeyle ilgisiz) derleme hatası tam çözüm testini bloke etmişti; o oturum düzeltince tekrar koşturulup doğrulandı.

## 1. Tek cümlelik hüküm

Backend doğru yerde, doğru kurallarla ve testli; web panosu ise ürünün güvenilirliğini zedeleyen sentetik verilerle
dolu. Analitik sayfası bugünkü hâliyle **yayınlanamaz**; sentetik veri temizliği P0'dır, geri kalan her şey onun
arkasında sıralanır.

## 2. Neden bu kadar ciddi

- Analitik bir İK ürününde "güven" ürünüdür. Turnover, cinsiyet oranı, fazla mesai maliyeti yönetim kararlarına girer.
  Uydurma trend eğrisi, uydurma ayrılma nedenleri, uydurma kuşak dağılımı gösteren bir pano; hatalı bir pano değil,
  yanıltıcı bir panodur.
- Sentetik verilerin bir kısmı gerçek sayıdan türetiliyor (ör. gerçek mesai maliyeti × sabit çarpanlarla 6 aylık
  trend). Bu, "demo verisi" görünümünü bile kaldırır; kullanıcı gerçek zanneder.
- Panoyu görebilen roller Owner, Admin ve Auditor. Auditor personasına uydurma metrik göstermek denetim riski.
- Kök neden süreçsel: FE, BE'den önce Kolay İK benzeri tam bir yerleşimi kurdu ve boşlukları örnek değerle doldurdu;
  "üretim kodunda sabit veri seti olmaz" kapısı yoktu. Bu yüzden düzeltme yalnız kodu değil, kapıyı da içerir.

## 3. Doğrulanmış bulgular

### 3.1 Web (`reports.tsx`, 1656 satır)

| # | Bulgu | Kanıt | Sınıf |
|---|---|---|---|
| W1 | Her zaman sentetik: aylık turnover trendi (sabit çarpan dizisi), işe giriş/çıkış serisi, ayrılma nedenleri, kuşak dağılımı, ofis/uzaktan dağılımı, departman bazlı izin kullanım oranı, fazla mesai trendi | satır ~617-660, ~795-850, ~1005-1075, ~1205-1260 | P0 |
| W2 | API boşken sentetik fallback: headcount 48, turnover %8,4, ilk 6 ay %18, kadın %42, izin kullanım "%42.5", mesai 46.200 TL / 15 kişi, kariyer oranı %14,5, izin türü ve kariyer kırılımı sabit listeleri | satır ~250-262, ~660-672, ~1205-1218, ~1350-1365 | P0 |
| W3 | "Dijital analist içgörüleri" bloğu statik metin; yenile butonu yalnız toast gösteriyor | `DigitalAnalystInsights` | P0 (W1 ile birlikte kaldır) |
| W4 | Tarih filtresi yalnız workforce KPI'sına gidiyor; izin KPI'sı her zaman cari yıl, mesai KPI'sı her zaman cari ay | `WorkforceKpis` sorguları | P1 |
| W5 | Kadın oranı, kırılım adında "female"/"kadın" aranarak bulunuyor; BE `Gender.ToString()` gönderiyor ("Female") | `femaleRatio` useMemo | P1 |
| W6 | i18n yok: ay adları, rozetler, grafik etiketleri, "Cari Yıl Analitiği" Türkçe sabit; `reports` namespace mevcut ama kullanılmıyor | dosya geneli | P1 |
| W7 | `WeeklyOvertimeCostCard` hiçbir yerde kullanılmıyor, Türkçe sabit içeriyor | `components/reports/weekly-overtime-cost-card.tsx` | P2 |
| W8 | Sayfa için birim testi yok (servis testleri var) | `web/src/pages/` | P1 |
| W9 | Uygulamalar başlatıcısındaki "Analitik" kutusu `/app/apps`'a (kendine) gidiyor | `navigation.ts:302` | P2 |
| W10 | Manager `*.my.*` ile Raporlar menüsünü görüyor ama kataloğa/KPI'ya izni yok: boş sayfa | `navigation.ts:151`, `SystemRoleDefinitions` | P1 |

### 3.2 Reporting backend

| # | Bulgu | Kanıt | Sınıf |
|---|---|---|---|
| R1 | E-posta linki `/app/reports/executions/{id}`; web'de böyle rota yok (`App.tsx` yalnız `reports`) | `ReportMailer.cs` | P0 |
| R2 | Senkron ve zaten indirilen her rapor için "raporunuz hazır" e-postası gidiyor | `RequestReportHandler` | P1 |
| R3 | Katalogda tek provider: `documents.missingDocuments` (CSV). Kategori, izin bakiye raporları, Kendin oluştur yok | `IReportProvider` implementasyonları | P1 (ürün boşluğu) |
| R4 | Sonuç dosyası Postgres `bytea`; saklama süresi ve temizlik yok | `ReportExecution.ResultContent` | P2 |
| R5 | Reporting `Sense.Worker`'a kayıtlı değil; asenkron/zamanlanmış üretim yok (ADR-0026 ile ilk sürüm dışı, B-10-05) | yayın kontrol listesi md.3 | Backlog (bilinçli) |
| R6 | Katalog DTO yalnız `Key` taşıyor; kategori/parametre/format şeması yok (11.3.12'deki `ReportDescriptor` yok) | `ReportCatalogItemDto` | P2 |

### 3.3 Analytics backend

| # | Bulgu | Kanıt | Sınıf |
|---|---|---|---|
| A1 | `CAREER_PATH_RATIO` tanım uyuşmazlığı: 11.3.12 "Promotion / (Promotion + LateralMove)" der; kod "4 tür değişiklik toplamı / aktif headcount" hesaplıyor | `WorkforceKpiFeatures.BuildCareerPathRatio` | P1 |
| A2 | Fazla mesai KPI'sında çalışan başına ayrı maaş sorgusu (N+1); birim dalında tüm tenant aktif listesi iki kez çekiliyor | `GetOvertimeCostKpiHandler` | P1 |
| A3 | Turnover paydası dönem ortalaması değil bugünkü aktif headcount; "ilk 6 ay" kohort değil kıdem payı. Dokümante ama kullanıcıya görünmüyor | `faz7-durum-takibi.md`, kod yorumları | P1 (etiketle), P2 (düzelt) |
| A4 | `Weekly`/`Yearly` ödeme düzeni hesaptan dışlanıyor; oysa 45 saat/hafta ve 12 ay dönüşümü tanımlanabilir | `OvertimeCostCalculator.TryGetHourlyRate` | P2 |
| A5 | Kalıcı veri yok: trend, yıl-yıl karşılaştırma, ortalama headcount, geriye dönük hesap için `KpiSnapshot` gerekiyor | `AnalyticsModule` yorumu | P1 (mimari karar) |
| A6 | Küçük hücre gizleme (`MinCellSize`) yok; birim × cinsiyet kırılımı 1-2 kişilik birimlerde kişiyi işaret ediyor | `BuildGenderRatioByUnit` | P2 |
| A7 | HRSpecialist rolünde `analytics.*` yok; İK uzmanı KPI panosunu göremiyor. Admin/Owner/Auditor görüyor | `SystemRoleDefinitions.cs` | P1 |
| A8 | `Absences` entity'si Leave'de var (S-21 tek kaynak) ama devamsızlık KPI'sı yok | `Leave.Domain/Absences` | P2 (fırsat) |

### 3.4 Yol haritası ve izleme

- `faz7-durum-takibi.md` "Web tarafı yok" diyor; S-15/S-17'den habersiz. Son güncelleme 2026-09-16.
- Faz 10 için durum dokümanı yok; sözleşme ve yayın kontrol listesi var, backlog kayıtları (B-10-03, B-10-05) yalnız bu
  iki dosyada geçiyor.
- `paralel-gelistirme-plani.md` satır 96: Analytics sahipsiz (❌). Reporting hiç geçmiyor.
- Linear "SenseİK" projesinde analitik/rapor/KPI ile ilgili hiç kayıt yok (proje boş görünüyor).
- #100 çapraz analizler için "modül yok" gerekçesi artık geçersiz: Performance, Recruitment, Training modülleri var.
  Eksik olan Contracts portları: `Candidate.Source`'un işe alımda Employee'ye taşınması ve Performance'tan dönem
  sonucu okuma portu.

## 4. Kararlar

K1. **Sentetik veri sıfır toleransı.** `reports.tsx`'ten tüm sabit veri setleri ve fallback sayıları çıkar. API'de
    karşılığı olmayan grafikler (trend, işe giriş/çıkış, ayrılma nedenleri, kuşak, çalışma modeli, departman izin
    oranı, mesai trendi) ve içgörü bloğu **kaldırılır**, "yakında" kartı bile konmaz. Boş veri = boş durum bileşeni.
    Gerekçe: bunların dürüst versiyonu snapshot altyapısını (K3) bekler; o gelmeden yer tutucu göstermek yine yanıltır.

K2. **Kapı.** ESLint kuralı ya da CI grep: `web/src/pages/**` ve `web/src/components/**` içinde 3+ elemanlı sabit
    nesne dizisi yalnız `*.test.tsx`/`*.stories.tsx`/`fixtures/` altında olabilir. Sayfa için "API boşken boş durum"
    testi zorunlu. Bu kapı 10 faz FE hattının tamamına uygulanır, yalnız raporlara değil.

K3. **Analytics'e kalıcı katman.** `Domain` + `Infrastructure` + `analytics` şeması; `KpiSnapshot{TenantId, KpiKey,
    PeriodStart, PeriodEnd, OrgUnitId?, Dimension?, Value, Numerator, Denominator, SampleSize}`; Worker'da aylık job,
    mevcut 7 KPI'yı tenant ve birim bazında yazar; ilk kurulumda son 24 ayı `HireDate`/`TerminationDate`/
    `CareerRecord.ValidFrom`'dan geriye doğru doldurur. Bu tek yatırım şunları açar: dürüst trend grafikleri, yıl-yıl
    karşılaştırma, turnover için gerçek ortalama headcount (A3), kohort bazlı ilk 6 ay, hedef eşiği (`KpiTarget`).

K4. **KPI tanımları.** `CAREER_PATH_RATIO` 11.3.12 tanımına çekilir (A1); turnover ve ilk 6 ay etiketleri K3
    gelene kadar FE'de formül açıklamasıyla gösterilir (`reports:kpi.<key>.method`). Fazla mesaide Weekly (÷45) ve
    Yearly (÷12÷225) dönüşümü eklenir (A4). Devamsızlık KPI'sı `ABSENCE_RATE` Leave.Absence üzerinden eklenir (A8).

K5. **Küçük hücre kuralı.** Company kapsamı dışındaki çağıranlar için birim × cinsiyet kırılımında N < 5 hücre
    gizlenir (`KpiBreakdownDto.IsSuppressed`). Performans/maaş içeren herhangi bir kırılım bu kural olmadan yayına
    çıkmaz.

K6. **Roller.** HRSpecialist'e `analytics.*`; Manager'a `reports.catalog.view` (provider bazlı izin zaten var) ve
    `analytics.kpi.view` @Team (handler kapsamlı görünümü destekliyor). Raporlar menüsü yalnız `*.my.*` ile görünmez.

K7. **Reporting R1.** (a) E-posta linki `/app/reports`'a çevrilir ve senkron akışta e-posta kaldırılır; e-posta
    yalnız asenkron yolda kalır. (b) `IReportProvider`'a `Category` eklenir (documents/leave/employee/…); FE kategori
    kartlarını buradan üretir. (c) İlk iki gerçek provider: `leave.balance.summary` (Leave, 11 kolon, senkron Excel,
    mevcut `IReportExporter`) ve `employee.list` (Employee). (d) Reporting Worker'a kaydedilir; 30 gün sonuç temizliği
    job'ı eklenir. Kendin oluştur ve asenkron `leave.balance.detail` Faz 4 planında kalır, bu turda açılmaz.

K8. **Sahiplik.** Analytics ve Reporting çekirdeği PC-A'ya (Employee/Organization/web sahibi). Provider'lar sahibi
    modülün makinesine: `leave.*` PC-B, `employee.*`/`documents.*` PC-A. `paralel-gelistirme-plani.md` satır 96 güncellenir.

K9. **#100 önkoşulları ayrı iş olarak açılır**, bu turda yapılmaz: Recruitment işe alım olayına `Source` eklenir ve
    Employee `HireSource` snapshot'ı tutar; Performance.Contracts'a `IPerformanceRatingReader` eklenir. Bunlar
    kapanınca çapraz analiz Faz 7'de blokajsız kalır.

K10. **İzleme.** `faz7-durum-takibi.md` güncellenir (web var, S-15/S-17, A1 sapması, K3 kararı); Faz 10 için
     `faz10-durum-takibi.md` açılır ve B-10-03/B-10-05 oraya taşınır. Linear'da aşağıdaki iş listesi açılır.

## 5. İş listesi ve sıra

| Öncelik | İş | Sahip | Boyut | Bağımlılık |
|---|---|---|---|---|
| P0 | W1-W3: sentetik veri ve içgörü bloğu kaldırma, boş durumlar | PC-A FE | 2 g | yok |
| P0 | R1: e-posta linki + senkron e-posta kaldırma | PC-A BE | 0,5 g | yok |
| P0 | K2 kapısı (lint/CI + boş durum testi) | PC-A FE | 1 g | yok |
| P1 | A1 kariyer oranı tanımı + testi; A3 formül etiketleri | PC-A BE/FE | 1 g | yok |
| P1 | A2 toplu maaş portu `IEmployeeSalaryLookup.GetCurrentSalariesAsync` | PC-A BE | 1 g | yok |
| P1 | W4-W6, W10: filtre yayılımı, cinsiyet anahtarı, i18n, menü koşulu | PC-A FE | 2 g | yok |
| P1 | K6 rol değişiklikleri (`SystemRoleDefinitions` + senkronizer) | PC-A BE | 0,5 g | yok |
| P1 | Sayfa bölme: sekme başına bileşen, `reports.tsx` < 300 satır | PC-A FE | 1 g | W1 |
| P1 | K3 snapshot katmanı + aylık job + 24 ay backfill | PC-A BE | 6-8 g | yok |
| P1 | K7b-c: `Category` + `leave.balance.summary` + `employee.list` | PC-B / PC-A | 3 g | yok |
| P2 | K7d Worker kaydı + 30 gün temizlik | PC-A BE | 1 g | yok |
| P2 | K5 küçük hücre gizleme | PC-A BE/FE | 1 g | yok |
| P2 | A4 Weekly/Yearly dönüşümü; A8 `ABSENCE_RATE` | PC-A BE | 1,5 g | S-21 |
| P2 | Trend/yıl-yıl grafikleri (gerçek veriyle) | PC-A FE | 2 g | K3 |
| P2 | W7, W9 temizlik | PC-A FE | 0,5 g | yok |
| Backlog | K9 önkoşul portları (HireSource, IPerformanceRatingReader) | PC-B / Performance sahibi | 2 g | yok |
| Backlog | Kendin oluştur, `leave.balance.detail` asenkron, zamanlanmış gönderim (B-10-05) | plan Faz 4 | L | K7d |

İki sprintlik okuma: Sprint 1 P0 + P1'in snapshot dışı kalemleri (≈9 gün), Sprint 2 K3 snapshot + ilk provider'lar +
P2 (≈14 gün). Sprint 1 sonunda sayfa yayınlanabilir hâle gelir; Sprint 2 sonunda "analitik" iddiası gerçek olur.

## 6. Bilinçli olarak yapılmayacaklar

- Sentetik grafiklerin yerine "yakında" yer tutucuları: yanıltmanın hafif hâli, konmaz.
- KPI sonuçlarını önbelleklemek: K3 gelince gereksiz; N+1 dışında performans sorunu bugün ölçülmedi.
- `ReportDescriptor` tam şeması (parametre, format, grain): Kendin oluştur ile birlikte, Faz 4.
- Cinsiyet oranı hedef eşiği (`KpiTarget`): snapshot olmadan anlamsız; K3 sonrası.

## 7. Açık doğrulamalar

- ~~`UnitManagerGrants` anahtarı~~ Doğrulandı: "kendi birim raporları" anahtarı `reports.unit.run` veriyor; bu anahtarı
  ne Reporting ne Analytics tüketiyor. Birim yetkilendirme diyaloğundaki anahtar bugün **ölü**. K6 kapsamında
  `reports.unit.run` → `reports.catalog.view` + `reports.executions.generate` + `analytics.kpi.view` (birim kapsamı)
  olarak çözülmeli ya da handler'lar bu anahtarı da kabul etmeli. (P1, PC-A BE, 0,5 g; iş listesine eklendi.)
- Linear "SenseİK" projesi gerçekten boş mu, yoksa erişim/isim farkı mı var.
- Analytics ve Reporting testleri bu branch'te yeşil mi (son commit mesajı "BE tamamen yeşil" diyor, koşturulmadı).

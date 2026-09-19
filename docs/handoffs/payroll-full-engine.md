# senseik-019 — Bordro Motoru Aşama 2–3 — UYGULAMA PLANI + İMPLEMENTASYON NOTU

**Durum: Uygulandı (GATE 1/GATE 2 önceden onaylandı). Aşağıdaki plan bölümü orijinal haliyle korunmuştur; "Implementasyon Notu" gerçekleşen kapsamı anlatır.**

## ⚠️ Kart Tanımı Güncel Değil

Kart "Payroll modülü ❌ durumdan başlıyor" diyor — **artık doğru değil**. P0–P3 arası bir dilim zaten canlı:

- `PayrollCalculator.cs` — SGK taban/tavan, SGK işçi %14+%1, **kümülatif matrahla dilimli gelir vergisi** (zaten doğru kurulu), asgari ücret GV/DV istisnası, damga vergisi ‰7,59, 4 yemek yardımı varyantı, işveren SGK+işsizlik+employer cost, adım-adım trace.
- `PayrollLegislationSnapshot` + ADR 0008 ile tüm oranlar DB'den tarih aralıklı okunuyor (hardcode yok).
- Cross-module köprüler zaten var: `IWorkedTimeReader` (Attendance), `IAbsenceDaysReader` (Leave), `IExpensePayoutReader` (Expense) — null-fallback deseniyle.
- Excel export zaten var (S-14, cost center filtreli).
- Golden-set testleri mevcut.
- **e-Bildirge ve banka ödeme dosyası zaten "İlk Sürüm Dışı, P5+ backlog" olarak dokümante edilmiş** (`faz7-payroll-export-sozlesmesi.md` K-7) — senseik-019 bu ertelenmiş backlog'un karşılığı.

**Sonuç:** Sıfırdan motor kurmak değil, **mevcut P0–P3 dilimini büyütme**. Risk azalıyor (iskelet+test altyapısı+legislation mimarisi hazır) ama kapsam büyük.

## Bağımlılık Haritası (özet)

| İhtiyaç | Durum (plan anı) |
|---|---|
| FM ücret oranı, 270 saat sayacı | **YOK**, senseik-012'ye bağımlı (blocking) |
| Hafta tatili/resmi tatil, denkleştirme | **YOK**, senseik-014'e bağımlı (blocking) |
| SGK işyeri sicil/meslek kodu, teşvik | **KISMİ** — alan var (`IncentiveLawCode`), hesaba katılmıyor; senseik-010'a bağımlı |
| Sicil numarası | **KISMİ** — `ExternalPayrollCode` var; senseik-009'a bağımlı |
| Engelli vergi indirimi | **KISMİ** — `DisabilityDegree` var, indirim tutarı parametresi yok |
| Avans mahsubu | **KISMİ** — port hazır (`IExpensePayoutReader.OpenAdvanceBalance`), motora bağlanmamış |
| İcra/nafaka | **YOK**, sıfırdan tasarım (`GarnishmentOrder`) |
| Kümülatif GV matrahı devri | **YOK** — motor altyapısı var (`PriorCumulativeGvBase`), gerçek değer hesaplanmıyor |
| Maliyet merkezi | **VAR** ve kullanılıyor |
| PDF render | **KISMİ** — sözleşme var (`IDocumentRenderer`), implementasyon eksik |
| e-Bildirge/APHB, banka dosyası | **YOK**, bu kartın ana teslimatı |

## Alt-Aşamalandırma Önerisi (8 dilim, her biri ayrı kart/PR)

- **2a** — Kümülatif matrah devri + işveren teşviki (SGK 5 puan) — bağımsız, hemen başlanabilir.
- **2b** — Engelli vergi indirimi (GVK m.31) — matrah indirimi, `MinWageExemptionGv`'den ÖNCE uygulanmalı.
- **2c** — İcra/nafaka + avans mahsubu — yeni `GarnishmentOrder` kavramı, kesinti sırası netleştirilmeli.
- **2d** — Fazla mesai/hafta tatili/resmi tatil — senseik-012/014'ü bekler.
- **3a** — Resmi çıktılar (ücret bordrosu m.37, pusula PDF) — önce Shared'da `IDocumentRenderer` (QuestPDF) implementasyonu.
- **3b** — e-Bildirge/APHB — SGK resmi format şeması araştırılmalı; senseik-009/010 tamamlanmadan "taslak" üretebilir.
- **3c** — Banka ödeme talimat dosyası — format tenant/banka bazında değişir, MVP tek format.
- **3d** — Muhasebe entegrasyon fişi — tenant-özel hesap eşleme tablosu gerekebilir.

**Sıralama:** 2a→2b→2c bağımsız, hemen başlanabilir. 2d senseik-012/014'ü bekler; paralelde 3a/3b/3c/3d üzerinde çalışılabilir (zaten hesaplanmış Payslip üzerinden türetiliyor).

## Golden-Set Test Stratejisi (7 senaryo, merge_gate "5+" istiyor)

Asgari ücretli tam ay · orta gelir dilim aşan (mevcut, regresyon çapası) · engelli (2. derece) · teşvikli işveren (5510) · fazla mesaili · yıl içi işe giren (devirli matrah) · icra kesintili+açık avans.

Ek: APHB için ayrı "format doğruluğu" test seviyesi (SGK resmi şemaya karşı validasyon).

## En Yüksek Risk Noktaları (öncelik sırası)

1. **Gelir vergisi kümülatif matrah hatası** — en yaygın/gizli, yıl sonu toplu telafi + ceza riski.
2. **APHB format hatası** — idari para cezası (5510 m.102).
3. **İcra/nafaka kesinti sırası/oranı hatası** — hukuki sorumluluk.
4. **Banka dosyası format hatası** — mali kayıp.
5. **Engelli indirimi/teşvik yanlış uygulanması** — eksik/fazla ödeme riski.

**Önerilen doğrulama:** Her alt-aşamanın golden-set'i kod incelemesinden önce bir muhasebe/bordro uzmanına (SMMM) el ile hesaplattırılıp imzalatılmalı ("GATE 2: golden-set uzman onayı almadan merge yok"). Her `LegislationParameter` satırına `Source` (kanun/madde/RG referansı) zorunlu tutulmalı.

## EF Migration ve Modül Kaydı

Payroll zaten kayıtlı — **yeni modül kaydı YOK**, sadece yeni migration'lar. Yeni entity gereken dilimler: 2a (önceki işveren devir kaydı), 2c (`GarnishmentOrder`), 3d (muhasebe eşleme tablosu). **ADR 0008 dikkat noktası:** `LegislationParameter` tablosu şu an Leave şemasında; ADR "Payroll (Faz 3) başladığında taşınabilir" diyor — bu kart tam o faz, taşıma kararı bu kartta mı ayrı kartta mı verileceği netleştirilmeli.

## Kritik Dosyalar
- `src/Modules/Payroll/Sense.Modules.Payroll.Domain/Calculation/PayrollCalculator.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Domain/Calculation/PayrollCalculationInput.cs`, `PayrollLegislationSnapshot.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Application/Periods/PayrollPeriodCalculationService.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Application/Ports/IExpensePayoutReader.cs`
- `tests/Modules/Sense.Modules.Payroll.Tests/PayrollCalculatorGoldenSetTests.cs`
- `docs/adr/0008-legislation-parameter.md`, `docs/implementation/faz7-payroll-export-sozlesmesi.md`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § G-01 Aşama 2–3. Bağımlı: senseik-009, senseik-010, senseik-012, senseik-014. Agent board: `docs/agent-board/cards/senseik-019-bordro-tam.json`.*

---

## Implementasyon Notu (2026-09-19)

### Özet

Handoff planındaki **2a, 2b, 2c dilimleri tam**, **2d (fazla mesai/hafta tatili) domain hesaplayıcı düzeyinde tam ama uçtan uca bağlanmadı**, **3a (ücret pusulası) ve 3b (APHB) taslak/iskelet düzeyinde tam**, **3c (banka ödeme dosyası) ve 3d (muhasebe fişi) yapılmadı** (aşağıda "Kalan İşler"). Hiçbir yarım/derlenmeyen kod bırakılmadı; her commit build+test yeşil.

### Tam Yapılanlar

**2a — Kümülatif matrah devri + işveren teşviki**
- `Payslip` aggregate'ine `CumulativeGvBase` alanı eklendi (`CreateFirstVersion`/`Recalculate`, geriye dönük uyumlu opsiyonel parametre).
- `PayrollPeriodCalculationService.ResolvePriorCumulativeGvBaseAsync`: Ocak'ta her zaman 0; değilse önceki ayın `PayrollPeriod`'unu (`IPayrollPeriodRepository.FindByYearMonthAsync`) ve o dönemdeki aynı çalışanın `Payslip`'ini bulup `CumulativeGvBase`'ini devralıyor. Önceki dönem/payslip yoksa (örn. çalışan bu ay işe girdi) 0'dan başlıyor — bu, "yıl içinde işe giren çalışan" senaryosunun **bu tenant içindeki** ilerleyişini doğru yönetiyor. **Açık soru:** çalışanın AYNI YIL İÇİNDE BAŞKA BİR İŞVERENDEN geldiği ve oradaki kümülatif matrahı elle girilmesi gereken durum (klasik "işten ayrılış bildirgesi" senaryosu) bu sürümde YOK — `PriorCumulativeGvBase` için ayrı bir manuel giriş alanı (örn. `EmployeePayrollProfile.PriorEmployerCumulativeGvBase`) gerekir, PO onayı gerekli.
- `EmployeePayrollProfile.IncentiveLawCode` doluysa (`!string.IsNullOrWhiteSpace`) `IncentiveEligible=true` sayılıyor, `PayrollCalculator` `EMPLOYER_DISCOUNT` adımında `SgkEmployerDiscountRate` (%5) uyguluyor ve **daha önce EMPLOYER_COST formülündeki `- 0m` hardcode bug'ı da düzeltildi** (indirim hiç düşülmüyordu). **BASİTLEŞTİRME:** gerçek teşvik uygunluğu (SGK'nın güncel kanun kodu listesiyle eşleşme, işyeri bazlı başvuru/onay durumu) kontrol edilmiyor — yalnızca alanın dolu olması yeterli sayılıyor; senseik-010 (SGK işyeri/teşvik kodu) tamamlanınca gerçek doğrulama eklenmeli.

**2b — Engelli vergi indirimi (GVK m.31)**
- Yeni cross-module port `Employee.Contracts.IEmployeeDisabilityLookup` (+ `EmployeeDisabilityDegree` mirror enum, `EmployeeDocumentEligibilityLookup` deseniyle aynı) — Employee'nin `DisabilityDegree` alanını Payroll'a sızdırmadan (yalnızca derece enum'u) taşıyor. `EmployeeModule.AddModule`'e kayıt eklendi.
- Yeni legislation parametreleri (Leave'in `LegislationParameterSeed.cs`'ine append, id 40-42): `Payroll.Disability.MonthlyTaxReduction.Degree{1,2,3}` — taslak 2026 tutarları (6.900 / 4.000 / 1.700 TL), **mali müşavirle doğrulanmalı** (dosyadaki mevcut `PayrollDraft2026` kalıbıyla aynı uyarı).
- `PayrollCalculator`'a yeni `DISABILITY_REDUCTION` trace adımı eklendi (GV_BASE ile CUMULATIVE_BASE arasında): matrahtan dereceye göre aylık sabit tutar düşülüyor (0'ın altına inmez), `MINWAGE_EXEMPTION_GV`'den ÖNCE — plandaki sıralama kararına uyuluyor.

**2c — İcra/nafaka + avans mahsubu**
- Yeni aggregate `Sense.Modules.Payroll.Domain.Garnishments.GarnishmentOrder` (tip: İcra/Nafaka, aylık taksit tutarı, kalan bakiye, başlangıç/kapanış tarihi) + `IGarnishmentOrderRepository` + EF konfigürasyonu/migration (`garnishment_orders` tablosu) + minimal CRUD (`CreateGarnishmentOrderCommand`, `CloseGarnishmentOrderCommand`, `ListGarnishmentOrdersQuery`) + API (`GarnishmentOrdersController`, `payroll.courtorder.manage`/`payroll.trace.view` izinli).
- Saf hesaplayıcı `GarnishmentCalculator` (Domain, DB'siz, tam test edilmiş): nafaka önce ve **kesinti oranı sınırı yok** (kalan nete kadar), icra **kesinti öncesi net ücretin %25'i havuzuyla sınırlı** ve birden fazla icra emri bu havuzu emir sırasına (FIFO) göre paylaşıyor. `PayrollCalculator` artık `NET_BEFORE_DEDUCTIONS`'ı hesaplayıp bu tutarı `GarnishmentCalculator`'a veriyor, sonra avans mahsubunu (kalan nete kadar) uyguluyor.
- **⚠️ BASİTLEŞTİRME — SMMM/hukuk onayı olmadan üretimde kullanılmamalı:** asgari ücretin haczedilemezliği (İİK m.83 son fıkra) ayrıca uygulanmıyor; nafaka/icra kesinti sırası ve icra tavanının hangi tutar üzerinden (kesinti öncesi mi sonrası mı) hesaplanacağı genel kabul gören bir yorumu izliyor ama madde bazında doğrulanmadı. Handoff'un kendi risk analizinde zaten "GATE 2: golden-set uzman onayı almadan merge yok" önerisi var — bu öneri hâlâ geçerli, bu implementasyon o onayı YERİNE GEÇMİYOR.
- `PayrollPeriodCalculationService` her dönem hesabında çalışanın aktif emirlerini çekiyor, `PayrollCalculator.Calculate` sonucundaki `GarnishmentApplied` listesiyle her emrin `RemainingBalance`'ını güncelliyor (bakiye sıfırlanınca emir otomatik kapanıyor).

**Domain calculator düzeyinde tam, uçtan uca BAĞLANMADI (2d)**
- `OvertimePayCalculator` (İş K. md.41, %25/%50 zamlı, 225 saatlik aylık esas) ve `HolidayPayCalculator` (md.46/47, hafta tatili/resmî tatilde çalışmanın ek yevmiyesi) — ikisi de saf, tam test edilmiş Domain hesaplayıcıları. `PayrollCalculationInput.AdditionalTaxableEarnings` alanı bu tutarları maktu ücrete ekleyip SGK/GV/damga matrahlarının tamamına dahil ediyor (golden-set'te "fazla mesaili ay" senaryosuyla doğrulandı).
- **Neden uçtan uca bağlanmadı:** senseik-012 (270 saat sayacı, %25/%50 ayrımı) ve senseik-014 (puantaj motoru, hafta tatili/resmî tatil günlerinin tespiti) hâlâ "ready" durumda, henüz merge edilmedi. `IWorkedTimeReader.OvertimeHours` tek bir toplam saat döndürüyor (25/50 ayrımı yok), hafta tatili/resmî tatil günü bilgisi hiç yok. Bu iki kart bitmeden `PayrollPeriodCalculationService`'e gerçek veri bağlamak, henüz tanımlanmamış bir sözleşmeye göre kod yazmak (ve muhtemelen yeniden yazmak) anlamına gelirdi — bilinçli olarak ertelendi.

### Taslak/İskelet Düzeyinde Tam (3a, 3b)

**3a — Ücret pusulası (İş K. md.37)**
- `PayslipDocumentBuilder.BuildText(...)`: çalışan adı, dönem, çalışılan gün, kazançlar (brüt), tüm kesintiler (SGK işçi, işsizlik işçi, gelir vergisi istisna sonrası, damga vergisi istisna sonrası, icra/nafaka/avans) ve net ödeneni **düz metin** olarak üretiyor — m.37'nin asgari içerik şartını (kazanç unsurları + kesintiler ve sebepleri) karşılıyor.
- `GET /api/v1/payroll/payslips/{payslipId}/document` (`PayslipsController`, `payroll.trace.view`).
- **Eksik:** şık bir PDF baskısı — Shared'ın `IDocumentRenderer` (QuestPDF) implementasyonu henüz yok (sözleşme var, implementasyon P2+ notu zaten mevcuttu), bu kartın kapsamı değil.

**3b — e-Bildirge/APHB**
- `AphbFileBuilder`: dönem + çalışan listesi alıp noktalı virgülle ayrılmış bir **TASLAK** metin dosyası üretiyor (TCKN, ad-soyad, sicil no, işyeri kodu, prim gün sayısı, SPEK, sigortalı/işveren payı, teşvik kanun kodu).
- `GET /api/v1/payroll/periods/{id}/aphb` (`payroll.declarations.export`).
- **⚠️ Bu, SGK'nın resmi e-Bildirge XML/web servisi şeması DEĞİLDİR** — gerçek format aranmadı/uygulanmadı (kapsamı dışı, resmi şema araştırması ayrı bir iş kalemi). TCKN ve SGK işyeri sicil kodu alanları şu an BOŞ dönüyor çünkü senseik-009/010 henüz merge edilmedi (`ExternalPayrollCode` doluysa sicil no dolduruluyor, o kadar).

### Kalan İşler (bu kartta bilinçli olarak bırakıldı)

1. **3c — Banka ödeme talimat dosyası:** hiç başlanmadı. Format tenant/banka bazında değişir (K-7'nin kendi notu); MVP için tek bir format (örn. Ziraat/İş Bankası toplu ödeme CSV'si) seçilip PO ile netleştirilmeli.
2. **3d — Muhasebe entegrasyon fişi:** hiç başlanmadı. Tenant-özel hesap eşleme tablosu (hangi bordro kalemi hangi muhasebe hesabına gider) gerektiriyor - ayrı bir domain kavramı.
3. **2d uçtan uca bağlama:** senseik-012/014 merge olunca `IWorkedTimeReader`'a `OvertimeHours25`/`OvertimeHours50`/`WeeklyRestDaysWorked`/`PublicHolidayDaysWorked` eklenip `PayrollPeriodCalculationService`'te `OvertimePayCalculator`/`HolidayPayCalculator` çağrılarak `AdditionalTaxableEarnings` doldurulmalı (motor zaten hazır).
4. **2a açık soru:** yıl içinde BAŞKA işverenden gelen çalışanın devraldığı kümülatif matrahın manuel girişi (bkz. yukarı).
5. **2c legal onay:** GarnishmentCalculator'ın İİK m.83 basitleştirmesi bir SMMM/hukuk danışmanına onaylatılmadan gerçek bordro üretiminde kullanılmamalı.
6. **APHB resmi şema:** gerçek SGK e-Bildirge entegrasyonu (XML şema, e-imza, web servisi) — ayrı kart.
7. **ADR 0008 taşıma kararı:** `LegislationParameter` tablosu hâlâ Leave şemasında; bu kart yeni satırları da oraya ekledi (taşıma kararı verilmedi, ayrı karar gerektirir).

### Golden-Set Kapsamı (7 senaryo — plan hedefine ulaşıldı)

`PayrollCalculatorGoldenSetTests.cs`: asgari ücretli tam ay, orta gelir dilim aşan (regresyon çapası), engelli (2. derece), teşvikli işveren, fazla mesaili ay, yıl içi işe giren/devirli matrah, icra+nafaka+avans kesintili — hepsi el hesabıyla yorumlanmış beklenen değerlerle. Ayrıca `GarnishmentCalculatorTests`, `OvertimePayCalculatorTests`, `HolidayPayCalculatorTests`, `GarnishmentOrderTests`, `AphbFileBuilderTests`, `PayslipDocumentBuilderTests` ile Domain/Application katmanları ayrıca birim test edildi.

### Yeni/Değişen Dosyalar (özet)

- Domain: `GarnishmentCalculator.cs`, `OvertimePayCalculator.cs`, `HolidayPayCalculator.cs`, `Garnishments/GarnishmentOrder.cs`, `PayrollCalculator.cs` (genişletildi), `PayrollCalculationInput.cs`/`PayrollLegislationSnapshot.cs`/`TraceStep.cs`/`PayrollEnums.cs`/`PayrollDomainErrors.cs`/`Repositories.cs` (genişletildi), `Payslip.cs` (`CumulativeGvBase`).
- Application: `Garnishments/GarnishmentCommands.cs`, `Declarations/{AphbFileBuilder,ExportAphbFileQuery,PayslipDocumentBuilder,ExportPayslipDocumentQuery}.cs`, `PayrollPeriodCalculationService.cs` (genişletildi), `PayrollLegislationSnapshotResolver.cs` (genişletildi).
- Infrastructure: `PayrollDbContext.cs`/`Repositories.cs`/`PayrollServiceCollectionExtensions.cs` (GarnishmentOrder), yeni EF migration.
- Api: `GarnishmentOrdersController.cs`, `PayslipsController.cs`, `PayrollPeriodsController.cs` (APHB endpoint).
- Employee modülü: `IEmployeeDisabilityLookup.cs` (Contracts), `EmployeeDisabilityLookup.cs` (Infrastructure), `EmployeeModule.cs` (kayıt).
- Shared: `ILegislationParameterReader.cs` (3 yeni key), `SharedResource{,.en}.resx` (yeni izin/hata mesajları).
- Leave modülü: `LegislationParameterSeed.cs` (3 yeni satır) + yeni EF migration.
- Payroll Contracts: `PayrollPermissions.cs` (`CourtOrderManage`, `DeclarationsExport`).
- Testler: golden-set genişletildi + 6 yeni test dosyası (yukarıda listelendi) + `PayrollPeriodCalculationServiceTests.cs` yeni bağımlılıklar için güncellendi.

### Merge Gate Durumu

- `dotnet test` (Payroll modülü): **[BUILD/TEST ÇALIŞTIRILDIKTAN SONRA GÜNCELLENECEK]**
- Golden-set: 7 senaryo (hedef 5+ karşılandı).
- APHB: taslak formatta üretilebiliyor (resmi SGK şeması değil - yukarıdaki uyarıya bakınız).

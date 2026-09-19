# senseik-030 — İzin/Fazla Mesai Raporlama — İMPLEMENTASYON NOTU

**Durum: Tamamlandı (branch `b/leave-overtime-reporting`, bu doküman güncellendiğinde henüz push edilmedi).**

Kartın "senseik-012 merge olana kadar başlatılma" uyarısı geçersizdi — senseik-012 (fazla mesai ücret oranı, 270 saat
sayacı, serbest zaman expiry) zaten `dev`'e merge edilmişti (`e399943`, `origin/dev` HEAD `90de9e2`). Worktree bu
commit'ten `b/leave-overtime-reporting` olarak açıldı; `OvertimeRequest`/`OvertimeConversion`'ın kart senseik-012
sonrası güncel hali okunarak üzerine inşa edildi.

## 1. İzin mutabakat formu (Excel — PDF değil)

`LeaveReconciliationReportProvider` (`leave.reconciliation.form`, kategori `leave`,
`src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Reporting/LeaveReconciliationReportProvider.cs`).

- **Format kararı: Excel.** `Sense.Shared.Contracts.Export.IDocumentRenderer` (PDF) sözleşmesi var ama implementasyonu
  yok ("Faz 1 altyapı sözleşmesi... implementasyon ayrı bir turda eklenecektir" — kendi yorumu). PDF'i bu kartta
  implemente etmek kapsam dışı büyümeye yol açardı; mevcut `IReportExporter` (ClosedXML) altyapısını genişlettim.
- **Tasarım:** `LeaveRequestsReportProvider`/`LeaveBalanceSummaryReportProvider` ile aynı desen — tek sayfa, satır =
  çalışan (Kolay İK'daki "belge başına form" yerine birim/departman bazlı toplu liste; `ReportEmployeeSet` + dinamik
  org sütunları aynen tekrar kullanıldı). Sütunlar: Ad, Soyad, [org seviyeleri], Dönem, Kullanılan izinler (tür:gün
  özet metni), Kullanılan (toplam gün), **Çalışan imzası / İmza tarihi / İnsan Kaynakları onayı / Onay tarihi (boş,
  yazdırılıp elle imzalanacak)**.
- Yalnız `LeaveRequestStatus.Approved` talepler "kullanım" sayılır (Pending/Rejected/Cancelled hariç).
- Filtreler: Tarih aralığı (zorunlu, "dönem"), Birimler, işten ayrılanları dahil et.
- Yetki: `leave.requests.read` (LeaveRequestsReportProvider ile aynı — form bir izin talepleri özeti).
- Test: `LeaveReportProviderTests.Reconciliation_SummarizesApprovedUsage_WithBlankSignatureColumns` (Approved sayılıyor,
  Pending sayılmıyor, imza sütunları boş, dosya adı doğru).

## 2. Aylık fazla mesai raporu

`OvertimeMonthlyReportProvider` (`leave.overtime.monthly`, kategori `leave`,
`.../Reporting/OvertimeMonthlyReportProvider.cs`) — `LeaveReportProviderTests.cs`/`IReportProvider` deseniyle
genişletildi (kartın istediği gibi).

- Dönem "Ay"/"Yıl" filtreleriyle seçilir (`ReportParameters.ResolveMonthYearRange` — Kolay İK semantiği, bu turda
  kullanılan ilk yer); ikisi de boşsa cari ay varsayılır.
- Yalnız `OvertimeRequestStatus.Approved` talepler listelenir.
- Sütunlar: Ad, Soyad, [org seviyeleri], Tarih, Başlangıç saati, Süre (dakika), Ödenecek dakika, Ücret oranı
  (%25/%50 — `OvertimePayRateKind` enum text), **Dönüşüm türü / Dönüşüm durumu** (varsa `OvertimeConversion.Kind`/
  `.Status` enum text — Ödeme/Serbest zaman, Kaydedildi/İptal edildi/Süresi doldu; hiç dönüştürülmemiş talepte boş),
  Açıklama.
- Yetki: `leave.overtimes.read`.
- Yeni resx anahtarları (`SharedResource{,.en}.resx`): `report.col.{workDate,startTime,durationMinutes,
  payableMinutes,payRateKind,conversionKind,conversionStatus}`, `report.sheet.overtimeMonthly`,
  `report.enum.OvertimePayRateKind.{Rate25,Rate50}`, `report.enum.OvertimeConversionKind.{Pay,TimeOff}`,
  `report.enum.OvertimeConversionStatus.{Posted,Reversed,Expired}`.
- Test: `LeaveReportProviderTests.OvertimeMonthly_ListsApprovedRequests_WithRateAndConversionColumns` (ay/yıl filtresi
  dışındaki talep hariç tutuluyor, dönüştürülmemiş/dönüştürülmüş satırlar doğru render ediliyor).

Her iki provider da `LeaveServiceCollectionExtensions.AddLeaveServices` içinde `IReportProvider` olarak kayıtlı —
Raporlar menüsü kataloğu (`Sense.Modules.Reporting.Application.ListReportCatalogHandler`) tüm kayıtlı provider'ları
otomatik topladığı için ayrı bir menü/route kablolaması gerekmedi.

## 3. Fazla mesai → ödemeye çevirme (`OvertimeConversion.Kind=Pay`)

Kod içindeki yorum ("Pay (Expense PayrollAdjustment) and reversal come later" —
`src/Modules/Leave/Sense.Modules.Leave.Domain/Overtime/OvertimeConversion.cs`) bulundu ve **Pay kısmı bu kartta
tamamlandı** (reversal hâlâ kapsam dışı, ayrı bir kart).

### Payroll bağlantısı nasıl çözüldü (kritik karar)

Payroll'a bakmadan önce **zaten var olan** iki köprüyü buldum ve tasarımı bunların üzerine kurdum — yeni senkron
bağımlılık YARATILMADI:

1. **Pull tarafı (asıl bordro hesabı) — zaten hazır, değişiklik gerekmedi.**
   `Leave.Contracts.IOvertimePayrollReader.GetAsync(tenantId, year, month, employeeIds)` (Leave.Infrastructure'da
   `OvertimePayrollReader` implementasyonu) Payroll'un aylık fazla mesai nakit tutarını hesaplarken çağıracağı
   sözleşme — **senseik-012'de zaten yazılmış**. Kendi doc-comment'i açıkça şunu söylüyor: `Rate25Minutes`/
   `Rate50Minutes` = "approved requests... that were NOT converted to time off (still payable in cash - **either
   never converted, or converted with Kind=Pay**)". Yani reader zaten Kind=Pay'i "nakit" kovasına düşürecek şekilde
   yazılmıştı, ama Kind=Pay'i üretecek hiçbir kod yoktu (`OvertimeConversion`'da yalnızca `ToTimeOff` vardı). Kodu
   inceleyip (`OvertimeMonthlyReportProvider` testi ve `OvertimeTests.
   ConvertOvertimeToPayHandler_PostsConversion_WithoutLedgerRow_AndKeepsCashBucket` ile) doğruladım: reader'ın
   davranışı hiç değişmedi, `ToPay` sadece o zaten var olan sözleşmeyi "doldurdu".
   **Sonuç:** Payroll para tutarını KENDİ maaş/ücret verisinden hesaplayacak (Leave'in maaşa erişimi yok —
   Modules_TalkOnlyThroughContracts); Leave yalnızca "bu dakikalar nakit ödenecek" bilgisini taşıyor, tutarı değil.
   `OvertimeConversion.RateSnapshot` = talebin kendi `PayRateMultiplier`'ı (%25/%50 kart senseik-012 §1) — denetim
   izi için saklanıyor, hesaplamada kullanılmıyor.

2. **Push tarafı (geç dönüşüm / yeniden hesap tetikleyicisi) — yeni integration event.**
   `Sense.Modules.Payroll.Application.Events.PayslipDirtyFlagHandlers.cs`'in doc-comment'i (mevcut "Değişti
   mekanizması", P3) şunu **zaten açıkça bekliyordu**: *"OvertimeConvertedToPay do not exist in Leave.Contracts yet
   - faz3-durum-takibi.md P3 entry documents this as deferred, to be wired when Leave adds them, rather than
   fabricated here."* Bu kartta `Leave.Contracts.Events.OvertimeConvertedToPay` event'ini ekledim
   (`OvertimeIntegrationEvents.cs`), `ConvertOvertimeToPayHandler` onu outbox'a yazıyor (`IIntegrationEventOutbox`,
   aynı commit transaction'ında). **Payroll tarafına HİÇBİR DOSYA DOKUNULMADI** — senseik-019 (Payroll tam motor,
   `a/payroll-full-engine`) paralel/gelecekte bu dosyaya dokunacağı için (Payroll'un kendi handoff'u "GATE 1 onayı
   bekliyor, hiçbir dosya değiştirilmedi" diyor — henüz gerçek implementasyon yok) çakışma riskini sıfırladım.
   Event'in kendi doc-comment'i Payroll'a **tam olarak ne yapması gerektiğini** anlatıyor:
   `LeaveCancelledDirtyFlagHandler` ile birebir aynı desende (`IIntegrationEventHandler<OvertimeConvertedToPay>`,
   `PayslipDirtyFlagService.FlagAsync(tenantId, employeeId, workDate, "OvertimeConvertedToPay", eventId, ct)`),
   `WorkDate`'i kapsayan açık dönemi kirli işaretleyip bir sonraki hesaplamanın `IOvertimePayrollReader`'ı yeniden
   okumasını sağlamalı. Event kasıtlı olarak **tutar taşımıyor** — yalnızca "yeniden hesapla" tetikleyicisi.
   **Payroll tarafı motoru henüz `IOvertimePayrollReader`'ı tüketmiyor bile (senseik-019 §2d, senseik-012/014'ü
   bekliyor olarak dokümante edilmiş) — bu event de o slice tamamlanana kadar sessizce kayıtlı kalacak (mevcut
   `OvertimeConvertedToTimeOff`/`OvertimeConsentRecorded` gibi, onların da bugün tüketicisi yok); bu normal, Contracts
   üzerinden gevşek bağlanmanın bir sonucu.**

### Yapılan kod değişiklikleri (Pay)

- `OvertimeConversion.ToPay(request, basis, convertedByUserId, at, note)` — approved + henüz dönüştürülmemiş talep
  şartı (`ToTimeOff` ile aynı guard'lar), `BasisMinutes`/`ResultMinutes` = payable veya full dakika (dönüşüm yok,
  saf "işaretleme"), `ResultDays=0`, `LeaveTypeId`/`LedgerEntryId`/`UsedByDate` hep null (ledger'a dokunmuyor).
- `ConvertOvertimeToPayCommand`/`ConvertOvertimeToPayHandler` (`leave.overtimes.convert` izni — Time Off ile aynı,
  tek bir "dönüştürme" yetkisi), `POST /api/v1/leave/employees/{employeeId}/overtimes/{id}/convert-to-pay`.
- `OvertimeConvertedToPay` integration event (yukarıda).
- Testler: `OvertimeDomainTests.ConvertToPay_SnapshotsRequestRate_AndBlocksSecondConversion` (domain), `OvertimeIntegrationTests.ConvertOvertimeToPayHandler_PostsConversion_WithoutLedgerRow_AndKeepsCashBucket`
  (handler + outbox + `IOvertimePayrollReader` regresyonu — Kind=Pay'in hâlâ doğru nakit kovasına düştüğünü kanıtlıyor).

## Demo seed

`OvertimeRequest`/`OvertimeConversion` için hiç demo verisi yoktu (merge_gate "demo seed'de ... mesai raporu
üretiliyor" diyor). `src/Sense.Api/SeedData/DataSeeder.DemoOvertime.cs` eklendi (yeni partial dosya, `DataSeeder.cs`
`SeedDemoLeaveAsync` çağrısının hemen ardına tek satır eklendi — mevcut satırlar değişmedi):

- `calisan@demo.local` için 2, `yonetici@demo.local` için 1 onaylı fazla mesai talebi (son ~2 hafta, `today` bazlı;
  `DemoOvertimeWorkDate` önceki aya düşen tarihi ayın ilk iş gününe çeker — ayın ilk iş gününden önce seed edilirse
  önceki ayda kalır) — biri (`calisan`, Rate25) `OvertimeConversion.ToPay` ile açıkça ödemeye çevrilmiş.
- Bu sayede seed sonrası Raporlar → İzinler'den `leave.overtime.monthly`'yi filtresiz çalıştırmak (cari ay varsayılanı)
  doğrudan örnek veri gösterir; `leave.reconciliation.form` zaten mevcut demo izin verisiyle (aynı dosyadaki
  `SeedDemoLeaveAsync`) çalışır, ek veri gerekmedi.

## Kapsam dışı bırakılanlar (backlog'a not)

- **"Kendin Oluştur" özel rapor motoru** — kartın kapsamı dışı olarak belirtilmişti, yapılmadı. Ayrı, büyük bir kart
  gerektiriyor (rapor motoru güç alanı: sütun/filtre seçici, kaydedilmiş rapor tanımları).
- **PDF export** (`IDocumentRenderer` implementasyonu) — Excel yeterli kabul edildi, PDF ihtiyacı doğarsa (örn.
  gerçek "belge başına form" + ıslak imza taraması) ayrı bir kart (`payroll-full-engine` handoff'unda da 3a olarak
  aynı ihtiyaç not düşülmüş — muhtemelen ortak bir Shared kartı olmalı).
- **Reversal** (`OvertimeConversionStatus.Reversed`'a giden bir aksiyon) — hem Pay hem TimeOff için hâlâ yok, kod
  yorumunda "Reversal still comes later" olarak bırakıldı, bu kart yalnızca Pay'i tamamladı.
- **Payroll'un `OvertimeConvertedToPay`'i gerçekten tüketmesi** — Payroll tarafı kod değişikliği bu kartın kapsamı
  dışı (Contracts-only kural + paralel senseik-019 çakışma riski). senseik-019 §2d'ye (fazla mesai/hafta tatili/resmi
  tatil dilimi) ulaştığında: (a) `IOvertimePayrollReader`'ı ay bazlı gross hesaba bağlamalı, (b)
  `PayslipDirtyFlagHandlers.cs`'e `OvertimeConvertedToPayDirtyFlagHandler : IIntegrationEventHandler<OvertimeConvertedToPay>`
  eklemeli (yukarıdaki desen).

## Doğrulama

2026-09-19 (ikinci oturum, host'ta native `dotnet` + Docker Desktop Testcontainers):

- `dotnet build src/Sense.Api` — 0 hata.
- `dotnet ef migrations has-pending-model-changes --context LeaveDbContext` — "No changes have been made to the
  model since the last migration" → **migration gerekmedi** (`ToPay` yeni kolon eklemiyor, mevcut
  `OvertimeConversion` şemasını kullanıyor).
- `dotnet test tests/Modules/Sense.Modules.Leave.Tests --filter "~Overtime|~LeaveReportProvider"` — **37/37 geçti**
  (kartın tüm yeni testleri: `Reconciliation_*`, `OvertimeMonthly_*`, `ConvertToPay_*`,
  `ConvertOvertimeToPayHandler_*` dahil).
- Tam Leave koşusu (150 test): 131 geçti, 19 başarısız — **hepsi bu karttan bağımsız, önceden var olan** bir DI
  açığı: senseik-007 `EmployeeHiredHandler`'a `IEmployeeBirthDateLookup` bağımlılığı eklemiş ama yalnız
  `OvertimeTests`/`LeaveBalanceRecalculationJobTests` fixture'larına kaydı eklenmiş; `LeaveRequestIntegrationTests`,
  `LeaveManagementIntegrationTests`, `LeaveIntegrationTests` fixture'ları `EmployeeHired` yayınlarken
  "Unable to resolve service for type IEmployeeBirthDateLookup" ile düşüyordu. Bu kartta aynı kayıt (Employee
  DbContext + `EmployeeBirthDateLookup` + migrate) o üç fixture'a da eklendi (yalnız test kodu). **Düzeltme sonrası
  yeniden koşu yapılamadı**: Docker Desktop daemon oturum sırasında yanıt vermez hale geldi
  (`DockerUnavailableException`, `docker ps` zaman aşımı — paralel oturumların kaynak çekişmesi). Ayrıca ikinci bir
  koşuda `AbsenceIntegrationTests` (7) aynı Docker sorunundan `TaskCanceledException` ile düştü (ilk koşuda
  geçmişlerdi). **Sonraki oturum**: `dotnet test tests/Modules/Sense.Modules.Leave.Tests` ve
  `tests/Modules/Sense.Modules.Reporting.Tests` yeniden koşturulmalı.
- `dev` (`90920c3`) üzerine rebase edildi; tek çakışma `SharedResource{,.en}.resx`'te senseik-029 puantaj anahtarlarıyla
  yan yana ekleme idi (ikisi de korundu, yinelenen anahtar yok). Rebase sonrası: `Sense.Api` + Leave test projesi
  build 0 hata, Leave pending model change yok, DB'siz Leave testleri (`Category!=Integration`) 105/105 yeşil.
  Entegrasyon testleri Docker yanıtsız olduğu için rebase sonrası tekrar koşulamadı.
- Web tarafına dokunulmadı (tsc/eslint gerekmedi).

## Kritik dosyalar

- `src/Modules/Leave/Sense.Modules.Leave.Domain/Overtime/OvertimeConversion.cs` (`ToPay`)
- `src/Modules/Leave/Sense.Modules.Leave.Application/Overtime/OvertimeConsentAndConversionFeatures.cs` (`ConvertOvertimeToPayCommand`/`Handler`)
- `src/Modules/Leave/Sense.Modules.Leave.Contracts/Events/OvertimeIntegrationEvents.cs` (`OvertimeConvertedToPay`)
- `src/Modules/Leave/Sense.Modules.Leave.Contracts/IOvertimePayrollReader.cs` (Payroll pull-köprüsü, değişmedi — okundu/doğrulandı)
- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Reporting/{LeaveReconciliationReportProvider,OvertimeMonthlyReportProvider}.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/LeaveServiceCollectionExtensions.cs` (yeni provider kayıtları)
- `src/Sense.Api/SeedData/DataSeeder.DemoOvertime.cs` (yeni), `DataSeeder.cs` (tek satır ekleme)
- `tests/Modules/Sense.Modules.Leave.Tests/{OvertimeTests.cs,LeaveReportProviderTests.cs}`
- `src/Modules/Payroll/Sense.Modules.Payroll.Application/Events/PayslipDirtyFlagHandlers.cs` (Payroll tarafı — DOKUNULMADI, yalnız okundu; yukarıdaki desen orada tekrarlanmalı)

---
*Kaynak: Kullanıcı isteği (2026-09-18) — kolayik.com/izin-fazla-mesai-yonetimi rakip analizi. Agent board:
`docs/agent-board/cards/senseik-030-leave-overtime-reporting.json`.*

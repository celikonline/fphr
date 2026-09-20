# senseik-048 — İK için şirket geneli izin/fazla mesai defteri — UYGULAMA NOTU

**Durum: GATE 1/2 onaylı, uygulandı, branch `b/hr-company-wide-leave-ledger`.** Build/test doğrulaması bu oturumda
Docker daemon'ının paralel worktree oturumlarından kaynaklanan aşırı yüklenmesi nedeniyle **tamamlanamadı**
(`docker ps` bile 120s+ yanıt vermedi; ayrıca bu sandbox'ta host `pnpm`/`node` PATH'te değildi). Kod, mevcut
`LeaveBalanceSummaryReportProvider`/`LeaveRequestsReportProvider` desenleriyle satır satır karşılaştırılarak elle
doğrulandı (bkz. altta "Doğrulama"), ama **merge'den önce `dotnet test` (Leave) + `pnpm exec tsc --noEmit` gerçekten
çalıştırılmalı** — bu, kartın merge gate'i zaten şart koşuyor.

## Ekran nasıl çalışıyor

Sol menüde "İzinlerim"/"Fazla mesailerim" (nav.overtimes) **yanına**, ayrı bir kalem: "Şirket izin/mesai defteri"
(`nav.companyLedger`, `/app/leave/company-ledger`, `leave.company_ledger.read` izniyle kapılı). Tek bir filtrelenebilir
tablo — üç ayrı ekran/sekme değil, üç soruyu da aynı satır kümesinin farklı sıralama/filtre kombinasyonları cevaplıyor:

- **"Bu ay kimler izinde"** → "Sadece bu ay izinde olanlar" checkbox'ı + Bu Ay İzinde Mi kolonu (rozet + tooltip'te
  tarih aralığı/izin türü).
- **"Yıllık izin birikimi en yüksek 10 kişi"** → "Yıllık izin bakiyesi" kolonuna tıklayıp azalan sıralama; ilk 10
  satır cevap.
- **"270 saati aşan fazla mesai var mı"** → "Sadece {{hours}} saati aşan fazla mesailer" checkbox'ı ("270" backend'in
  o an yürürlükteki `Overtime.AnnualLimitHours` Legislation Parameter'ından geliyor, hardcode değil) + 270 Saat Sınırı
  kolonu (rozet).

Filtreler: Birim seçici (`OrgUnitSelect` — mevcut departman/lokasyon ağacı, team-timesheet.tsx'teki gibi tek seçim +
alt birimler dahil), ay/yıl gezinme okları (bugünün ayı varsayılan), "İşten ayrılanları da göster" switch'i. Tablo
`useDataTable`/`DataTable` (TanStack) ile — sütun başlığına tıklayarak istemci tarafı sıralama, 20 satırlık sayfalama.

Yıllık izin bakiyesi her zaman **bugüne göre** anlık projekte edilir (Ay/Yıl filtresinden bağımsız — ranking sorusu
zaten "şu an" içindir); Ay/Yıl filtresi sadece "bu ay izinde" penceresini ve fazla mesai yıl toplamını değiştirir.

**Excel/CSV dışa aktarım**: "Excel'e aktar" butonu mevcut `IReportProvider`/ClosedXML deseniyle yeni bir sağlayıcı
(`leave.company_ledger`) çağırıyor — `reports.service.ts`'teki `requestReport`/`downloadReportExecution`/
`saveDownloadedReportFile` akışı aynen kullanıldı (reports.tsx'teki genel akışla birebir). Bu sağlayıcı otomatik
olarak genel Raporlar → İzinler kataloğunda da görünür (bonus, ayrı iş gerektirmedi).

## Backend

- **Yeni permission**: `LeavePermissions.CompanyLedgerRead` = `leave.company_ledger.read`. `SystemRoleDefinitions.cs`'e
  **dokunulmadı** — HR/HRSpecialist zaten `"leave.*"` joker deseniyle, Owner/Admin `"*"` ile bu yeni anahtarı otomatik
  alıyor (regex `^leave\..*$` yeni `leave.company_ledger.read` ile eşleşiyor — elle doğrulandı). Senkronizasyon
  `SystemRolePermissionSynchronizer` üzerinden mevcut kiracılara da otomatik ulaşacak (CLAUDE.md'deki kural).
- **Ortak veri toplama** (DRY): `Sense.Modules.Leave.Infrastructure/Reporting/CompanyLeaveLedgerData.cs` (yeni,
  internal) — hem interaktif sorgu hem Excel aynı `BuildAsync`'i çağırıyor. `ReportEmployeeSet` (mevcut) ile
  employee+org+data-scope çözümlemesi tekrar kullanıldı; yıllık bakiye hesabı `LeaveBalanceSummaryReportProvider`'daki
  Profiles/RuleSets/LedgerEntries akışının **bire bir kopyası** (o dosyaya dokunulmadı — merge çakışma riskini
  azaltmak için mevcut, çalışan koda müdahale edilmedi, aynı mantık yeni bir yardımcıda tekrarlandı). "Bu ay izinde"
  onaylı `LeaveRequest`'lerin `[monthStart, monthEnd]` ile kesişimi; fazla mesai yıl toplamı
  `OvertimeRequestRules.EvaluateAsync`'teki taban (DurationMinutes, Pending+Approved) ile aynı, sınır
  `LegislationParameterKeys.OvertimeAnnualLimitHours` (varsayılan 270, `OvertimeRequestRules.FallbackAnnualLimitHours`).
- **İnteraktif JSON**: `GetCompanyLeaveLedgerQuery`/`CompanyLeaveLedgerDto`/`CompanyLeaveLedgerRowDto`
  (`Sense.Modules.Leave.Application/Ledger/CompanyLeaveLedgerFeatures.cs`) — handler'ı ise **Infrastructure'da**
  (`CompanyLeaveLedgerQueryHandler.cs`, internal) çünkü `LeaveDbContext`'e doğrudan erişip birden çok çalışanı tek
  seferde okuyor (repository arayüzleri tek-çalışan odaklı, `ILeaveRequestRepository.ListActiveInRangeAsync` hariç —
  ama Profiles/RuleSets/LedgerEntries/OvertimeRequests için toplu okuma yok). `LeaveModule.AddModuleHandlers` zaten
  Infrastructure derlemesini de tarıyor (`typeof(LeaveDbContext).Assembly`), o yüzden ayrı bir DI kaydı gerekmedi.
  Controller: `GET /v1/leave/company/ledger?year=&month=&orgUnitId=&includeTerminated=`
  (`LeaveCompanyLedgerController.cs`).
- **Excel**: `CompanyLeaveLedgerReportProvider.cs` (`leave.company_ledger`, kategori `ReportCategories.Leave`) —
  Ay/Yıl/Birimler/İşten-ayrılanlar parametreleri, dinamik kariyer kolonları (diğer izin raporlarıyla tutarlı).
  `LeaveServiceCollectionExtensions.cs`'e DI kaydı eklendi (append).
- **Yeni resx anahtarları** (`SharedResource{,.en}.resx`, `report.col.*`/`report.sheet.*`): `onLeave`, `leaveStart`,
  `leaveEnd`, `overtimeHoursYtd`, `overtimeLimitExceeded`, `companyLeaveLedger` (sheet adı). Mevcut `firstName`,
  `lastName`, `leaveType`, `remainingBalance` anahtarları tekrar kullanıldı.

## Web

- Yeni sayfa: `web/src/pages/leave/company-leave-ledger.tsx`.
- Route: `App.tsx` → `leave/company-ledger` (lazy).
- Nav: `navigation.ts`'e `companyLedger` kalemi **append edildi** (hot file, append-only kuralına uyuldu),
  `leave.company_ledger.read` iznine bağlı.
- Servis: `leave.service.ts`'e `getCompanyLeaveLedger` + tipler eklendi (append, dosya sonuna).
- i18n: `leave.json` (tr/en/de) → yeni `companyLedger` bölümü; `navigation.json` (tr/en/de) → `nav.companyLedger`;
  `reports.json` (tr/en/de) → `catalog.leave.company_ledger` (Excel sağlayıcısının genel Raporlar kataloğundaki adı).

## Test

`tests/Modules/Sense.Modules.Leave.Tests/CompanyLeaveLedgerTests.cs` (yeni, `LeaveReportProviderTests.cs`'teki
fixture deseniyle, real PostgreSQL/Testcontainers):

1. `GetCompanyLeaveLedgerHandler_ReportsOnLeave_Balance_AndOvertimeLimit` — bir çalışan Haziran 2026'da onaylı izinli
   (org yolu, tarih aralığı, izin türü doğrulanıyor), yıllık bakiye 14 tahakkuk − 2 kullanım = 12 doğrulanıyor; ikinci
   bir çalışan 14×20 saat = 280 saatlik (270 saatlik yasal sınırın üstünde, `OvertimeLimits.MaxDurationMinutes`=24s
   sınırına uymak için 14 ayrı kayda bölündü) fazla mesaisi var, `overtimeLimitExceeded=true` ve bakiyesi `null`
   (profil yok) doğrulanıyor. Handler doğrudan DI'dan (`IQueryHandler<...>`) çözülüyor — `Dispatcher`'ın
   `RequiresPermission` ardışığı burada test edilmiyor (mevcut `LeaveReportProviderTests`'in `IReportProvider`'ı
   doğrudan çözme yaklaşımıyla aynı, kasıtlı).
2. `CompanyLeaveLedgerReportProvider_ExportsSameData_WithDynamicOrgColumns` — Excel çıktısında "Şirket"/"Departman"
   dinamik kolonları ve "Kalan" (bakiye) kolonu var, 1 satır.

**Bu oturumda çalıştırılamadı** (Docker/host toolchain erişilemedi) — merge gate'in şart koştuğu gibi
`dotnet test` (Leave modülü) mutlaka önce çalıştırılmalı. Aynı şekilde `pnpm exec tsc --noEmit` de host'ta
doğrulanmalı (bu sandbox'ta `pnpm`/`node` PATH'te yoktu).

## Riskler / bilinen sınırlamalar

- Yıllık bakiye ve fazla mesai yıl toplamı hesabı `LeaveBalanceSummaryReportProvider`/`OvertimeRequestRules` ile
  **aynı mantığın kasıtlı tekrarı** — o iki dosyaya dokunulmadı (paralel Leave çalışmalarıyla (senseik-012/014/036)
  çakışma riskini azaltmak için). İleride üçü ortak bir yardımcıya çıkarılabilir (YAGNI gereği şimdi yapılmadı).
- Excel export'un dinamik kariyer kolonları ile ekranın tek "Birim" metin kolonu kasıtlı farklı — ekran basit/okunur
  kalsın diye (tam hiyerarşi tabloya sığmaz), Excel diğer izin raporlarıyla tutarlı kalsın diye.
- "Bu ay izinde" sütunu bir çalışanın ayla kesişen **birden fazla** onaylı izni varsa yalnızca en erken başlayanı
  gösteriyor (V1 basitleştirmesi, Kolay İK'daki "birincil kayıt" yaklaşımıyla tutarlı).

## Kritik Dosyalar

- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Reporting/CompanyLeaveLedgerData.cs` (yeni, ortak mantık)
- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Reporting/CompanyLeaveLedgerQueryHandler.cs` (yeni)
- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Reporting/CompanyLeaveLedgerReportProvider.cs` (yeni)
- `src/Modules/Leave/Sense.Modules.Leave.Application/Ledger/CompanyLeaveLedgerFeatures.cs` (yeni)
- `src/Modules/Leave/Sense.Modules.Leave.Api/Controllers/LeaveCompanyLedgerController.cs` (yeni)
- `src/Modules/Leave/Sense.Modules.Leave.Contracts/LeavePermissions.cs` (append)
- `web/src/pages/leave/company-leave-ledger.tsx` (yeni)
- `web/src/services/leave.service.ts`, `web/src/config/navigation.ts`, `web/src/App.tsx` (append)
- `tests/Modules/Sense.Modules.Leave.Tests/CompanyLeaveLedgerTests.cs` (yeni)

---
*Kaynak: senseik-048 kart isteği. Agent board: `docs/agent-board/cards/senseik-048-hr-company-wide-leave-ledger.json`.*

## Merge öncesi doğrulama (2026-09-19)

- Branch `dev` üzerine rebase edildi; `web/src/App.tsx` çakışması (dev'in `company-calendar` rotası + bu branch'in
  `leave/company-ledger` rotası) ikisi birden tutularak çözüldü; kart JSON'u branch versiyonuyla çözüldü.
- **Hata düzeltmesi:** `CompanyLeaveLedgerData` yıllık bakiyeyi `IReadOnlyDictionary<Guid, decimal>.GetValueOrDefault`
  ile okuyordu → profili/yıllık türü olmayan çalışan `null` ("—") yerine `0` bakiye gösteriyordu (mevcut test
  `overLimitRow.AnnualLeaveBalance.ShouldBeNull()` bunu yakaladı). `TryGetValue` ile düzeltildi.
- İzin adı dev'deki Türkçeleştirmeye (90de9e2) uyarlandı; `leave.company_ledger.read` için `SharedResource{,.en}.resx`
  izin adı yerelleştirmesi eklendi; Excel kolonu "270 saat" sabitini değil yasal parametreyi yansıtacak şekilde
  "Yıllık fazla mesai sınırı aşıldı mı" oldu.
- Yetki (sunucu tarafı): JSON sorgusu `[RequiresPermission("leave.company_ledger.read")]` (Dispatcher ardışığı),
  Excel `IReportProvider.RequiredPermission` (ReportCommands kontrolü); çalışan kümesi `IDataScopeService` ile aynı
  anahtarın veri kapsamına daraltılıyor. Not: mevcut `leave.*.read` / `*.read` joker desenleri PayrollSpecialist,
  Timekeeper (Departman kapsamı), Auditor, ApiIntegration rollerine de bu izni verir; Manager almaz.
- Yeni testler: `CompanyLedger_EntryPoints_AreGatedByDedicatedPermission`,
  `GetCompanyLeaveLedgerHandler_ExcludesEmployeesOutsideDataScope`.
- Gate sonuçları: Leave test projesi derleniyor; filtreli koşuda (CompanyLeaveLedgerTests + LeaveReportProviderTests)
  düzeltme öncesi 5/6 geçti (tek hata = yukarıdaki bakiye hatası). Düzeltme sonrası koşu Docker Desktop yanıt
  vermediği için (npipe bağlantı hatası, 20+ dk) **tamamlanamadı** — Testcontainers testleri merge sonrası tekrar
  koşulmalı. Web: `tsc --noEmit` bu branch'e ait hata yok (yalnız dev'de önceden var olan 4 hata:
  `protected-route.test.tsx`, `home.test.tsx`), `eslint src` 0 hata / 1 önceden var olan uyarı.
- Migration yok.

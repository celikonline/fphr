# senseik-029 — Puantaj Raporlama: Excel/Mali Müşavir Çıktısı, Ay Sonu Kapatma, İzinli Günde Çalışma Detektörü

**Durum: review — implementasyon tamamlandı, testler yeşil (2026-09-19 devam turu). GATE 1/2 önceden onaylı. Branch: `b/attendance-reporting-export`, `dev`'in güncel halinden (senseik-012/014 dahil) açıldı.**

## Önkoşul doğrulaması

Kartın "012/014 merge olana kadar başlatılma" uyarısı bu turda geçersiz kılınmıştı. Worktree önce eski bir `dev` noktasında (`0d7261f`) açılmıştı — 012/014 merge commit'lerini (`e399943`, `031aaa3`) içermiyordu. Branch, güncel yerel `dev` (`d5bc322`, senseik-054 dahil) üzerinden yeniden oluşturuldu (`git checkout -b b/attendance-reporting-export dev`) ve AttendanceAnomaly/AttendanceDay'in G-02 genişlemesi (NetWorkedMinutes, BreakMinutesApplied, NightMinutes, OvertimeMinutes, DailyLimitExceeded/NightLimitExceeded) okunarak rapor sağlayıcılarına dahil edildi.

## 1. Aylık puantaj özeti Excel raporu (`attendance.monthly.summary`)

`src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure/Reporting/AttendanceMonthlySummaryReportProvider.cs` — mevcut Employee/Leave `IReportProvider` deseniyle (ClosedXML üzerinden `IReportExporter`, `ReportEmployeeSet` ile veri kapsamı/birim filtresi). Kategori: `Employee` (Kolay İK'de "Çalışma takvimi raporu" da "Raporlar > Çalışan" altında, 11.3.12 satır 252). Filtreler: Yıl+Ay (zorunlu), Birimler, işten ayrılanları dahil et.

Kolonlar: Ad, Soyad, kariyer kolonları, Kayıtlı gün, Çalışılan gün, Çalışılan saat, Fazla mesai (saat), Gece çalışması (saat), Geç kalma sayısı, Erken çıkış sayısı, Eksik gün sayısı, İzinli günde çalışma sayısı, Çözülmemiş uyuşmazlık sayısı. Tamamı zaten var olan `AttendanceDay` alanlarından hesaplanır (`AttendanceMonthlyAggregate.From` — saf, yan etkisiz bir indirgeme); rapor üretimi hiçbir şey yazmaz.

## 2. Ay sonu kapatma / mali müşavir raporu (`attendance.monthly.closing`)

`AttendanceMonthlyClosingReportProvider.cs` — Kategori: `Finance`. Aynı `AttendanceMonthlyAggregate`'e ek olarak `Leave.Contracts.ILeaveDayReader` üzerinden ücretli/ücretsiz/raporlu izin gün sayılarını, `IMonthlyTimesheetApprovalRepository` üzerinden **mevcut ama önceden hiç kullanılmayan** `MonthlyTimesheetApproval` bayrağının o dönem için onaylı olup olmadığını (+ onay tarihi) ekler. Yalnız okur — onay bayrağını raporun kendisi set etmez.

**Önemli bulgu:** `MonthlyTimesheetApproval` (senseik-012/014 tarafından eklenmiş domain aggregate + DbSet + repository arayüzü) hiçbir Application/Infrastructure/API bağlantısına sahip değildi — ne bir repository implementasyonu, ne bir command/query, ne bir DI kaydı vardı; tamamen ölü kod olarak duruyordu. Bu turda tamamlandı:
- `MonthlyTimesheetApprovalRepository` (`Persistence/Repositories.cs`) + `MonthlyTimesheetApprovalConfiguration` (`AttendanceDbContext.cs`, `(TenantId, EmployeeId, PeriodMonth)` üzerinde unique index — yeni migration `MonthlyTimesheetApprovalUniqueIndex`).
- `ApproveMonthlyTimesheetCommand`/`Handler` (`Application/MonthlyTimesheet/MonthlyTimesheetApprovalFeatures.cs`, yeni izin `attendance.timesheet.approve` (senseik-056 ile ortak anahtar, bkz. "senseik-056 ile uzlaştırma") — mevcut `attendance.*` joker izinleri Owner/Admin/HR/HRSpecialist/Timekeeper'a otomatik ulaşır, `SystemRoleDefinitions.cs`'e dokunmaya gerek yok). Kapatma, onaylamadan **önce** o ayın izin çapraz kontrolünü (`LeaveOverlapChecker.ApplyRangeAsync`) yeniden çalıştırır — kapatma anındaki en güncel Leave verisiyle son bir tarama.
- `ListMonthlyTimesheetApprovalsQuery`/`Handler` (dönem başına onay durumu okuma).
- `TimesheetController`'a `POST /attendance/timesheet/monthly/employees/{employeeId}/approve?periodMonth=YYYY-MM-01` eklendi (senseik-056 ile aynı rota; yanıt `MonthlyTimesheetApprovalDto`).
- Web UI'da "Tümünü Onayla" butonu hâlâ YOK (bu kartın kapsamı dışında bırakıldı — API/Application hazır, buton eklenmesi ayrı bir kart).

## 3. İzinli günde çalışma detektörü (`AttendanceAnomaly.WorkedWhileOnLeave`)

- `AttendanceAnomaly` enum'ına yeni flag: `WorkedWhileOnLeave = 1 << 9` (mevcut 8 bayraktan sonra, `[Flags]` string conversion kullandığı için — kolon zaten `text`, yeni bayrak migration gerektirmez).
- `AttendanceDay.ApplyLeaveOverlap(bool)` (yeni domain metodu, saf/yan etkisiz): fiilen bir giriş/çıkış kaydı olan bir günün onaylı izinle kesiştiği öğrenildiğinde bayrağı ekler (günü yeniden Unresolved yapar), kesişmiyorsa kaldırır (başka anomali kalmadıysa Resolved'a döner). İzin gününde hiç kayıt yoksa (normal izin günü) bayrak hiç eklenmez.
- **Modüller arası sözleşme (ADR 0001):** yeni `Leave.Contracts.IApprovedLeaveDateLookup` (+ `ApprovedLeaveDay` DTO) — bir dönemdeki onaylı izin taleplerini gün gün açar (`ILeaveDayReader`in bucket'lanmış toplamının aksine, gün seviyesinde granülerlik gerekiyor). `Leave.Infrastructure.Lookups.ApprovedLeaveDateLookup` implementasyonu, `AddLeaveCoreServices`'te kayıtlı (Api+Worker, `ILeaveDayReader` ile aynı kapsam).
- Attendance tarafı: `LeaveOverlapChecker` (Application, `AttendanceDays/LeaveCrossCheckFeatures.cs`) tek/toplu gün için Leave sorgusu yapıp `ApplyLeaveOverlap`'i uygular. `DetectWorkedWhileOnLeaveCommand`/`Handler` toplu yeniden tarama sağlar (retroaktif izin onayları için) — `POST /attendance/timesheet/detect-leave-overlap`.

**BİLİNÇLİ TASARIM KARARI — gerçek zamanlı tetikleme YOK:** `RecordAttendanceHandler` (manuel puantaj kaydı) ve `AttendanceImportRowHandler` (PDKS içe aktarma) bu turda `LeaveOverlapChecker`'a bağlanmadı. İlk denemede bağlanmıştı ama geri alındı: bu iki handler Attendance'ın en sık çalışan yazma yolları ve `LeaveOverlapChecker` çalışma zamanında `Leave.Contracts.IApprovedLeaveDateLookup`'ı (Leave.Infrastructure'da implemente) gerektiriyor — yalnız Attendance'ı kuran mevcut test fixture'ları (`AttendanceIntegrationTests.cs`, `AttendanceImportRowHandlerTests.cs`, vb.) Leave DI'ını hiç wire etmiyor. `AddScoped` kaydı tek başına lazy olduğu için mevcut testler şu an KIRILMAZ (hiçbiri `DetectWorkedWhileOnLeaveCommand`/`ApproveMonthlyTimesheetCommand`'ı tetiklemiyor), ama bu iki komutu Leave'siz bir DI konteynerinde dispatch etmeye çalışan gelecekteki bir test/entegrasyon aynı sorunu yaşar. Bu yüzden flag SADECE şu iki yolda üretilir: (a) elle/toplu `DetectWorkedWhileOnLeaveCommand`, (b) `ApproveMonthlyTimesheetCommand`'ın ay sonu taraması. **Sonraki tur için not:** gerçek zamanlı tetikleme isteniyorsa ya Leave'in "LeaveRequestApproved" gibi bir integration event'i eklenip Attendance'ın Worker'da abone olması (event-driven, mevcut `LeaveCancelled` desenine benzer) ya da bu iki handler'ın DI'ının her ortamda Leave ile birlikte kurulacağı garanti edilmesi gerekir.

## Kabul kriterleri durumu

1. ✅ `attendance.monthly.summary` Excel raporu — ClosedXML, mevcut desen.
2. ✅ Ay sonu kapatma raporu (`attendance.monthly.closing`) + `MonthlyTimesheetApproval`'ın ilk kez uçtan uca çalışır hale getirilmesi.
3. ✅ `WorkedWhileOnLeave` flag'i — `UnplannedWork`'ten ayrı, Leave.Contracts üzerinden çapraz kontrol. Gerçek zamanlı değil, toplu/ay-sonu tetiklemeli (yukarıdaki not).

## Test

`tests/Modules/Sense.Modules.Attendance.Tests/AttendanceLeaveCrossCheckAndReportingTests.cs` (yeni, gerçek Postgres, hem AttendanceDbContext hem LeaveDbContext wire edilmiş — `Sense.Modules.Leave.Tests.LeaveReportProviderTests`'in simetriği): izin kesişimi flag'lemesi/temizlenmesi, ay sonu kapatma (ilk kez + reapprove), her iki rapor sağlayıcısının Excel içeriği. `tests/Modules/Sense.Modules.Attendance.Tests/AttendanceDayDomainTests.cs`'e `ApplyLeaveOverlap` + `AnomalyResolutionAdvisor` genişlemesi için birim testleri eklendi.

`dotnet test` sonucu (2026-09-19): `Sense.Modules.Attendance.Tests` 238/238 başarılı; `Sense.Modules.Leave.Tests` 127/146 — 19 başarısızlık bu karttan bağımsız, dev'de mevcut: Leave entegrasyon fixture'ları `EmployeeHired` yayınlarken `IEmployeeBirthDateLookup` kaydı eksik (`EmployeeHiredHandler` bu bağımlılığı senseik-007 / `569a553` ile aldı; örneklenen testlerin hatası `Unable to resolve service for type IEmployeeBirthDateLookup`). `dotnet ef migrations has-pending-model-changes` (AttendanceDbContext): değişiklik yok. Web: `tsc --noEmit` yalnız dokunulmamış test dosyalarında önceden var olan 4 hata (`protected-route.test.tsx`, `home.test.tsx` — `roleLabels`/`mfaSetupRequired` fixture eksikliği, dev'de de mevcut); `eslint src` 0 hata.

## Dokunulan/eklenen dosyalar

- `src/Modules/Attendance/Sense.Modules.Attendance.Domain/AttendanceDays/AttendanceAnomaly.cs` (yeni flag + advisor)
- `src/Modules/Attendance/Sense.Modules.Attendance.Domain/AttendanceDays/AttendanceDay.cs` (`ApplyLeaveOverlap`)
- `src/Modules/Attendance/Sense.Modules.Attendance.Contracts/AttendancePermissions.cs` (`TimesheetClose`)
- `src/Modules/Attendance/Sense.Modules.Attendance.Application/AttendanceDays/LeaveCrossCheckFeatures.cs` (yeni)
- `src/Modules/Attendance/Sense.Modules.Attendance.Application/MonthlyTimesheet/MonthlyTimesheetApprovalFeatures.cs` (yeni)
- `src/Modules/Attendance/Sense.Modules.Attendance.Application/AttendanceErrors.cs` (`DateRangeInvalid`)
- `src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure/Persistence/AttendanceDbContext.cs` (`MonthlyTimesheetApprovalConfiguration`)
- `src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure/Persistence/Repositories.cs` (`MonthlyTimesheetApprovalRepository`)
- `src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure/Persistence/Migrations/*MonthlyTimesheetApprovalUniqueIndex*` (yeni migration)
- `src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure/Reporting/*` (yeni: `ReportEmployeeSet`, `AttendanceMonthlyAggregate`, `AttendanceMonthlySummaryReportProvider`, `AttendanceMonthlyClosingReportProvider`)
- `src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure/AttendanceServiceCollectionExtensions.cs` (yeni kayıtlar)
- `src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure/Sense.Modules.Attendance.Infrastructure.csproj` (Reporting.Contracts referansı)
- `src/Modules/Attendance/Sense.Modules.Attendance.Api/Controllers/TimesheetController.cs` (iki yeni uç nokta)
- `src/Modules/Leave/Sense.Modules.Leave.Contracts/IApprovedLeaveDateLookup.cs` (yeni)
- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Lookups/ApprovedLeaveDateLookup.cs` (yeni)
- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/LeaveServiceCollectionExtensions.cs` (kayıt)
- `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx` (yeni permission/error/report kolon-sayfa-enum anahtarları)
- `web/src/services/attendance.service.ts` (`AttendanceAnomalyFlag` union'a `workedWhileOnLeave`)
- `tests/Modules/Sense.Modules.Attendance.Tests/*` (yeni test dosyası + csproj referansları + `AttendanceDayDomainTests.cs` genişlemesi)

## Sonraki tur için açık noktalar

1. Web UI: "Tümünü Onayla" (ay sonu kapatma) butonu senseik-056'da. İki yeni rapor Raporlar menüsüne DI üzerinden otomatik gelir; başlık/açıklama i18n anahtarları (`reports:catalog.attendance.monthly.{summary,closing}.{label,description}`) tr/en/de `reports.json`'a bu turda eklendi.
2. Gerçek zamanlı "izinli günde çalışma" tetiklemesi (yukarıdaki tasarım kararına bakın) - şu an yalnız toplu/ay-sonu.
3. `AttendanceMonthlyClosingReportProvider`'da onaylayan kullanıcının adı gösterilmiyor (yalnız `ApprovedByUserId` var, Identity'den ad çözümü bu turun kapsamı dışında bırakıldı).

## senseik-056 ile uzlaştırma (2026-09-19 devam turu)

`b/attendance-monthly-timesheet-approval` (senseik-056, dev'e merge edilmemiş, state `blocked`) aynı altyapıyı bağımsız olarak kurmuş: birebir aynı `MonthlyTimesheetApprovalConfiguration` ve `MonthlyTimesheetApprovalRepository`, aynı isimli `ApproveMonthlyTimesheetCommand` (aynı namespace, aynı dosya adı `MonthlyTimesheetApprovalFeatures.cs`), ve eski bir dev noktasından açıldığı için **tabloyu baştan oluşturan** bir migration (`20260918134954_MonthlyTimesheetApprovals`). Dev'de tablo zaten `PendingModelSync` ile var ama unique index yok — bu yüzden bu karttaki `20260919080107_MonthlyTimesheetApprovalUniqueIndex` (yalnız unique index ekler) dev'e göre doğru ve tek migration'dır; 056 rebase edilirken kendi `MonthlyTimesheetApprovals` migration'ını düşürmelidir (tabloyu ikinci kez yaratır).

Çakışmayı küçültmek için bu turda 029 tarafı 056'ya hizalandı: izin anahtarı `attendance.timesheet.close` → `attendance.timesheet.approve` (`AttendancePermissions.TimesheetApprove[Key]`), rota `employees/{id}/close-month` (body) → `monthly/employees/{id}/approve?periodMonth=` (query). 056 merge edilirken kalan uzlaştırma: `MonthlyTimesheetApprovalFeatures.cs` add/add çakışması (056'nın ızgara sorgusu + `ApproveAllMonthlyTimesheetsCommand` korunmalı, tekil onay handler'ı 029'unkini — `LeaveOverlapChecker` taraması içeren — kullanmalı), Manager rolüne `attendance.timesheet.approve` eklenmesi (056 `SystemRoleDefinitions.cs`), controller'da iki kez tanımlanan `ApproveMonthly` aksiyonu.

## Merge gate notu — "demo seed'de örnek Excel export"

Demo seed'de (`src/Sense.Api/SeedData/`) Attendance verisi yok (`DataSeeder.DemoLeave.cs` var, `DemoAttendance` yok). Raporlar yine de demo tenant'ta Excel üretir: her görünür çalışan için satır çıkar (puantaj alanları 0, kapatma raporunda Leave demo verisinden izin günleri dolar). Excel üretiminin kendisi `AttendanceLeaveCrossCheckAndReportingTests` (`MonthlySummaryReport_*`, `ClosingReport_*`) içinde gerçek Postgres + ClosedXML ile doğrulanıyor; API üzerinden demo tenant'ta elle çalıştırılmadı. Anlamlı demo çıktısı için bir `DataSeeder.DemoAttendance.cs` ayrı bir iş olarak eklenebilir.

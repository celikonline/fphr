# senseik-056 — "Aylık Puantaj" ızgarası + Tümünü Onayla

**Durum: review.** 19 Eylül 2026'da dev'in güncel ucu (senseik-029 dahil) üzerine rebase edildi ve 029 ile uzlaştırıldı. Dev'e merge edilmedi, push edilmedi.

## Kapsam (API/Application)

- `GET attendance/timesheet/monthly?periodMonth=&orgUnitId=` — `GetMonthlyTimesheetQuery`: görünür her çalışan için worked/missing/overtime (`IAttendanceMonthlySummaryReader`) + ücretli/ücretsiz/raporlu izin günü (`ILeaveDayReader`) + onay durumu; verisi olmayan çalışan sıfırlanmış satır alır. Görünürlük `attendance.timesheet.view` veri kapsamından.
- `POST attendance/timesheet/monthly/employees/{id}/approve?periodMonth=` — **senseik-029'un** `ApproveMonthlyTimesheetCommand` handler'ı (onaydan önce `LeaveOverlapChecker` ile izin çapraz kontrolü, Approve/Reapprove).
- `POST attendance/timesheet/monthly/approve-all?periodMonth=&orgUnitId=` — `ApproveAllMonthlyTimesheetsCommand`: görünür (veri kapsamı ∩ org birimi) çalışanlardan zaten onaylı olanları atlar, yeni onaylananlar için tek toplu Leave sorgusuyla 029'daki aynı izin çapraz kontrolünü çalıştırır, onaylanan sayıyı döner.
- `SystemRoleDefinitions`: Manager `attendance.timesheet.approve` alır (Team kapsamı); Timekeeper açıkça hariç (11.3.06 rol eşlemesi "approve yok" diyor, `attendance.*` joker'i aksi halde veriyordu).

## 029 ile uzlaştırma (rebase kararları)

| Parça | Karar |
|---|---|
| `20260918134954_MonthlyTimesheetApprovals` migration | DÜŞÜRÜLDÜ (dev tabloyu `PendingModelSync` ile, unique index'i `20260919080107_MonthlyTimesheetApprovalUniqueIndex` ile zaten kuruyor) |
| `MonthlyTimesheetApprovalRepository` / `MonthlyTimesheetApprovalConfiguration` / DI kaydı / snapshot | 029'un (dev) sürümü korundu, 056'nınkiler düşürüldü |
| `AttendancePermissions.TimesheetApprove` | 029'un tanımı (Türkçe metin) korundu; 056'nın kopyası düşürüldü |
| `ApproveMonthlyTimesheetCommand/Handler`, `TimesheetController.ApproveMonthly` | 029'unki korundu (ICommand<MonthlyTimesheetApprovalDto>, 200 + DTO); 056'nın void/204 sürümü düşürüldü |
| Izgara sorgusu, `ApproveAll`, `GET monthly`, `POST monthly/approve-all`, Manager grant | 056'dan korundu |

## Doğrulamada bulunan ve düzeltilen gerçek boşluklar

1. 029'un tekil onay handler'ı **veri kapsamı denetlemiyordu** (`[RequiresPermission]` yalnız izne bakar; Manager kendi ekibi dışındaki bir çalışanı onaylayabilirdi). `IDataScopeService.CanAccessEmployeeAsync(attendance.timesheet.approve, …)` eklendi, kapsam dışı → `EmployeeNotInScope` (403). Handler kurucusuna `IDataScopeService` eklendi.
2. 056'nın Tümünü Onayla'sı izin çapraz kontrolünü atlıyordu; artık 029'daki tekil onayla aynı taramayı yapıyor.
3. Izgara ve Tümünü Onayla'nın "görünür çalışan" kuralı tek yerde (`VisibleTimesheetEmployees`): ızgarada görünen satırlar = Tümünü Onayla'nın dokunduğu satırlar.
4. Timekeeper'a istenmeden geçen onay izni kaldırıldı (yukarıda).

## Kapsam dışı / açık

- **Web ekranı YOK.** Kartın kabul kriterleri yalnız API'dir; ızgara + "Onayla"/"Tümünü Onayla" butonları için ayrı bir web kartı gerekir (`web/src/pages/attendance/` altında ekran yok, `attendance.service.ts`'te istemci fonksiyonları yok).
- Tümünü Onayla zaten onaylı satırları yeniden taramaz/yeniden onaylamaz (kabul kriteri böyle); yeniden onay için tekil "Onayla" kullanılır.
- Tümünü Onayla dönemi kilitli/gelecek ay diye engellemiyor (029'daki tekil onayla aynı davranış).

## Doğrulama (dürüst durum)

Çalıştırıldı (Docker KAPALI, yerel dotnet 10):
- `dotnet build src/Sense.Api` ve `tests/Modules/Sense.Modules.Attendance.Tests` — 0 hata.
- `Sense.Modules.Attendance.Tests --filter "Category!=Integration"` — 241/241 yeşil; `MonthlyTimesheetApproval*` 18/18 (Domain + Features; ızgara, kapsam dışı 403, ilk onay/reapprove, izin çapraz kontrolü tekil + toplu, Manager kapsamı, org birimi kesişimi, hepsi zaten onaylıysa tarama yok).
- `Sense.Modules.Identity.Tests --filter Roles` — 17/17 (yeni: Manager/HR/Admin alır, Timekeeper/Employee almaz).
- `dotnet ef migrations has-pending-model-changes` (AttendanceDbContext) — değişiklik yok.

ÇALIŞTIRILMADI: `[Trait("Category","Integration")]` Testcontainers testleri (özellikle `AttendanceLeaveCrossCheckAndReportingTests`; onay handler'ı çağıran iki testin çağrısı yeni kurucu imzasına uyarlandı, derleniyor ama koşulmadı). Web'e dokunulmadı; web kapıları çalıştırılmadı.

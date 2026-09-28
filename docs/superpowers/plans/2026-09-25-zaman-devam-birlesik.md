# Zaman & Devam + İK eksikleri — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Bu projede kodu `takim-gelistirici-{dotnet,node}` yazar; "bitti" öncesi `takim-gozden-gecirici` + `node .claude/takim/betikler/test-kapisi.mjs`.

**Goal:** 11.10'daki Z0–Z4 + Z5.1/Z5.3/Z5.4 ve Ek A E1–E9'u, dalga/şerit modeliyle en yüksek ajan paralelliğinde teslim etmek.

**Architecture:** Dalga 0 tüm ortak şemayı/sözleşmeyi/iskeleti tek elden yazar; Dalga 1–2 şeritleri yalnız kendi yeni dosyalarına yazar; ana oturum ONAY alan şeritleri `a/zaman-devam`'a tek tek rebase eder. Ham okutma (`Punch`) gün hesabının kaynağı olur; `AttendanceDay` türetilmiş özet kalır.

**Tech Stack:** .NET 10 (xUnit v3, Shouldly, NSubstitute, Testcontainers, EF Core), React + Vite + Mantine 9 + Vitest, Expo + Jest.

**Spec:** `docs/superpowers/specs/2026-09-25-zaman-devam-birlesik-design.md` (K1–K9, §4 veri modeli, §5 kurallar, §7 sözleşmeler)

## Global Constraints

- API JSON: camelCase, enum'lar camelCase string (`JsonStringEnumConverter(CamelCase)`), null alanlar çıkmaz — web kodu eksik alanı null sayar.
- Modül handler'ları **Application** assembly'sinden `AddModuleHandlers` ile kaydedilir (Domain/Contracts de geçilir).
- Entegrasyon olayı: komut handler'ında `IIntegrationEventOutbox.Enqueue(...)`.
- Hata kodu ve izin adları `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx` içinde yerelleştirilir (iki dosyada aynı anahtar kümesi — `ErrorResourceParityTests`).
- Yeni izin anahtarları `SystemRoleDefinitions` include listelerine eklenir (`SystemRolePermissionSynchronizer` açılışta dağıtır); `attendance.*` HR/Timekeeper'a, `*.my.*` her role zaten gider.
- Her modül kendi DbContext + migration'ı; migration `dotnet-ef` ile SDK container'ında üretilir; tablo şeması modül adı.
- Web: yalnız Mantine 9 + Tailwind v4 (shadcn/Radix yok); i18n tr/en/de anahtar eşitliği testleri geçmeli; sıcak dosyalara **yalnız sona ekleme**.
- .NET komutu: `docker run --rm -v "$PWD":/src -v senseik-nuget:/root/.nuget/packages -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal --add-host=host.docker.internal:host-gateway -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet <args>` (`-p:UseAppHost=false` yok). Web: host `pnpm exec tsc --noEmit`, `pnpm exec eslint src`, `pnpm exec vite build`, `pnpm test`.
- Commit: husky hook'u için `dotnet` shim'li PATH (CLAUDE.md); mesaj `<type>: <açıklama>` + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- `main`'e doğrudan yazılmaz; şerit dalları `takim/<şerit-id>`, entegrasyon `a/zaman-devam`.
- Testleri silmek/atlamak/filtrelemek yasak.
- Derleme/test komutları slot semaforu üzerinden koşar (Task W0-s); slot sayısı `SENSE_BUILD_SLOTS`.
- Bordro/FM/aylık özet sonuçları değişmez (Task W0-a karakterizasyon testleri her şeritte yeşil kalmalı).

## Review Focus

1. **Gece vardiyası, gün sınırı:** 22:00–06:00 vardiyada 23:30 `In` + ertesi gün 05:50 `Out` → tek gün (vardiyanın başladığı `WorkDate`), `Incalculable` değil; 05:50 `Out` yanlış güne yazılmamalı. (Task B1)
2. **Tek/çift dışı okutma:** 3 okutma (`In,Out,In`), yön `Unknown` tek okutma, ilk kayıt `Out` → `Incalculable`, `WorkedMinutes = 0`, başka bayrak yok; günlük listede yalnız `incalculable`'da görünür. (B1, B4)
3. **Kilitli ay:** onaylı `MonthlyTimesheetApproval` ayında elle okutma ekleme/silme reddedilir ve yeniden hesaplama o günleri atlar; çözüm durumu/override yeniden hesapta korunur. (B1, B3, B4)
4. **Aynı okutmayı iki kez gönderme** (terminal yeniden denemesi, çift içe aktarma): tekillik (Tenant, Employee, OccurredAtUtc, Direction, Source) ihlali hata değil **idempotent yoksayma**; toplu uçta kabul/yoksayılan sayısı döner. (B1, T1)
5. **Vekalet süresi bitişi/geri alma:** `EndDate` gün sonunda vekilin yetkisi düşer; `RevokedAtUtc` sonrası vekil karar veremez; vekil kendi onayına vekil olamaz; geçmiş kararlar değişmez. (B7)
6. **Bordro dış PDF eşleşmesi:** aynı sicil no iki dosyada / sicil bulunamadı / işten ayrılmış çalışan → satır bazlı sonuç raporu, kalan dosyalar yüklenir. (B13)
7. **Çalışan filtresi boş sonuç:** E1 seçici boş küme ve "tümünü seç + hariç tut" birlikte `EmployeeSelection`'da doğru çözülür; hiç çalışan seçilmeden mesaj/etkinlik gönderilemez. (F1, B16)

---

## Dosya yapısı (yeni / sahiplik)

Şerit başına yalnız yeni dosyalar; sıcak dosyalar Dalga 0'da hazırlanır (bkz. Spec §3.1, §6).

Attendance (src/Modules/Attendance/Sense.Modules.Attendance.*):
- `Domain/Punches/{Punch,PunchSource}.cs` · `Domain/Shifts/{ShiftDefinition,ShiftDayType}.cs` · `Domain/PunchRequests/PunchRequest.cs` · `Domain/Shifts/ShiftPlanTemplate.cs` · `Domain/Rotations/{RotationPattern,RotationAssignment}.cs` · `Domain/Terminals/{Terminal,TerminalGroup,EmployeeTerminalCredential}.cs`
- `Application/Punches/*` (B1) · `Application/ShiftDefinitions/*` (B2) · `Application/Recalculation/*` (B3) · `Application/Daily/*` (B4) · `Application/PunchRequests/*` (B6) · `Application/Rotations/*` (B9) · `Application/ShiftTemplates/*` (B10) · `Application/Terminals/*` (T1/T2)
- `Api/Controllers/{DailyAttendanceController,ShiftDefinitionsController,PunchRequestsController,RotationsController,ShiftTemplatesController,MonthlyMatrixController,TerminalsController,TerminalIngestController}.cs`
- Test: `tests/Modules/Sense.Modules.Attendance.Tests/<Alan>/*Tests.cs`

---

# DALGA 0 — Sözleşme ve iskelet

### Task W0-s: Derleme slot semaforu + iş kartları (ana oturum)

**Files:**
- Create: `scripts/dev/slot.sh`, `scripts/dev/dotnet` (shim), `scripts/dev/pnpm-slot.sh`
- Create: `docs/takim/isler/<şerit-id>.json` (her şerit için, şablon `.claude/takim/is-karti.json`)

**Interfaces:**
- Produces: `scripts/dev/dotnet <args>` ve `scripts/dev/pnpm-slot.sh <args>` — `SENSE_BUILD_SLOTS` (varsayılan hesap: `max(2, min(nproc/4, (MemAvailableGB-8)/6))`) kilit dosyasından biri alınana kadar bekler, sonra komutu koşar.

- [ ] **Step 1: slot.sh yaz**

```bash
#!/usr/bin/env bash
# usage: slot.sh <komut...>  — SENSE_BUILD_SLOTS adet flock kilidinden birini alıp komutu koşar
set -euo pipefail
LOCK_DIR="${SENSE_LOCK_DIR:-/tmp/senseik-slots}"; mkdir -p "$LOCK_DIR"
mem_gb=$(awk '/MemAvailable/ {printf "%d", $2/1048576}' /proc/meminfo)
auto=$(( (mem_gb-8)/6 )); cpu=$(( $(nproc)/4 )); [ $auto -gt $cpu ] && auto=$cpu; [ $auto -lt 2 ] && auto=2
SLOTS="${SENSE_BUILD_SLOTS:-$auto}"
while true; do
  for i in $(seq 1 "$SLOTS"); do
    exec {fd}>"$LOCK_DIR/slot.$i"
    if flock -n "$fd"; then "$@"; exit $?; fi
    exec {fd}>&-
  done
  sleep 2
done
```

- [ ] **Step 2: dotnet shim'i yaz** (`scripts/dev/dotnet`, chmod +x)

```bash
#!/usr/bin/env bash
exec "$(dirname "$0")/slot.sh" docker run --rm -v "$PWD":"$PWD" -v senseik-nuget:/root/.nuget/packages \
  -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  --add-host=host.docker.internal:host-gateway -w "$PWD" mcr.microsoft.com/dotnet/sdk:10.0 dotnet "$@"
```

- [ ] **Step 3: Doğrula** — iki eşzamanlı çağrının slot aldığını gör

Run: `SENSE_BUILD_SLOTS=1 bash -c 'scripts/dev/slot.sh sleep 3 & sleep 0.5; time scripts/dev/slot.sh true; wait'`
Expected: ikinci komut ~2.5 sn bekler (`real` ≥ 2.5s).

- [ ] **Step 4: İş kartlarını üret** — spec §6'daki her şerit için `docs/takim/isler/<id>.json`, `durum: "hazir"`, kabul kriterleri = bu plandaki şerit görevinin test adları. (`.claude/takim/**` değiştirilmez, yalnız şablon okunur.)

- [ ] **Step 5: Commit** — `chore: derleme slot semaforu ve is kartlari`

---

### Task W0-a: Attendance şema temeli + karakterizasyon testleri

**Owner:** `takim-gelistirici-dotnet` · **Risk:** migration → insan onayı

**Files:**
- Create: `Domain/Punches/Punch.cs`, `Domain/Punches/PunchSource.cs`, `Domain/Shifts/ShiftDefinition.cs`, `Domain/Shifts/ShiftDayType.cs`, `Domain/PunchRequests/PunchRequest.cs`, `Domain/Shifts/ShiftPlanTemplate.cs`, `Domain/Rotations/RotationPattern.cs`, `Domain/Rotations/RotationAssignment.cs`, `Domain/Terminals/Terminal.cs`, `Domain/Terminals/TerminalGroup.cs`, `Domain/Terminals/EmployeeTerminalCredential.cs`
- Modify: `Domain/AttendanceDays/AttendanceAnomaly.cs` (`Incalculable = 1 << 10`; `AttendanceDaySource` + `Request`, `Terminal`; `AnomalyResolutionAdvisor` yeni bayrağı bilir), `Domain/Shifts/ShiftAssignment.cs` (`ShiftDefinitionId?`, `DayType`), `Domain/Repositories.cs` (yalnız sona: `IPunchRepository`, `IShiftDefinitionRepository`, `IPunchRequestRepository`, `IShiftPlanTemplateRepository`, `IRotationRepository`, `ITerminalRepository`), `Infrastructure/Persistence/AttendanceDbContext.cs` (DbSet + konfigürasyon + `AttendanceTables` sabitleri, sona), `Infrastructure/Persistence/Repositories.cs` (sona), `Infrastructure/Persistence/Migrations/*` (yeni migration + snapshot)
- Test: `tests/Modules/Sense.Modules.Attendance.Tests/Characterization/{MonthlySummaryCharacterizationTests,OvertimePayrollCharacterizationTests}.cs`, `tests/Modules/Sense.Modules.Payroll.Tests/AttendanceWorkedTimeCharacterizationTests.cs`, `tests/Modules/Sense.Modules.Attendance.Tests/Domain/PunchTests.cs`

**Interfaces:**
- Produces (Domain, spec §4 alan adlarıyla birebir):
  - `Punch.Create(Guid tenantId, Guid employeeId, DateTime occurredAtUtc, DateOnly workDate, PunchDirection direction, PunchSource source, string? sourceRef, Guid? terminalId, Guid? locationId, double? lat, double? lon, bool isManual, Guid? createdByUserId, string? reason) : Result<Punch>`; `Punch.SoftDelete(Guid userId, string reason, DateTime nowUtc) : Result`
  - `PunchDirection { In = 0, Out = 1, Unknown = 2 }` (mevcut enum'a `Unknown` eklenir); `PunchSource { Manual, Import, Device, SelfService, Request, Terminal }`
  - `ShiftType { Fixed, Flexible, Dynamic }`, `ShiftDayType { Work, WeeklyRest, RestDay, PublicHoliday }`
  - `IPunchRepository`: `Task<IReadOnlyList<Punch>> ListForDayAsync(Guid tenantId, Guid employeeId, DateOnly workDate, CancellationToken)`, `ListForRangeAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken)`, `Task<bool> ExistsAsync(Guid tenantId, Guid employeeId, DateTime occurredAtUtc, PunchDirection direction, PunchSource source, CancellationToken)`, `Add(Punch)`.

- [ ] **Step 1: Karakterizasyon testlerini yaz (mevcut davranışı sabitle)**

```csharp
// MonthlySummaryCharacterizationTests.cs — mevcut AttendanceDay.Record + IAttendanceMonthlySummaryReader çıktısı
[Fact]
public async Task Monthly_summary_matches_baseline_for_mixed_month()
{
    // Arrange: 5 gün — 09:00-18:00 (plan 09-18, mola 60), 09:30 giriş (geç), 17:00 çıkış (erken), 09:00-20:00 (FM 120), hiç kayıt yok (NoData)
    var days = TestDays.MixedMonth(employeeId, period: new DateOnly(2026, 8, 1));
    var reader = TestReaders.Summary(days);

    // Act
    var s = await reader.GetAsync(tenantId, employeeId, new DateOnly(2026, 8, 1), ct);

    // Assert — değerler bu test yazılırken mevcut kodla koşup gözlenen çıktıdan sabitlenir
    s.WorkedMinutes.ShouldBe(BaselineWorked);      // ör. 2 * 480 + 450 + 420 (koşup yazın)
    s.OvertimeMinutes.ShouldBe(BaselineOvertime);
    s.MissingMinutes.ShouldBe(BaselineMissing);
}
```

Aynı kalıpla: `OvertimePayrollCharacterizationTests` (`AttendanceOvertimePayrollReader`), Payroll'da `AttendanceWorkedTimeReaderAdapter` (mevcut `AttendanceWorkedTimeReaderAdapterTests` yanına). Baseline sayıları **önce mevcut kodla koşturup** sabit yazılır (bu testler davranış değiştirmeyecek).

- [ ] **Step 2: Karakterizasyon testlerini koştur — geçmeli (baseline)**

Run: `scripts/dev/dotnet test tests/Modules/Sense.Modules.Attendance.Tests --filter "FullyQualifiedName~Characterization"` ve Payroll eşleniği
Expected: PASS (henüz kod değişmedi)

- [ ] **Step 3: `PunchTests` yaz (kırmızı)**

```csharp
[Fact]
public void Create_rejects_future_timestamp_beyond_tolerance() { /* now+10dk → Error AttendanceDomainErrors.PunchInFuture */ }

[Fact]
public void SoftDelete_requires_reason_and_marks_deleted()
{
    var p = Punch.Create(tenant, emp, T("2026-09-01T06:00Z"), new(2026,9,1), PunchDirection.In, PunchSource.Manual, null, null, null, null, null, true, user, "unuttu").Value;
    p.SoftDelete(user, " ", now).IsFailure.ShouldBeTrue();
    p.SoftDelete(user, "hatalı giriş", now).IsSuccess.ShouldBeTrue();
    p.DeletedAtUtc.ShouldBe(now);
}
```

- [ ] **Step 4: Çalışmayı doğrula** — `dotnet test --filter PunchTests` → FAIL (`Punch` yok)

- [ ] **Step 5: Varlıkları, enum bayrağını, konfigürasyonları, repository'leri yaz** (spec §4 alanları; `Incalculable = 1 << 10`; tekillik indeksi `(TenantId, EmployeeId, OccurredAtUtc, Direction, Source)` filtre `DeletedAtUtc IS NULL`; `AnomalyResolutionAdvisor`'a `Incalculable` → "elle düzelt / yoksay" önerisi; hata kodları `AttendanceDomainErrors` sonuna + resx tr/en).

- [ ] **Step 6: Migration üret**

Run: `scripts/dev/dotnet ef migrations add AddPunchShiftDefinitionRotationTerminal --project src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure --startup-project src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure`
Expected: yeni migration + güncel snapshot; `Up` yalnız yeni tablo/sütun (mevcut veri bozulmaz; `ShiftAssignment.DayType` varsayılan `0`).

- [ ] **Step 7: Tümünü koştur** — `dotnet build Sense.slnx` + Attendance/Payroll/Analytics testleri: karakterizasyon + `PunchTests` PASS, `AttendanceIntegrationTests` (migration Testcontainers) PASS.

- [ ] **Step 8: Commit** — `feat(attendance): punch, shift definition, rotation, terminal semasi + karakterizasyon testleri`

---

### Task W0-b: Modüller arası kayıtlar ve sözleşmeler

**Owner:** `takim-gelistirici-dotnet`

**Files:**
- Create: `Attendance.Contracts/{IPunchRecorder,IAttendanceDayRecalculator,IDailyAttendanceQuery,IAttendanceDailyKpiReader}.cs`, `Attendance.Contracts/AttendanceTimeDtos.cs`, `Leave.Contracts/ILeaveOnDateReader.cs`
- Modify (yalnız sona ekleme): `Attendance.Contracts/AttendancePermissions.cs` (+`All`), `Payroll.Contracts/PayrollPermissions.cs`, `Workflow.Contracts/IApprovalRequestSummaryProvider.cs`, `Notifications.Contracts/NotificationKeys.cs`, `Notifications.Application/Catalog/NotificationCatalog.cs`, `Notifications.Infrastructure/Templates/*.sbn` (yeni anahtar × kanal × tr/en/de) + `labels.{tr,en,de}.json`, `Shared.Contracts/BackgroundJobs/JobTypes.cs`, `Shared.Infrastructure/BackgroundJobs/BackgroundJobSeeding.cs`, `Reporting.Contracts/IReportProvider.cs` (`ReportCategories.Time`, `ReportParameterKeys.Date`), `Identity.Application/Roles/SystemRoleDefinitions.cs`, `Attendance.Infrastructure/AttendanceServiceCollectionExtensions.cs` (boş kayıt noktaları), `src/Sense.Worker/Program.cs`, `SharedResource{,.en}.resx`
- Test: `Identity.Tests/Roles/SystemRoleCoverageTests` (mevcut, yeşil kalmalı), `Reporting.Tests/ReportProviderRegistryTests` (mevcut), `Notifications.Tests/TemplateRendererTests` (mevcut — her katalog girdisi için şablon)

**Interfaces — Produces** (tam imza; spec §7):

```csharp
// Sense.Modules.Attendance.Contracts
public enum PunchDirectionDto { In, Out, Unknown }
public enum PunchSourceDto { Manual, Import, Device, SelfService, Request, Terminal }
public enum DailyStatus { Incalculable, Arrived, Late, EarlyLeave, Absent, Overtime, Undertime, OnLeave }
public sealed record PunchInput(Guid TenantId, Guid EmployeeId, DateTime OccurredAtUtc, PunchDirectionDto Direction,
    PunchSourceDto Source, string? SourceRef, Guid? TerminalId, Guid? LocationId, double? Latitude, double? Longitude,
    bool IsManual, Guid? CreatedByUserId, string? Reason);
public sealed record PunchBatchResult(int Accepted, int Ignored);
public interface IPunchRecorder {
    Task<Result<Guid>> RecordAsync(PunchInput input, CancellationToken ct);
    Task<Result<PunchBatchResult>> RecordBatchAsync(IReadOnlyList<PunchInput> inputs, CancellationToken ct);
    Task<Result> SoftDeleteAsync(Guid tenantId, Guid punchId, Guid userId, string reason, CancellationToken ct);
}
public sealed record RecalculationStatusDto(Guid JobId, string State, int Total, int Done, string? Error);
public interface IAttendanceDayRecalculator {
    Task<Guid> EnqueueAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
    Task<RecalculationStatusDto?> GetStatusAsync(Guid tenantId, Guid jobId, CancellationToken ct);
    Task<int> RecalculateNowAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
}
public sealed record DailyAttendanceRowDto(Guid EmployeeId, string EmployeeName, string? OrgUnitName, DateOnly Date,
    TimeOnly? CheckIn, TimeOnly? CheckOut, int WorkedMinutes, int OvertimeMinutes, string[] Anomalies, string? LeaveType);
public interface IDailyAttendanceQuery {
    Task<PagedResult<DailyAttendanceRowDto>> ListAsync(Guid tenantId, DateOnly date, DailyStatus status, Guid? orgUnitId, bool includeSubUnits, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyDictionary<DailyStatus, int>> CountsAsync(Guid tenantId, DateOnly date, Guid? orgUnitId, bool includeSubUnits, CancellationToken ct);
}
public sealed record DailyAttendanceTotalsDto(DateOnly Date, int Planned, int Arrived, int Late, int EarlyLeave, int Absent, int OnLeave);
public sealed record EmployeeAttendancePeriodDto(Guid EmployeeId, int LateCount, int LateMinutes, int EarlyLeaveCount, int EarlyLeaveMinutes, int OvertimeMinutes, int PlannedShiftDays, int WorkedShiftDays);
public interface IAttendanceDailyKpiReader {
    Task<IReadOnlyList<DailyAttendanceTotalsDto>> DailyTotalsAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);
    Task<IReadOnlyList<EmployeeAttendancePeriodDto>> EmployeeTotalsAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);
}
// Sense.Modules.Leave.Contracts
public sealed record EmployeeLeaveOnDate(Guid EmployeeId, string LeaveTypeCode, bool IsPaid);
public interface ILeaveOnDateReader {
    Task<IReadOnlyList<EmployeeLeaveOnDate>> ListAsync(Guid tenantId, DateOnly date, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
}
```

İzin anahtarları (yeni, `AttendancePermissions`): `attendance.daily.view`, `attendance.punch.manage`, `attendance.shiftdefinition.view/manage`, `attendance.recalculate.run`, `attendance.punchrequest.manage`, `attendance.my.punchrequest`, `attendance.rotation.view/manage`, `attendance.shifttemplate.manage`, `attendance.terminal.view/manage`, `attendance.dashboard.view`; Payroll: `payroll.payslip.import`, `payroll.my.payslip` (my → herkes); Workflow: `workflow.delegation.manage`, `workflow.my.delegation`; Employee: `employee.notes.view/manage`; Notifications: `notifications.messages.send/view`, `notifications.messagegroups.manage`; Documents: `documents.sharedfiles.types.manage`. Onay tipleri: `"AttendancePunch"`, `"PayslipObjection"`.

- [ ] **Step 1: Kayıt testlerini yaz (kırmızı)** — `AttendancePermissionsTests.All_contains_every_declared_permission` (reflection ile `Permission` alanları ⊆ `All`), `NotificationCatalogTests.Every_new_key_has_templates_for_all_channels_and_languages`, `ApprovalRequestTypeCatalogTests` beklenen sayıyı **günceller** (yeni tipler `ApprovalRequestTypes.All`'a eklenir → mevcut test sayısı+2, sırası korunur).
- [ ] **Step 2: Koştur** → FAIL.
- [ ] **Step 3: Arayüzleri/DTO'ları, izinleri, anahtarları, şablonları, iş tipini, rapor kategorisini, resx (tr/en aynı kümeler) yaz.** Uygulamalar boş: `AttendanceServiceCollectionExtensions`'a `NotImplemented` fırlatan **geçici** `Stub*` kayıtları YOK — bunun yerine arayüzler yalnız Contracts'ta durur, DI kaydını uygulayan şerit ekler (aksi halde ölü kod).
- [ ] **Step 4: Koştur** — Identity/Notifications/Reporting/Workflow/Attendance testleri PASS, `dotnet build Sense.slnx` PASS.
- [ ] **Step 5: Commit** — `feat: zaman-devam sozlesmeleri, izin/bildirim/onay tipi kayitlari`

---

### Task W0-c: Web/mobil iskelet

**Owner:** `takim-gelistirici-node`

**Files:**
- Create: `web/src/services/{attendance-daily,attendance-shift-definitions,attendance-punch-requests,attendance-rotations,attendance-terminals,attendance-recalc,workflow-delegations,employee-notes,hr-messages,payslip-ack}.service.ts` (DTO tipleri W0-b C# sözleşmesinden, camelCase), `web/src/pages/attendance/{daily-list,time-dashboard,shift-definitions,rotations,monthly-matrix,punch-requests,terminals}.tsx` (boş iskelet + başlık), `web/src/pages/settings/delegations.tsx`, `web/public/locales/{tr,en,de}/{attendance-daily,shifts,rotations,terminals,delegations,messages}.json`
- Modify (yalnız sona): `web/src/App.tsx`, `web/src/config/navigation.ts`, `web/src/i18n.ts` (namespace listesi), `web/public/locales/{tr,en,de}/navigation.json`, `web/src/pages/reports.tsx` (PDF/Excel seçici, `format` parametresi), `apps/senseik-mobile/src/navigation/RootNavigator.tsx`, `apps/senseik-mobile/src/types/navigation.ts`
- Test: `web/src/config/navigation.test.ts`, `route-permissions.test.ts`, `i18n.locales.test.ts`, `i18n.raw-keys.test.ts` (mevcut — yeşil), `web/src/pages/reports.test.tsx` (yeni: format seçici)

**Interfaces — Produces:** Rotalar: `/app/attendance/daily/:status`, `/app/attendance/time-dashboard`, `/app/attendance/shift-definitions`, `/app/attendance/rotations`, `/app/attendance/monthly-matrix`, `/app/attendance/punch-requests`, `/app/attendance/terminals`, `/app/settings/delegations`. Servis fonksiyon adları: `listDaily(date,status,orgUnitId?,page?)`, `getDailyCounts(date,orgUnitId?)`, `addManualPunch(...)`, `deletePunch(id,reason)`, `listShiftDefinitions()`, `saveShiftDefinition(dto,{applyToPast})`, `createPunchRequest(dto)`, `listMyPunchRequests()`, `listRotations()`, `listTerminals()`, `listDelegations()`, `createDelegation(dto)`, `revokeDelegation(id)`. Bu adlar F-şeritlerinin tüketeceği sözleşmedir.

- [ ] **Step 1: Test yaz (kırmızı)** — `reports.test.tsx`: `it('PDF ve Excel biçim seçicisi format parametresini gönderir', …)` (`getByRole('radio',{name:'PDF'})` → `requestReport` çağrısında `parameters.format==='pdf'`).
- [ ] **Step 2: Koştur** → FAIL.
- [ ] **Step 3: İskeleti yaz** — her sayfa `PageHeader` + "yakında" boş durumu; rotalar `lazy`, `requireAppItem` ile menü; i18n üç dilde aynı anahtarlar.
- [ ] **Step 4: Koştur** — `pnpm exec tsc --noEmit`, `pnpm exec eslint src`, `pnpm test`, `pnpm exec vite build` PASS; mobil `pnpm --filter senseik-mobile test`.
- [ ] **Step 5: Commit** — `feat(web): zaman-devam iskeleti (rota, menu, servis tipleri, i18n)`

**Dalga 0 birleştirme sırası (ana oturum):** W0-s → W0-a → W0-b → W0-c; her birleştirmeden sonra `dotnet build Sense.slnx` + `pnpm exec tsc --noEmit`. **İnsan onayı noktası:** W0-a migration'ı.

---

# DALGA 1 — Geniş dalga (tümü aynı anda; her şerit kendi worktree'sinde `takim/<id>`)

Ortak şerit şablonu (her B/F şeridi): iş kartı oku → kırmızı test → koştur (FAIL) → minimal uygulama → koştur (PASS) → test kapısı → commit → `İNCELEMEYE HAZIR` → `takim-gozden-gecirici`. Aşağıda her şeridin **dosyaları, tüketim/üretim imzaları ve kabul testleri**.

### Task B1: Punch hattı + Incalculable + veri taşıma (Z0.1, Z0.4) — **riskli: bordro, migration → insan onayı**

**Files:**
- Create: `Application/Punches/{PunchRecorder,DayFromPunches,WorkDateResolver}.cs`, `Infrastructure/Migrations/AttendanceBackfillPunches` (veri migration'ı: yeni migration dosyası)
- Modify: `Application/AttendanceDays/AttendanceDayUpsert.cs`, `Application/DeviceLogs/DeviceLogImportFeatures.cs:81-82`, `Application/Import/AttendanceImportRowHandler.cs`, `Application/SelfCheckIns/SelfCheckInFeatures.cs`, `AttendanceServiceCollectionExtensions.cs` (`IPunchRecorder` kaydı, sona)
- Test: `tests/.../Punches/{DayFromPunchesTests,WorkDateResolverTests,PunchRecorderTests,BackfillMigrationTests}.cs`

**Interfaces:**
- Consumes: `IPunchRepository`, `AttendanceDay.Record(...)` (imza değişmez), `IWorkScheduleReadService`
- Produces: `IPunchRecorder` uygulaması; `DayFromPunches.Build(IReadOnlyList<Punch> punches, PlannedDay? plan) : DayResult(TimeOnly? CheckIn, TimeOnly? CheckOut, int ActualBreakMinutes, bool Incalculable)`

- [ ] **Step 1: Kırmızı testler**

```csharp
[Theory]
[InlineData("In,Out,In")]           // tek sayı
[InlineData("Out,In")]              // ilk kayıt Out
[InlineData("In,In")]               // yön bozuk
public void Incalculable_when_direction_sequence_invalid(string seq)
{
    var punches = Punches(seq, "09:00,12:00,13:00");
    var r = DayFromPunches.Build(punches, plan: Plan("09:00","18:00",60));
    r.Incalculable.ShouldBeTrue();
}

[Fact]
public void Night_shift_out_belongs_to_shift_start_day()
{
    var plan = Plan("22:00", "06:00", 30, startsPreviousDay: false);
    var day1In  = Punch(In,  "2026-09-01T23:30");
    var day2Out = Punch(Out, "2026-09-02T05:50");
    WorkDateResolver.Resolve(day1In,  new[]{ (new DateOnly(2026,9,1), plan) }).ShouldBe(new DateOnly(2026,9,1));
    WorkDateResolver.Resolve(day2Out, new[]{ (new DateOnly(2026,9,1), plan), (new DateOnly(2026,9,2), plan) })
        .ShouldBe(new DateOnly(2026,9,1));   // pencere: start-4sa..end+4sa; Eylül 2 22:00 penceresine 05:50 düşmez
}

[Fact]
public void Break_gap_between_out_in_adds_to_actual_break()
{   // In 09:00, Out 12:00, In 12:45, Out 18:00 → ActualBreakMinutes 45
    DayFromPunches.Build(Punches("In,Out,In,Out","09:00,12:00,12:45,18:00"), Plan("09:00","18:00",60)).ActualBreakMinutes.ShouldBe(45);
}

[Fact]
public async Task Duplicate_punch_is_ignored_not_error()
{
    var r = await recorder.RecordBatchAsync(new[]{ input, input }, ct);
    r.Value.ShouldBe(new PunchBatchResult(1, 1));
}

[Fact]
public async Task Locked_month_rejects_manual_punch()
{   // MonthlyTimesheetApproval var → RecordAsync(IsManual:true) Failure AttendanceErrors.PeriodLocked
}
```

- [ ] **Step 2: Koştur** → FAIL.
- [ ] **Step 3: Uygula** — `AttendanceDayUpsert` tüm giriş noktalarında (`Manual/Import/Device/SelfService`) önce `Punch` yazar, sonra günü `DayFromPunches`'tan türetip `AttendanceDay.Record`'a verir; `DeviceLogImportFeatures` `ins.Min/outs.Max` indirgemesini kaldırır (her satır bir `Punch`); `Incalculable` gün: `WorkedMinutes = 0`, tek bayrak; `AttendanceDay` `Source` en son punch kaynağı.
- [ ] **Step 4: Backfill migration + testi** — mevcut `AttendanceDay` (CheckIn/CheckOut) ve `SelfCheckIn` satırlarından `Punch` üret (`Source` günün kaynağına göre; `SelfCheckIn.Id` → `SourceRef`); Testcontainers testi: 3 örnek gün ekle → migration → yeniden hesaplanan `WorkedMinutes/Overtime/Anomalies` taşıma öncesiyle **aynı**.
- [ ] **Step 5: Karakterizasyon testleri (W0-a) hâlâ PASS**; Attendance + Payroll + Analytics tam koşu.
- [ ] **Step 6: Commit(ler)** — `feat(attendance): ham okutma hatti ve hesaplanamayan durumu`, `feat(attendance): mevcut gunleri okutmaya tasi`

### Task B2: ShiftDefinition kataloğu + plan çözümleme + toleranslar (Z0.2)

**Files:** Create `Application/ShiftDefinitions/{ShiftDefinitionFeatures,ShiftRuleEvaluator,DynamicShiftMatcher}.cs`, `Api/Controllers/ShiftDefinitionsController.cs` (`v1/attendance/shift-definitions`) · Modify `Application/AttendanceDays/AttendanceDayFeatures.cs` (`PlannedDayResolver`: K2 önceliği), `Domain/AttendanceDays/DailyWorkCalculator.cs` (toleranslar) · Test `.../ShiftDefinitions/*Tests.cs`
**Interfaces:** Consumes `IShiftDefinitionRepository`, `ShiftAssignment.ShiftDefinitionId`. Produces `PlannedDayResolver.Resolve(Guid employeeId, DateOnly date) : PlannedDay?` (ShiftAssignment → ShiftDefinition kuralları → WorkSchedule); `ShiftDefinitionChanged(Guid definitionId, DateOnly appliedFrom)` C# olayı (B3 dinler).

- [ ] Kırmızı testler:

```csharp
[Fact] public void Late_arrival_respects_tolerance()
{   // start 09:00, LateTolerance 10 → 09:10 geç değil, 09:11 geç
}
[Fact] public void Overtime_ignores_below_min_and_caps_at_max()
{   // LateDepartureOvertimeMin 15 max 60: 18:10 → FM 0; 18:30 → 30; 20:00 → 60
}
[Fact] public void Flexible_planned_end_is_actual_start_plus_duration()
{   // Flexible, süre 480, pencere 07:00-10:00, giriş 08:20 → planlanan bitiş 16:20
}
[Fact] public void Dynamic_picks_definition_with_closest_start_to_first_in()
{   // 06:00/14:00/22:00 vardiyaları, ilk giriş 13:40 → 14:00 vardiyası
}
[Fact] public void Assignment_with_rest_day_type_ignores_hours()
{   // DayType WeeklyRest → plan çalışma günü değil → okutma varsa UnplannedWork
}
[Fact] public async Task Save_and_apply_to_past_enqueues_recalculation_for_affected_range() { /* IAttendanceDayRecalculator.EnqueueAsync çağrısını doğrula */ }
```
- [ ] Koştur (FAIL) → uygula → koştur (PASS) → kapı → commit `feat(attendance): vardiya kataloğu ve tolerans kuralları`.

### Task B3: Yeniden hesaplama servisi + oto-kilit düzeltmesi (Z0.3, K7)

**Files:** Create `Application/Recalculation/{AttendanceDayRecalculator,RecalculationJob,RecalculationJobRepository}.cs`, `Infrastructure/Jobs/RecalculationHostedService.cs`, `Infrastructure/Jobs/ShiftPeriodAutoLockJobHandler.cs` (`IScheduledJobHandler`, `JobTypes.ShiftPeriodAutoLock`), `Api/Controllers/RecalculationController.cs` · Modify `AttendanceModule.cs` (handler kaydı, sona), `AttendanceServiceCollectionExtensions.cs` (sona) · Test `.../Recalculation/*Tests.cs`
**Interfaces:** Produces `IAttendanceDayRecalculator` uygulaması. `RecalculationJob` iş kaydı **tablo eklemeden** mevcut Hangfire/hosted-service kalıbıyla bellek+`JobTypes` kaydı ile izlenir (kalıcı tablo gerekirse W0-a migration'ına değil, B3'ün **kendi** migration'ına) — **karar:** kalıcı `RecalculationJob` tablosu **W0-a'da yok**; B3 durumu `IDistributedCache`/bellek yerine mevcut iş altyapısının sonuç alanında tutar; ilerleme kaybolabilir (yeniden başlatmada iş baştan kuyruklanır, hesap idempotent).
- [ ] Kırmızı testler: `Skips_days_in_approved_month`; `Preserves_resolved_state_and_override`; `Is_idempotent_when_run_twice`; `AutoLock_handler_dispatches_LockDueShiftPeriodsCommand` (handler `LockDueShiftPeriodsCommand`'ı dispatcher'a gönderir — NSubstitute ile doğrula); `Range_over_31_days_is_chunked_by_employee` (parça boyutu 200 çalışan).
- [ ] Uygula → PASS → kapı → commit `feat(attendance): yeniden hesaplama servisi ve otomatik ay kilidi isi`.

### Task B4: Günlük liste uç noktası (Z1.1)

**Files:** Create `Application/Daily/{DailyAttendanceQuery,DailyStatusFilter}.cs`, `Api/Controllers/DailyAttendanceController.cs` (`GET v1/attendance/daily`, `GET v1/attendance/daily/counts`, `POST v1/attendance/daily/punches`, `DELETE v1/attendance/daily/punches/{id}`), Leave: `Leave.Infrastructure/Lookups/LeaveOnDateReader.cs` + kayıt (sona) · Test `.../Daily/*Tests.cs`, `Leave.Tests/LeaveOnDateReaderTests.cs`
**Interfaces:** Consumes `IPunchRecorder` (elle ekle/sil), `ILeaveOnDateReader`, `IDataScopeService`. Produces `IDailyAttendanceQuery` uygulaması.
- [ ] Kırmızı testler (tablo §5.4): her `DailyStatus` için fixture'lı gün ve beklenen çalışan kümesi (`late` = `LateArrival`; `absent` = `NoData` **ve** onaylı izin yok; `onLeave` = `ILeaveOnDateReader`); `Every_list_matches_day_anomaly_flags` (aynı gündeki `AttendanceDay.Anomalies` ile küme eşitliği); `Manual_add_requires_reason_and_permission`; `Manual_add_in_locked_month_fails`; `Delete_writes_audit_entry`; `Counts_equal_list_totals`; `Respects_data_scope`.
- [ ] Uygula → PASS → kapı → commit `feat(attendance): gunluk liste ucu ve elle okutma`.

### Task B5: Zaman KPI'ları (Z1.3)

**Files:** Create `Attendance.Infrastructure/Lookups/AttendanceDailyKpiReader.cs` (kayıt sona), `Analytics.Application/Kpis/TimeKpiFeatures.cs`, `Analytics.Api/Controllers/TimeKpisController.cs` (`/analytics/kpis/attendance-7d`, `/late-early-by-unit`, `/overtime-by-group?groupBy=branch|unit|team|title`, `/shift-attendance-rate`) · Test `Analytics.Tests/TimeKpi*Tests.cs`, `Attendance.Tests/AttendanceDailyKpiReaderTests.cs`
**Interfaces:** Consumes `IAttendanceDailyKpiReader`, `IOrgUnitLookup`, `IDataScopeService`.
- [ ] Kırmızı testler: `Seven_day_totals_fill_missing_days_with_zero`; `Late_early_grouped_by_unit_uses_org_lookup`; `Overtime_grouped_by_title`; `Shift_attendance_rate_is_worked_over_planned_days`; `Empty_scope_returns_empty_not_error`.
- [ ] Uygula → PASS → kapı → commit `feat(analytics): zaman kpi uclari`.

### Task B6: Eksik kayıt talebi (Z2.1)

**Files:** Create `Application/PunchRequests/{PunchRequestFeatures,PunchRequestApprovalHandlers,PunchRequestSummaryProvider}.cs`, `Api/Controllers/PunchRequestsController.cs` (`POST/GET v1/attendance/punch-requests/me`, `POST .../{id}/cancel`, yönetici `GET v1/attendance/punch-requests`) · Modify `Attendance.Application.csproj` (Workflow.Contracts referansı), `AttendanceServiceCollectionExtensions.cs` `AddAttendanceIntegrationEventHandlers` (sona) · Test `.../PunchRequests/*Tests.cs`
**Interfaces:** Consumes `IWorkflowService.StartAsync("AttendancePunch", subjectId, requesterEmployeeId, …)`, `IPunchRecorder.RecordAsync` (Source = Request). Ödünç örnek: `Expense…ExpenseClaimApprovalHandlers`.
- [ ] Kırmızı testler: `Create_starts_workflow_with_AttendancePunch_type`; `Approved_creates_punch_with_Request_source_and_recalculates`; `Rejected_sets_status_rejected_no_punch`; `Cannot_request_future_time`; `Cannot_request_in_locked_month`; `Cancel_only_when_pending`; `Duplicate_pending_same_time_rejected`.
- [ ] Uygula → PASS → kapı → commit `feat(attendance): eksik kayit talebi ve onay akisi`.

### Task B7: Vekalet (Z2.2)

**Files:** Create `Workflow.Domain/Delegations/ApprovalDelegation.cs`, `Workflow.Application/Delegations/{DelegationFeatures,DelegationResolver}.cs`, `Workflow.Api/Controllers/DelegationsController.cs` (`v1/workflow/delegations`), Workflow migration · Modify `Workflow.Domain/Repositories.cs` (`ListInboxTasksAsync` vekalet kapsamı), `Workflow.Application/ApprovalRequests/ApprovalRequestCommands.cs:245` (yetki kontrolü), `ApprovalRequestQueries.cs` (`ListInboxHandler`), e-posta karar yolu · Test `Workflow.Tests/Delegations/*Tests.cs`
**Interfaces:** Produces `DelegationResolver.ActiveDelegatorsFor(Guid tenantId, Guid delegateEmployeeId, DateOnly today, string requestType) : IReadOnlyList<Guid>`; karar kaydı `DecidedOnBehalfOfEmployeeId?`.
- [ ] Kırmızı testler (Review Focus 5): `Delegate_sees_delegator_inbox_within_range`; `Delegation_expires_at_end_of_EndDate`; `Revoked_delegation_blocks_decision`; `Delegate_decision_recorded_on_behalf_of`; `Cannot_delegate_to_self`; `Delegate_cannot_be_delegated_from_delegate_chain` (zincir yok); `RequestTypes_filter_limits_scope`; `Past_decisions_unchanged_after_revoke`.
- [ ] Uygula → PASS → kapı → commit `feat(workflow): vekalet`.

### Task B8: Takvim toplu işlemleri (Z3.1)

**Files:** Modify `Application/Shifts/ShiftAssignmentFeatures.cs` (yeni komutlar dosya sonuna: `BulkAssignShiftsCommand`, `BulkClearShiftsCommand`, `ListUnassignedEmployeesQuery`, `GetShiftPlanTotalsQuery`), `Api/Controllers/ShiftsController.cs` (sona 4 aksiyon) · Test `.../Shifts/BulkShiftTests.cs`
**Interfaces:** Consumes `ShiftAssignment.ShiftDefinitionId/DayType`, `ShiftPlanLockGuard`.
- [ ] Kırmızı testler: `Bulk_assign_skips_locked_period_and_reports_skipped`; `Bulk_assign_overwrites_draft_not_published_without_flag`; `Unassigned_lists_employees_without_assignment_in_range`; `Totals_sum_shift_minutes_per_day_and_employee`; `Day_type_rest_day_stores_no_hours`.
- [ ] Uygula → PASS → kapı → commit `feat(attendance): takvim toplu atama, gun tipleri, vardiyasizlar`.

### Task B9: Rotasyon (Z3.3, 11.7 #47)

**Files:** Create `Application/Rotations/{RotationFeatures,RotationExpander,RotationPresets}.cs`, `Api/Controllers/RotationsController.cs` · Test `.../Rotations/*Tests.cs`
**Interfaces:** Produces `RotationExpander.Expand(RotationPattern p, DateOnly start, int offsetDays, DateOnly from, DateOnly to) : IReadOnlyList<(DateOnly Date, Guid? ShiftDefinitionId, ShiftDayType DayType)>`; hazır kalıp fabrikaları `RotationPresets.TwoByTwelve/ThreeByEight/FourBySix(Guid dayShiftId, Guid nightShiftId)`.
- [ ] Kırmızı testler: `TwoByTwelve_alternates_day_night_rest_rest` (4 günlük döngü: gündüz, gece, dinlenme, dinlenme); `Offset_shifts_cycle_start`; `ThreeByEight_cycle_length_is_3_shifts_plus_rest`; `Apply_creates_draft_assignments_skipping_locked_months`; `Overlapping_assignments_for_employee_rejected`; `EndDate_stops_expansion`.
- [ ] Uygula → PASS → kapı → commit `feat(attendance): rotasyon kaliplari ve atama`.

### Task B10: Plan şablonu + Excel içe aktarma (Z3.2)

**Files:** Create `Application/ShiftTemplates/ShiftTemplateFeatures.cs`, `Application/Import/{ShiftPlanImportTemplate,ShiftPlanImportRowHandler}.cs`, `Api/Controllers/ShiftTemplatesController.cs` · Modify `Api/AttendanceModule.cs:50` çevresi (`IImportTemplate` kaydı, sona) · Test `.../ShiftTemplates/*Tests.cs`
**Interfaces:** Consumes `IImportTemplate`/`IImportRowHandler` (`Shared.Contracts/Import/ImportContracts.cs`), örnek `AttendanceImportTemplate.cs`.
- [ ] Kırmızı testler: `Template_roundtrip_apply_reproduces_entries`; `Import_row_with_unknown_shift_code_fails_row_only`; `Import_respects_locked_period`; `Template_workbook_has_shift_code_dropdown`.
- [ ] Uygula → PASS → kapı → commit `feat(attendance): plan sablonu ve excel ice aktarma`.

### Task B11: Aylık puantaj matrisi (Z3.4)

**Files:** Create `Application/MonthlyTimesheet/MonthlyMatrixQuery.cs`, `Api/Controllers/MonthlyMatrixController.cs` (`GET v1/attendance/monthly-matrix?period&orgUnitId`) · Test `.../MonthlyTimesheet/MonthlyMatrixTests.cs`
**Interfaces:** Produces `MonthlyMatrixDto(IReadOnlyList<MatrixRowDto> Rows, bool IsLocked)`, `MatrixCellDto(DateOnly Date, string Code)`; `Code` ∈ `absent|missing|overtime|full|weeklyRest|restDay|paidLeave|unpaidLeave|otherLeave|publicHoliday|incalculable|none`.
- [ ] Kırmızı testler: her kod için fixture; `Locked_period_flag_matches_approval`; `Scope_applied`.
- [ ] Uygula → PASS → kapı → commit `feat(attendance): aylik puantaj matrisi sorgusu`.

### Task B12: Bordro onay/itiraz (E2) — **riskli: para**

**Files:** Create `Payroll.Application/Payslips/{PayslipAcknowledgementFeatures,PayslipObjectionApprovalHandlers,PayslipReminderJob}.cs`, `Payroll.Api/Controllers/MyPayslipsController.cs` (`GET v1/payroll/me/payslips`, `POST .../{id}/acknowledge`, `POST .../{id}/object`), Payroll migration · Modify `Payroll.Domain/Payslips/Payslip.cs` (`PublishedAt?`, `AcknowledgedAt?`, `AcknowledgedFromIp?`, `ObjectionReason?`, `ObjectionStatus`), `PeriodTransitionCommands.cs` (ödenince `PublishedAt`) · Test `Payroll.Tests/Payslips/*Tests.cs`
- [ ] Kırmızı testler: `Acknowledge_sets_timestamp_and_ip_once`; `Acknowledge_before_publish_fails`; `Object_starts_PayslipObjection_workflow`; `Objection_accepted_marks_status_and_notifies_payroll_role`; `Reminder_targets_only_unacknowledged_published`; `Employee_cannot_read_others_payslip`.
- [ ] Uygula → PASS → kapı → **insan onayı (para)** → commit `feat(payroll): bordro onay ve itiraz`.

### Task B13: Dış bordro PDF dağıtımı + doküman tipleri (E3, E8)

**Files:** Create `Documents.Application/PayslipImports/{PayslipBulkImportFeatures,PayslipFileMatcher}.cs`, `Documents.Application/SharedFiles/{SharedFileTypeFeatures,SharedFileZipDownload}.cs`, `Documents.Domain/SharedFiles/SharedFileType.cs`, `Api/Controllers/{PayslipImportsController,SharedFileTypesController}.cs`, Documents migration · Modify `SharedCompanyFile.cs` (`TypeId?`, `VisibleOnMobile`), `SharedCompanyFilesController.cs` (sona) · Test `Documents.Tests/{PayslipImports,SharedFiles}/*Tests.cs`
**Interfaces:** Consumes `IFileStorage`, `IEmployeeDirectory`, `ISystemDocumentDeliveryService`. Dosya adı kalıbı `<sicil>_<YYYY-MM>.pdf` (K8).
- [ ] Kırmızı testler (Review Focus 6): `Matches_by_registration_number`; `Duplicate_registration_files_reported_not_imported`; `Unknown_registration_reported`; `Terminated_employee_reported`; `Zip_with_non_pdf_skipped_with_reason`; `Result_report_lists_every_file`; `Shared_file_mobile_flag_filters_mobile_list`; `Zip_download_respects_visibility_and_roles`.
- [ ] Uygula → PASS → kapı → commit `feat(documents): dis bordro toplu yukleme ve dokuman tipleri`.

### Task B14: Etkinlik zenginleştirme (E4 backend)

**Files:** Create `Announcements.Domain/{CompanyEventType,CompanyEventInvitee}.cs` (katalog varlığı: `CompanyEventCategory` renk+ad), `Application/CompanyEvents/{EventCategoryFeatures,EventInvitationFeatures}.cs`, Announcements migration (**`Migrations/` klasörü `Persistence/` dışında**) · Modify `CompanyEvent.cs` (`StartsAt`/`EndsAt` `DateTime?`, `IsAllDay`, `Mode` Onsite/Online, `Location`, `OnlineUrl`, `NotifyInvitees`, `CategoryId?`; `EventDate` korunur ve `StartsAt`'tan türetilir), `CompanyEventCalendar.cs`, `CompanyEventsController.cs` · Test `Announcements.Tests/*`
- [ ] Kırmızı testler: `Legacy_event_migrates_to_all_day_with_same_date`; `Online_requires_url_onsite_requires_location`; `EndsAt_must_not_precede_StartsAt`; `Invitees_notified_when_flag_set`; `Calendar_returns_events_overlapping_range`; `Birthday_and_leave_derivation_unchanged` (mevcut).
- [ ] Uygula → PASS → kapı → commit `feat(announcements): etkinlik zenginlestirme`.

### Task B15: Onboarding zenginleştirme (E5 backend)

**Files:** Create `Onboarding.Domain/Tasks/{OnboardingCategory,OnboardingTemplateSet}.cs`, `Application/Tasks/{CategoryFeatures,TemplateSetFeatures,StartProcessCommand}.cs`, Onboarding migration · Modify `OnboardingTaskTemplate.cs` (`CategoryId?`, `Importance`, `RequiresEDocument`), `OnboardingController.cs`, `OnboardingMaterializer` · Test `Onboarding.Tests/*`
- [ ] Kırmızı testler: `Start_from_named_set_creates_tasks_grouped_by_category`; `Category_progress_counts_done_over_total_excluding_waived`; `Existing_templates_without_category_still_materialize`; `Cannot_start_same_set_twice_for_employee`.
- [ ] Uygula → PASS → kapı → commit `feat(onboarding): kategori, onem, isimli sablonlar`.

### Task B16: İK mesajları (E6 backend) + SMS arayüzü

**Files:** Create `Notifications.Domain/HrMessages/{HrMessage,HrMessageRecipient,MessageGroup}.cs`, `Application/HrMessages/*`, `Api/Controllers/{HrMessagesController,MessageGroupsController}.cs`, `Shared.Contracts/Notifications/ISmsSender.cs`, `Shared.Infrastructure/Notifications/NullSmsSender.cs`, Notifications migration · Test `Notifications.Tests/HrMessages/*`
**Interfaces:** Consumes `IEmployeeDirectory.ResolveSelectionAsync(EmployeeSelection)`, `IEmailSender`, in-app `UserNotification`. Kanal `sms` istenirse `Result.Failure(NotificationErrors.SmsChannelUnavailable)` (K5).
- [ ] Kırmızı testler: `Empty_recipient_selection_rejected`; `Group_snapshot_resolves_at_send_time`; `Sms_channel_rejected_when_null_sender`; `Inbox_and_outbox_scope_by_user`; `Importance_high_marks_delivery_priority`; `Content_over_4096_rejected`.
- [ ] Uygula → PASS → kapı → commit `feat(notifications): ik mesajlari ve mesaj gruplari`.

### Task B17: Çalışan notları (E7 backend)

**Files:** Create `Employee.Domain/Employees/EmployeeNote.cs`, `Application/Notes/*`, `Api/Controllers/EmployeeNotesController.cs`, Employee migration · Modify `EmployeeDbContext.cs` (sona) · Test `Employee.Tests/EmployeeNoteTests.cs`
**Interfaces:** Ek dosya deseni: `EmployeePhoto` + `IFileStorage`.
- [ ] Kırmızı testler: `Only_notes_permission_holders_can_read` (`*.my.*` okuyamaz); `Title_max_255_content_max_512`; `Attachment_upload_confirm_flow`; `Author_recorded`; `Filter_by_importance_and_employee`.
- [ ] Uygula → PASS → kapı → commit `feat(employee): calisan notlari`.

### Task B18: E1 backend — EmployeeFilter genişletme

**Files:** Modify `Employee.Contracts/EmployeeDirectoryContracts.cs` (`EmployeeFilter`'a **sonda opsiyonel** `IReadOnlyList<Guid>? OrgUnitIds = null, bool IncludeTerminated = false`), `Employee.Application/Employees/EmployeeSelectionResolver.cs`, `EmployeeQueries.cs`, `Employee.Infrastructure/Lookups/EmployeeDirectory.cs`, `EmployeeApiModels.cs` · Test `Employee.Tests/EmployeeSelectionResolverTests.cs`
- [ ] Kırmızı testler: `OrgUnitIds_union_with_subunits`; `Terminated_excluded_by_default`; `Select_all_with_exclusions`; `Empty_filter_result_is_empty_not_all` (Review Focus 7); mevcut çağrılar (yeni alanlar null) davranışı aynı.
- [ ] Uygula → PASS → kapı → commit `feat(employee): filtre coklu birim ve ayrilanlar`.

### Task B19: E9 backend

**Files:** Modify `Identity.Application/Users/CreateUser.cs` / `EmployeeInvitationCommands.cs` (`SendCredentials` bayrağı yalnız e-posta; SMS yok — K5) · Test `Identity.Tests/*`
- [ ] Kırmızı testler: `Invitation_sent_when_flag_true`; `No_invitation_when_flag_false`; `Missing_work_email_falls_back_to_personal_or_fails_clearly`.
- [ ] Uygula → PASS → kapı → commit `feat(identity): yeni calisana giris bilgisi anahtari`.

---

### Web / mobil şeritleri (node) — W0-c servis sözleşmesine göre

Ortak: her şerit yalnız kendi sayfa/bileşen dosyalarına yazar (`web/src/pages/attendance/<sayfa>.tsx`, `web/src/components/<alan>/*`), test `*.test.tsx` yanında; sıcak dosyalara dokunmaz (W0-c hazırladı). Her birinde: `pnpm exec tsc --noEmit`, `eslint src`, `pnpm test`, `vite build` PASS.

| Şerit | Dosyalar (Create) | Kabul testleri (Vitest) |
|---|---|---|
| **F1** E1 | `components/employees/{employee-multi-picker,employee-filter-panel}.tsx` (+ test) | `emits_EmployeeSelection_with_ids`; `select_all_with_exclusions_emits_filter_and_excluded`; `keeps_selected_when_filter_changes`; `terminated_toggle_sets_includeTerminated`; `empty_result_disables_confirm` |
| **F2** Z1.2 | `pages/attendance/daily-list.tsx`, `components/attendance/daily-grid.tsx` | `renders_status_from_route_param`; `date_navigation_updates_query`; `counts_badges_match_service`; `manual_punch_requires_reason`; `delete_punch_confirms_and_sends_reason`; `locked_month_error_shown` |
| **F3** Z1.3 | `pages/attendance/time-dashboard.tsx`, `components/attendance/time-kpi-*.tsx` | `seven_day_chart_renders_zero_days`; `group_by_switch_refetches`; `empty_scope_shows_empty_state` |
| **F4** Z0.2 | `pages/attendance/shift-definitions.tsx`, `components/attendance/{shift-timeline,shift-definition-form}.tsx` | `timeline_snaps_to_15_minutes`; `overnight_shift_wraps_display`; `multiple_breaks_editable`; `save_and_apply_past_calls_service_with_flag`; `flexible_hides_fixed_end` |
| **F5** Z3.1 | `pages/attendance/shift-plan.tsx` (mevcut, **tek sahibi F5**), `components/attendance/{shift-palette,shift-calendar-grid}.tsx` | `drag_palette_shift_to_cell_assigns`; `day_type_cell_clears_hours`; `bulk_select_range_and_assign`; `unassigned_filter_lists_employees`; `footer_totals_sum_per_day`; `locked_period_disables_edit` |
| **F6** Z3.3 | `pages/attendance/rotations.tsx` | `preset_prefills_steps`; `apply_shows_skipped_locked_months`; `overlap_error_displayed` |
| **F7** Z3.2 | `components/attendance/shift-template-dialog.tsx` (F5'in sayfasına buton eklemesi **F5'ten sonra**, sona) | `save_template_from_selection`; `apply_template_to_range`; `excel_import_shows_row_errors` |
| **F8** Z3.4 | `pages/attendance/monthly-matrix.tsx`, `components/attendance/matrix-legend.tsx` | `cell_colors_map_from_code`; `legend_lists_all_codes`; `locked_badge_shown` |
| **F9** Z2.1 (web+mobil) | `pages/attendance/punch-requests.tsx`, `components/attendance/punch-request-form.tsx`; mobil `screens/app/PunchRequestScreen.tsx`, `services/PunchRequestService.ts`, `__tests__/screens/PunchRequestScreen.test.tsx` | web/mobil: `future_time_blocked`; `submit_calls_service`; `status_chip_updates`; `cancel_only_pending` |
| **F10** Z2.2 | `pages/settings/delegations.tsx` | `end_before_start_invalid`; `cannot_pick_self`; `revoke_calls_service`; `active_badge_by_date` |
| **F11** E2 (web+mobil) | `components/payroll/{my-payslips,payslip-acknowledge}.tsx`; mobil `PayrollScreen.tsx` (stub → gerçek), `services/PayslipService.ts` | `acknowledge_disabled_after_done`; `object_requires_reason`; `list_shows_ack_status`; mobil aynı |
| **F12** E3+E8 | `pages/payroll/payslip-import.tsx`, `pages/shared-files.tsx` (mevcut, tek sahibi F12) tip/mobil/zip eklemeleri | `import_result_table_lists_each_file`; `type_filter`; `mobile_toggle_saved`; `zip_download_button_uses_selection` |
| **F13** E5+E7 | `pages/onboarding.tsx` (mevcut, tek sahibi F13), `components/employees/{employee-onboarding-card,employee-notes-card}.tsx` | `start_process_picks_named_set`; `category_progress_rendered`; `notes_hidden_without_permission`; `note_attachment_upload` |
| **F14** E9 | `components/ui/form-modal.tsx` (mevcut; **yalnız yeni opsiyonel `onSaveAndNew` prop**), `pages/employees/employee-create*.tsx` giriş bilgisi anahtarı | `save_and_new_resets_form_keeps_modal`; `omitting_prop_renders_as_before`; `credentials_switch_sent_in_payload` |

# DALGA 2 — Bağımlı işler

### Task R1 / R2: Zaman raporları (Z4.1)

**Files:** Create (her biri) `Attendance.Infrastructure/Reporting/<Ad>ReportProvider.cs`; R1: `Incalculable`, `NoShiftAssigned`, `DailyStatusLists`; R2: `ManualEntries`, `CurrentlyInside`, `Breaks`, `IntermediatePunches` · Modify `AttendanceServiceCollectionExtensions.cs` (sona, satır başına 1 provider), `AttendanceReportPdfExport.cs` (genelleştir — **yalnız R1 dokunur**, R2 R1 birleşince rebase), `SharedResource*.resx` `report.sheet/col.*`, `web/public/locales/*/reports.json` (`attendance` düğümü altında rapor başına alt anahtar) · Test `Attendance.Tests/Reporting/*Tests.cs`, `ReportProviderRegistryTests` (anahtar tekilliği)
- [ ] Kırmızı testler (her rapor): `Excel_has_expected_columns`; `Pdf_when_format_pdf`; `Scope_and_date_param_respected`; `Empty_result_produces_header_only_file`.
- [ ] Commit `feat(attendance): zaman raporlari (R1|R2)`.

### Task T1: Terminal ve okutma ucu (Z5.1) — **riskli: auth → security-reviewer + insan onayı**

**Files:** Create `Application/Terminals/{TerminalFeatures,TerminalApiKeyService,TerminalPunchIngest}.cs`, `Api/Controllers/{TerminalsController,TerminalIngestController}.cs` (`POST v1/attendance/terminals/punches`, başlık `X-Terminal-Key`), `Api/Authentication/TerminalKeyAuthenticationHandler.cs`, rate-limit politikası · Test `.../Terminals/*Tests.cs`
**Interfaces:** Consumes `IPunchRecorder.RecordBatchAsync` (Source = Terminal), `EmployeeTerminalCredential` (kart/PIN → çalışan). Anahtar: 32 bayt rastgele, **yalnız SHA-256 hash + prefix saklanır**, ham anahtar oluşturmada bir kez döner.
- [ ] Kırmızı testler (Review Focus 4): `Key_shown_once_and_only_hash_stored`; `Invalid_key_returns_401_without_tenant_info`; `Terminal_key_scopes_to_its_tenant_only` (başka tenant çalışanı → reddedilir); `Batch_idempotent_retry_returns_ignored_count`; `Unknown_credential_reported_per_item`; `Inactive_terminal_rejected`; `Rate_limit_returns_429`; `Heartbeat_updates_LastSeen`.
- [ ] Uygula → PASS → kapı → `security-reviewer` → **insan onayı** → commit `feat(attendance): terminal ve okutma gonderme ucu`.

### Task T2: Toplu kart/kimlik (Z5.4)
**Files:** Create `Application/Terminals/CredentialImport*.cs` (`IImportTemplate`), `CredentialsController.cs` · Test: `Duplicate_credential_value_rejected_per_tenant`; `Import_row_unknown_employee_fails_row_only`; `Kind_Pin_hashed_at_rest`. Commit `feat(attendance): toplu kart kimlik tanimlama`.

### Task T3: Canlı monitör (Z5.3)
**Files:** Modify `Application/SelfCheckIns/LiveAttendanceBoardFeatures.cs` (terminal bilgisi + son okutma kaynağı ekle, geriye uyumlu), `LiveBoardController.cs` (sona: `GET board/terminals`) · Test `LiveBoardTests`: `Inside_derived_from_last_punch_direction`; `Terminal_offline_when_LastSeen_older_than_5_min`. Commit `feat(attendance): canli monitor terminal durumu`.

### Task F15: Terminal web (Z5)
**Files:** `pages/attendance/terminals.tsx`, `components/attendance/{terminal-form,credential-import}.tsx` · Testler: `api_key_shown_once_with_copy`; `offline_badge`; `credential_import_result_table`. Commit `feat(web): terminal ekranlari`.

### Task F16: Etkinlik web (E4) — bağımlı: F1, B14
**Files:** `pages/company-calendar.tsx` (mevcut, tek sahibi F16), `components/announcements/event-form-panel.tsx` (sağdan Drawer, "Oluştur ve Yeni" F14 prop'u) · Testler: `online_requires_url`; `invitees_from_multi_picker`; `time_range_validation`; `category_color_in_calendar`. Commit `feat(web): etkinlik zenginlestirme`.

### Task F17: İK mesajları web (E6) — bağımlı: F1, B16
**Files:** `pages/messages/{inbox,outbox,groups,compose}.tsx` (rota W0-c'de `/app/messages`) · Testler: `sms_channel_disabled_coming_soon`; `compose_requires_recipients`; `group_edit_uses_picker`; `content_counter_4096`. Commit `feat(web): ik mesajlari`.

# DALGA 3 — Entegrasyon

### Task I1: Birleşik doğrulama
- [ ] `a/zaman-devam` üzerinde `node .claude/takim/betikler/test-kapisi.mjs` (argümansız) → YEŞİL.
- [ ] Karakterizasyon testleri (W0-a) PASS; B1 backfill Testcontainers testi PASS.
- [ ] Performans: 1000 çalışan × 31 gün `RecalculateNowAsync` süresi ölçülüp `docs/takim/handoff/perf-recalc.md`'ye yazılır (hedef < 60 sn tek slotta; aşılırsa parça boyutu ayarlanır — kabul kriteri değil, rapor).
- [ ] `takim-gozden-gecirici` tüm dal için ONAY.

### Task I2: Belgeler ve PR
- [ ] `faz*-durum-takibi.md`, `11.7-yol-haritasi.md` (#47 kapandı), `11.10` durum satırı ("uygulandı", commit'ler) — **koda göre doğrulanarak**.
- [ ] `superpowers:finishing-a-development-branch` → yalnız "Push and Create PR" (`a/zaman-devam` → `dev`); PR açıklaması sonuna `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

---

## Self-Review

- **Spec kapsamı:** K1 → B2 (`Dynamic_picks…`, `Flexible…`); K2 → B2 `PlannedDayResolver`; K3 → B18/F1; K4 → B12 (`Acknowledge_sets_timestamp_and_ip_once`); K5 → B16/B19; K6 → B1/B3/B4/B6 kilit testleri; K7 → B3; K8 → B13; K9 → B7. Z0.1–Z0.4 → W0-a/B1/B2/B3; Z1 → B4/B5/F2/F3; Z2 → B6/B7/F9/F10; Z3 → B8–B11/F4–F8; Z4.1 → R1/R2; Z5.1/5.3/5.4 → T1/T3/T2/F15; E1 → B18/F1; E2 → B12/F11; E3+E8 → B13/F12; E4 → B14/F16; E5 → B15/F13; E6 → B16/F17; E7 → B17/F13; E9 → B19/F14. Z5.2 bilinçli kapsam dışı (spec §1).
- **Yer tutucu taraması:** B3'teki iş-durumu saklama kararı açıkça yazıldı (kalıcı tablo yok, idempotent yeniden kuyruklama); "TBD" yok. Baseline sayıları W0-a Step 1'de mevcut kodla koşup sabitlenir (kasıtlı — sayılar koddan gelir).
- **Tip tutarlılığı:** `IPunchRecorder`, `PunchInput`, `DailyStatus`, `RecalculationStatusDto`, `PlannedDayResolver.Resolve` adları W0-b tanımıyla şeritlerde aynı; `RequestType` dizgeleri `"AttendancePunch"` / `"PayslipObjection"`; permission adları W0-b listesiyle aynı.
- **Çakışma noktaları:** `shift-plan.tsx` yalnız F5; `shared-files.tsx` yalnız F12; `onboarding.tsx` yalnız F13; `company-calendar.tsx` yalnız F16; `AttendanceAnomaly.cs`/`DbContext`/snapshot yalnız W0-a; Attendance'ta Dalga 1'de migration ekleyen tek şerit B1; `AttendanceReportPdfExport.cs` yalnız R1.

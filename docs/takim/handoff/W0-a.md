# W0-a — Attendance şema temeli + karakterizasyon testleri

**Durum:** incelemeye hazır (2026-09-25) · dal `takim/W0-a` · **riskli alan: migration → insan onayı**

Plan: `docs/superpowers/plans/2026-09-25-zaman-devam-birlesik.md` Task W0-a · Spec §4.

## Yapılan
- Karakterizasyon (mevcut koddan gözlenen değerler, davranış değişmedi):
  `tests/Modules/Sense.Modules.Attendance.Tests/Characterization/{MonthlySummary,OvertimePayroll}CharacterizationTests.cs`,
  `tests/Modules/Sense.Modules.Payroll.Tests/AttendanceWorkedTimeCharacterizationTests.cs`. Karışık ay (Ağustos 2026):
  gün anlık görüntüsü + aylık özet (8 gün, 60 sa, 13 eksik, FM 6,5 sa, pazar 1) + provizyonel FM 360 dk.
- Domain: `Punch`, `PunchSource`, `ShiftDefinition` (+`ShiftBreak`, `ShiftType`, `ShiftDefinitionValues`), `ShiftDayType`,
  `ShiftPlanTemplate`, `PunchRequest`, `RotationPattern`, `RotationAssignment`, `Terminal`, `TerminalGroup`,
  `EmployeeTerminalCredential`. `AttendanceAnomaly.Incalculable = 1 << 10`, `AttendanceDaySource.Request/Terminal`,
  `PunchDirection.Unknown = 2`, `ShiftAssignment.ShiftDefinitionId/DayType` + `SetPlan`.
- Repository arayüzleri (`Domain/Repositories.cs` sonu) + EF uygulamaları (`Infrastructure/Persistence/Repositories.cs` sonu),
  DbContext DbSet/konfigürasyon/`AttendanceTables` sabitleri, 40 hata kodu (tr/en resx sonu).
- Migration `20260925113345_AddPunchShiftDefinitionRotationTerminal`: yalnız yeni 9 tablo + `shift_assignments`'a
  `day_type` (int, varsayılan 0 = Work) ve `shift_definition_id` (null) + indeksler. Mevcut veri değişmez.

## Sonraki şeritler için notlar
- Yeni repository'ler **DI'a kayıtlı değil** (`AttendanceServiceCollectionExtensions.cs` bu dalgada W0-b'de). Tüketen şerit
  (B1/B2/B6/B9/B10/T1/T2) kaydı sona ekler: `IPunchRepository→PunchRepository`, `IShiftDefinitionRepository→ShiftDefinitionRepository`,
  `IPunchRequestRepository→PunchRequestRepository`, `IShiftPlanTemplateRepository→ShiftPlanTemplateRepository`,
  `IRotationRepository→RotationRepository`, `ITerminalRepository→TerminalRepository`.
- `IPunchRepository` sorguları kiracı filtresini atlar, açık `tenantId` ile süzer; silinmiş okutmalar hiç dönmez.
- `Punch.Create` imzası plandaki gibi; sonuna isteğe bağlı `DateTime? nowUtc` eklendi (test edilebilir ileri tarih kontrolü, tolerans 5 dk).
- `PunchDirection.Unknown` self-servis QR/GPS doğrulayıcılarında reddedilir.
- Web/mobil tipleri (W0-c): anomali `incalculable`, kaynak `request`/`terminal`, yön `unknown` yeni JSON değerleri.

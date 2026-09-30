# FX1 — web/mobil takip düzeltmeleri

1. F3: `LateEarlyByUnitRow.orgUnitId` opsiyonel; id'siz satır `timeDashboard.unassigned` (tr/en/de) ile gösterilir (`time-kpi-groups.tsx`).
2. F11: `canObject` (web + mobil) = yayınlanmış && onaysız && `objectionStatus` yok (backend `FileObjection`: onaylı bordroda ve her sürümde ikinci itirazda 409).
3. F2: `daily-grid.tsx` birim sütunu sayfada hiçbir satırda `orgUnitName` yoksa gizli.
4. i18n: `incalculableReason` (`AttendanceDay`, `DailyAttendanceRow`, opsiyonel) -> `IncalculableHint` (günlük liste + Puantajım); bilinmeyen değer ham gösterilir.
   `attendance.timesheet.period_locked` -> `getAttendanceErrorMessage` (`lib/attendance-error.ts`), günlük liste, self-check-in, aylık/ekip puantajı hata yollarında.

Açık: `DailyAttendanceRow.incalculableReason` backend'de (günlük liste DTO'su) henüz olmayabilir; alan yoksa ipucu görünmez. Overtime-by-group "atanmamış" satırı ele alınmadı (sözleşme belirsiz). Aylık/ekip puantaj gün hücrelerinde ipucu yok (yalnız Puantajım + günlük liste).

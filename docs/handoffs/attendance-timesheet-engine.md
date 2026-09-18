# senseik-012 + senseik-014 — Fazla Mesai + Puantaj Motoru — UYGULAMA PLANI

**Durum: GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

## ⚠️ Kart Tanımı Kısmen Güncel Değil

Bazı şeyler zannedildiğinden daha ileride, bazıları daha eksik:
- **270 saat yıllık sınır ZATEN VAR** — `OvertimeRequestRules.EvaluateAsync`, `LegislationParameterKeys.OvertimeAnnualLimitHours` (270, seed'li) kontrolü yapıyor, uyarı üretiyor — ama kalıcı/görünür sayaç (gauge) UI'da yok.
- **Serbest zaman 1.5:1 oranı ZATEN UYGULANIYOR** (`OvertimeConversion.ToTimeOff`) — ama **6 ay kullanım süresi hiç takip edilmiyor** (`OvertimeCompTimeExpiryMonths` seed'li ama hiçbir yerde tüketilmiyor).
- **"Ekip puantajı" ızgarası zaten gerçek veri döndürüyor** (`TeamTimesheetFeatures.cs`, anomali flag'leriyle) — kartın "hücreler anlamsız (—1)" tarifi güncel değil; asıl eksik hücrenin *arkasındaki hesaplama derinliği* (mola, gece, günlük/haftalık limit, denkleştirme).
- **Gece çalışması sınırı sadece vardiya PLANLAMASINDA var** (`ShiftSettings`), gerçekleşen puantajda (`AttendanceDay.Recompute()`) hiç uygulanmıyor.
- **%25/%50 ücret oranı seçimi hiçbir yerde yok** — `OvertimeConversion.Kind=Pay` implemente değil.
- **Denkleştirme (m.63) ile mevcut `CompensatoryWorkLedger` (telafi çalışması, m.64) FARKLI KAVRAMLAR** — isim benzerliği kafa karıştırıcı, denkleştirme sıfırdan tasarlanmalı.

## PDKS CSV Genişletmesi (Mola/Çoklu Hareket)

Şema kırılmaz — yeni sütun ZORUNLU değil. `ImportDeviceLogsHandler`'daki grup mantığı genişletilir: ilk "in" ile son "out" arasındaki ara Out→In çiftleri gerçek mola olarak yorumlanır → `AttendanceDay.ActualBreakMinutes` (yeni alan, mevcut `BreakMinutes`=planlanan sabit mola ile karıştırılmaz). Opsiyonel v2: 5. sütun `punchType` (work/break), boşsa eski sezgisel davranış.

## Puantaj Hesaplama Motoru

Yeni: `Attendance.Domain.AttendanceDays.DailyWorkCalculator` (saf, statik). Girdi: gün damgaları + `WorkScheduleDay` + `ShiftSettings`. Çıktı: `NetWorkedMinutes`, `BreakMinutesApplied` (m.68: max(gerçek mola, yasal asgari — 4-7.5s→30dk, 7.5s+→1sa)), `NightMinutes`, `DailyLimitExceeded` (m.63: 11s), `NightLimitExceeded` (m.69: 7.5s), `OvertimeMinutes`. `AttendanceDay.Recompute()` bu servise delege eder.

## Fazla Mesai Ücret Oranı + 270 Saat Sayacı

- **Oran:** `OvertimeRequest`'e yeni `OvertimePayRateKind{Rate25,Rate50}` (Leave'de, Attendance'da değil — fazla mesai talebi orada yaşıyor). Sözleşme haftalık saatine bağlı ayrım (m.41), MVP: otomatik öneri + HR override.
- **270 saat sayacı:** Kalıcı entity yerine hesaplanan sorgu (`GetAnnualOvertimeUsageQuery`) — onay formunda gauge, "Fazla Mesailerim" sayfasında gösterge.
- **6 ay serbest zaman takibi:** `OvertimeConversion`'a `UsedByDate` + `Status.Expired` (mevcut `CompensatoryWorkLedger.ExpireIfPastDue` deseni).
- **Gece limiti:** `ShiftSettings.NightWorkEnabled` varsayılanı şu an `false` — MVP'de **açık ve 450dk (7.5s)** yapılmalı (mevcut tenantlar için veri geçiş kararı gerekir).

## Denkleştirme (m.63, 2/4 ay) — En Riskli Parça

Yeni entity: `EqualizationPeriod` (PeriodStart/End, TotalWorkedMinutes, BudgetMinutes=45s×hafta sayısı, TrueUpOvertimeMinutes, Status). Günlük motorun ürettiği `OvertimeMinutes` dönem kapanana kadar **provizyon (kesin değil)**. **PO/hukuk onayı olmadan implementasyona geçilmemeli** — dönem sınırı/prorata/kısmi dönem kuralları netleşmeden kod yazmak yanlış varsayımlara yol açar.

## Puantaj → Bordro Kontratı

Yeni `Attendance.Contracts.IAttendanceOvertimePayrollReader` (mevcut `IAttendanceMonthlySummaryReader` kırılmadan, paralel yeni kontrat) + `Leave.Contracts.IOvertimePayrollReader`. Payroll her ikisini tüketir, Attendance↔Leave birbirine bağımlı olmaz (modüler-monolit disiplini).

## Migration Sırası

1. Leave: `OvertimeRequest.PayRateKind/PayRateMultiplier`, `OvertimeConversion.UsedByDate/Status.Expired`
2. Attendance: `AttendanceDay` yeni alanlar, `ShiftSettings` denkleştirme kolonları
3. Attendance: yeni `equalization_periods` tablosu

## Test Planı

`DailyWorkCalculator` sınır değerleri (m.68 tam 4s/7.5s, gece penceresi gece yarısı sarması), 270 saat sınırda (269:59/270:00/270:01) + yıl dönümü sıfırlanması, 6 ay expiry ay-sonu kenar durumları, **denkleştirme dönem sınırı/kısmi dönem/2→4 ay geçişi** (en riskli), PDKS mola çıkarım senaryoları, puantaj→bordro sözleşme testleri.

## Riskler

1. **Denkleştirme sıfırdan tasarım — GATE 1'de PO/hukuk onayı gerekli.**
2. **`CompensatoryWorkLedger` (telafi) ile yeni `EqualizationPeriod` (denkleştirme) kavram karışıklığı** — doc comment'lerde açık ayrım gerekli.
3. **İki farklı gece penceresi tanımı** — `OvertimeMath` (Leave, sabit 20:00-06:00) vs `ShiftSettings` (Attendance, tenant-ayarlanabilir) — tutarsız sonuç riski, v1 kapsamı dışına alınabilir ama borç olarak kayda geçirilmeli.
4. **Gece limiti varsayılanı `false`** — mevzuat kontrolü varsayılan tenant'ta çalışmıyor, değiştirilmeli + veri geçişi gerekir.
5. **Serbest zaman grant'inin (Leave ledger) kendi expiry mekanizması doğrulanmadı** — implementasyondan önce araştırılmalı.
6. **Performans** — `TeamRangeBuilder` N+1 riski, motor genişleyince ağırlaşır, batch veri çekme gözden geçirilmeli.

## Kritik Dosyalar
- `src/Modules/Attendance/Sense.Modules.Attendance.Domain/AttendanceDays/AttendanceDay.cs`
- `src/Modules/Attendance/Sense.Modules.Attendance.Application/DeviceLogs/DeviceLogCsvParser.cs`, `DeviceLogImportFeatures.cs`
- `src/Modules/Attendance/Sense.Modules.Attendance.Domain/Shifts/ShiftSettings.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Domain/Overtime/OvertimeRequest.cs`, `OvertimeConversion.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Application/Overtime/OvertimeRequestFeatures.cs`
- `src/Modules/Attendance/Sense.Modules.Attendance.Contracts/IAttendanceMonthlySummaryReader.cs`
- `web/src/pages/attendance/team-timesheet.tsx`, `web/src/components/leave/overtime-section.tsx`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § F-05, G-02. Agent board: `docs/agent-board/cards/senseik-012-fazla-mesai-motor.json`, `senseik-014-puantaj-motor.json`.*

# Faz 2 Durum Takibi (Attendance: vardiya/puantaj/çalışma takvimi)

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) satır 84-91 (kalem #46-53), ayrıntı [11.3.06-pdks-puantaj-vardir-takvim.md](./11.3.06-pdks-puantaj-vardir-takvim.md); iş bölümü [paralel-gelistirme-plani.md](../../implementation/paralel-gelistirme-plani.md) §7 ("Attendance ... Leave motorundan sonra").

**Son güncelleme:** 2026-09-16
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti.

## Paket sırası

A0 (modül iskeleti) → **A1 (çalışma takvimi + atama)** → A2 (AttendanceDay + PDKS import) → A3 (uyuşmazlık çözümü) → A4 (vardiya ayarları/planı/kilit) → A5 (self-servis ekranları + raporlar + telafi).

## Kalemler

| Paket | Kalem | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|
| A0 | Modül iskeleti (5 proje: Domain/Contracts/Application/Infrastructure/Api, şema `attendance`) | ✅ Bitti | `Sense.Modules.Attendance.*`; hot file kayıtları: `ModuleCatalog.cs`, `Sense.Migrator/Program.cs`, `Sense.Worker/Program.cs`, ilgili 3 host `.csproj`, `Sense.slnx` (yeni Folder + test satırı), `TestFixture.cs` `SchemasToInclude`'a `attendance` eklendi | Mimari testler (`Sense.Tests.Architecture`) modülleri assembly adı sonekine göre genel taradığı için elle kayıt gerekmedi |
| A1 | **Çalışma takvimi** (`WorkSchedule`/`WorkScheduleDay`, gün bazlı saat+mola, `IncludeSaturdayWhenFullWeek`) + **atama** (`EmployeeWorkScheduleAssignment`, tarihli, `Manual/Bulk/Hire`) | ✅ Bitti | Domain: `WorkSchedule.CreateDefault` ("Genel çalışma tablosu" seed, Pzt-Cum 09:00-18:00 mola 12:30-13:30), `MakeDefault`, `EnsureDeletable`; Application: CRUD komutları (`CreateWorkScheduleCommand`, rename/update-days/set-default/set-active/delete), `AssignWorkScheduleCommand` (toplu + tekil, `IDataScopeService` + `IEmployeeLookup` kontrolü), `WorkScheduleAssignmentService.EnsureDefaultScheduleAsync` (Leave'in `EnsureDefaultRuleSetAsync`'i gibi ilk kullanımda tembel seed); `EmployeeHiredHandler` (varsayılan takvim ataması, `EffectiveFrom=HireDate`, `Source=Hire`, idempotent); API `attendance/work-schedules` (CRUD + `/assign`); izinler `attendance.workschedule.{view,manage,assign}`, `attendance.my.workschedule` (mevcut `attendance.*` joker izinleri `SystemRoleDefinitions.cs`'te HR/HRSpecialist/Timekeeper'a zaten tanımlıydı — dosyaya dokunulmadı) | Web ekranı yok (Ayarlar > Kurallar > Çalışma takvimleri, çalışan grid toplu aksiyonu, kart "Çalışma tablosu" alanı) — sonraki bir oturumda |
| A1 | **Contract-first: `Attendance.Contracts.IWorkScheduleReadService`** (`GetForDateAsync`, `GetWorkingDaysAsync`, `IsWorkingDayAsync`) → Leave'in Pzt-Cum yer tutucusunun (roadmap #23) yerini aldı | ✅ Bitti | `WorkScheduleReadService` (Infrastructure); Leave tarafı: `WorkScheduleReaderAdapter` (`MondayToFridayWorkScheduleReader`'ın yerine), `Sense.Modules.Leave.Infrastructure.csproj`'a `Attendance.Contracts` referansı eklendi; Leave'in 4 test fixture'ı (`LeaveIntegrationTests`, `LeaveManagementTests`, `LeaveRequestTests`, `OvertimeTests`) artık `AttendanceDbContext`'i de kaydedip migrate ediyor | Leave.Tests bu oturumda uçtan uca doğrulanamadı (bkz. "Bilinen açık nokta" altında) |
| A2 | `AttendanceDay` (planlanan/gerçekleşen dakika, çipler) + PDKS Excel import (`AttendanceImportJob/Row`) | ❌ Başlanmadı | — | — |
| A3 | Anomali çözümü (`AnomalyResolution`, "Fazla mesai ekle"/"Çalışılan saat"/"İzin talebi" önerileri, Leave.Contracts `CreateApprovedLeave`/`CreateOvertimeRequest` entegrasyonu) + haftalık tamamlanma durumu | ❌ Başlanmadı | — | — |
| A4 | Vardiya ayarları (`ShiftSettings`: günlük/haftalık limit, gece, mola, oto-kilit), `ShiftAssignment` (Not/Tekrarla/Yayınla), `ShiftPlanLock` | ❌ Başlanmadı | — | — |
| A5 | Self-servis (Puantajım, Vardiyam, Müsait değilim), raporlar (Excel/PDF), telafi çalışması takibi (`CompensatoryWorkLedger`) | ❌ Başlanmadı | — | — |

## Değişiklik geçmişi

- 2026-09-16: A0 + A1 tamamlandı. Modül iskeleti + çalışma takvimi/atama + `IWorkScheduleReadService` (Leave entegrasyonu dahil). Migration `20260915212919_InitialAttendance`. Doğrulama: `Sense.Modules.Attendance.Tests` 10/10 geçiyor (gerçek PostgreSQL testcontainer, `AttendanceIntegrationTests` + `WorkScheduleDomainTests`). `Sense.Modules.Attendance.Api`/`Sense.Modules.Leave.Api` izole build yeşil.
  **Bilinen açık nokta:** Bu oturumda repo üzerinde eşzamanlı olarak başka oturumlar Analytics/Payroll/Recruitment modüllerini iskeletliyordu (aynı çalışma ağacı, `docs/implementation/paralel-gelistirme-plani.md` §2'deki "aynı çalışma ağacı" kurulumu); bu yüzden `Sense.Api`/`Sense.Migrator`/`Sense.Worker` üzerinden geçen tam çözüm derlemeleri ve `dotnet test` çalıştırmaları sık sık geçici dosya kilidi çakışmalarıyla karşılaştı (yeniden denemelerle aşıldı). `Sense.Modules.Leave.Tests` bu oturumun sonunda hâlâ kırmızı: sebep benim değişikliklerim değil, eşzamanlı çalışan başka bir oturumun Payroll için `LeaveDbContext`'e eklediği `LegislationParameterSeed` satırlarının migration'ının henüz tamamlanmamış olması (`PendingModelChangesWarning` — model migration'la eşleşmiyor, `LeaveDbContext.cs`/`LegislationParameterSeed.cs`/yeni `PayrollLegislationParameters` migration'ı bu oturumda hâlâ commit edilmemiş/kararsız haldeydi). Leave sahibi oturum bu migration'ı tamamlayınca `Sense.Modules.Leave.Tests` yeniden doğrulanmalı.

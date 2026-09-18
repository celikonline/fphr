# senseik-007 — 50 Yaş İstisnası / Ek Kıdem Günü (A-01 Regresyon) — UYGULAMA PLANI

## Implementasyon Notu

**Durum: Uygulandı, `a/age-50-exception` branch'inde, yerel commit alındı (push YAPILMADI).**

Plan aynen uygulandı:

1. `Employee.Contracts/IEmployeeBirthDateLookup.cs` — tekil `GetBirthDateAsync` + toplu `GetBirthDatesAsync`, `IEmployeeSalaryLookup` ile aynı desen.
2. `Employee.Infrastructure/Lookups/EmployeeBirthDateLookup.cs` — `AsNoTracking`, aynı desen.
3. Kayıt: `EmployeeModule.cs` (`AddModule`) VE `Sense.Worker/Program.cs` (Leave bloğundan hemen önce, `IEmployeeDocumentEligibilityLookup` ile aynı desende) — plan §5'teki "Sıralama riski" burada giderildi.
4. `EmployeeHiredHandler.cs` (Leave.Application) — `IEmployeeBirthDateLookup` enjekte edildi, profil oluşturulduktan hemen sonra `SetDates(hireDate, leaveAccrualStartDate, birthDate)` çağrılıyor.
5. `LeaveBalanceRecalculationJob.cs` — `RecalculateTenantAsync` artık tenant'ın tüm profilleri için **toplu** `GetBirthDatesAsync` çağrısıyla doğum tarihi çekip `SetDates(...)` ile güncelliyor (N+1 yok), sonra normal `RecalculateAsync` akışı devam ediyor.
6. `LeaveBalanceEngine.cs` / `StatutoryAnnualLeave` — dokunulmadı (plan gereği zaten doğruydu).
7. `DataSeeder.DemoEmployees.cs` — "Test Yaş" (`testyas@demo.local`) fixed demo hesabı eklendi: `HireDate=today-2y`, `BirthDate=today-56y` (yeni `FixedDemoAccountAgeYears` sözlüğü ile), `EnqueueHiredIfActive` mevcut akıştan otomatik geçiyor.
8. **Plan dışı ek düzeltme:** `DataSeeder.DemoLeave.cs`'deki `SeedDemoLeaveAsync` da `EmployeeLeaveProfile.Create(...)` sonrası hiç `SetDates(...)` çağırmıyordu — yani demo verideki TÜM profiller (Test Yaş dahil) `BirthDate=null` ile kalıyordu ve kartın "Test Yaş → 40 gün" kabul kriteri demo ortamında hiç sağlanamıyordu. Bu metoda da `profile.SetDates(hireDate, accrualStart, employee.BirthDate)` eklendi (yorum satırıyla gerekçelendirildi). Bu olmadan senseik-007 "tamamlandı" sayılamazdı.

### Testler (hepsi yeşil)

- `tests/Modules/Sense.Modules.Leave.Tests/LeaveDomainTests.cs` (`LeaveBalanceEngineTests`): `AnnualType_Age50OrOlder_AppliesTwentyDayFloor`, `AnnualType_UnderFifty_UsesSeniorityTiersOnly`, `AnnualType_AgeExactlyOldAgeMin_AppliesTwentyDayFloor` (boundary), `AnnualType_SeniorityTierAboveFloor_TierWinsOverAgeFloor`.
- `tests/Modules/Sense.Modules.Leave.Tests/EmployeeHiredHandlerTests.cs` (yeni, mock `IEmployeeBirthDateLookup`): 55 yaşında işe giriş → profil BirthDate set + ilk accrual 20 gün; doğum tarihi yoksa BirthDate null + accrual 14 gün (kademe); mevcut profil varsa idempotent ve lookup hiç çağrılmıyor.
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveBalanceRecalculationJobTests.cs` (yeni, Testcontainers/Postgres, gerçek `EmployeeDbContext`+`LeaveDbContext`): null→dolu BirthDate backfill + ledger'da 14→20 gün geçişinin gerçekten üretildiğinin kanıtı; doğum tarihi kaydı olmayan çalışan için profil null kalıyor ve sadece kademe tabanı uygulanıyor.
- `tests/Modules/Sense.Modules.Employee.Tests/EmployeeBirthDateLookupTests.cs` (yeni, Testcontainers/Postgres): tekil + toplu okuma, boş girişte sorgusuz erken dönüş, kiracı izolasyonu.

Doğrulama: Docker SDK konteyneriyle `dotnet build Sense.slnx` → **Build succeeded** (tüm solution, hata yok). Hedefli test koşusu (`LeaveBalanceEngineTests` + `EmployeeHiredHandlerTests` + `LeaveBalanceRecalculationJobTests`, filtered): **22/22 passed**. `Sense.Modules.Employee.Tests` tam koşusu: 139 testten 135 geçti, kalan 4 hata (`PaymentsIntegrationTests`, `IUserProfileLookup` DI eksikliği) bu değişiklikten tamamen bağımsız, önceden var olan bir test-altyapı sorunu — dokunulmadı. `Sense.Modules.Leave.Tests` tam koşusunda ayrıca 38 önceden var olan hata var (`AttendanceDbContext` "PendingModelChangesWarning" — `c72791f chore(attendance): add pending EF migration for MonthlyTimesheetApproval` commit'inden miras, bu branch'in temeli `dev`'de zaten mevcuttu); bunlar da senseik-007 ile ilgisiz ve dokunulmadı.

### ⚠️ Geriye Dönük Bakiye Artışı Riski — Plan §5, Aynen Geçerli

Bu, motor/kod hatası değil, **bilinçli ve beklenen bir davranıştır**: `LeaveBalanceRecalculationJob` artık her gece (01:00 UTC) çalıştığında, `BirthDate` alanı henüz set edilmemiş TÜM mevcut profilleri (bu deploy'dan önce oluşturulmuş her 50+/18- yaşındaki çalışan) geriye dönük olarak backfill edip yeniden hesaplayacak — bu da o çalışanların izin bakiyelerini (14→20 gün gibi) **sessizce ve otomatik olarak artıracak**. Canlıya çıkmadan önce HR'a bilgi verilmeli; ideal olarak ilk çalıştırma önce bir dry-run/rapor modunda etkilenecek çalışanları listeleyip onay almalı. Detay ve gerekçe: aşağıdaki "5) Riskler" bölümü (orijinal plan).

---

**Durum (orijinal plan metni, referans için korunuyor): GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

## Kök Neden (özet)

Bu bir "motor eksik istisna uygulamıyor" hatası değil — bir **veri akışı (wiring) hatası**. Hesaplama motoru doğru yazılmış ve domain seviyesinde test edilmiş:

- `LeaveBalanceEngine.cs`: `StatutoryAnnualLeave.MinimumDays(completedYears, age)` ve `YearlyEntitlement(...)` zaten `age <= YoungAgeMax || age >= OldAgeMin` durumunda günü 20'ye yükseltiyor.
- `LeaveRuleSetSeed.cs`, `LegislationParameterSeed.cs`: `AgeExceptionEnabled=true`, `YoungAgeMax=18`, `OldAgeMin=50`, `AgeExceptionMinDays=20` (m.53) zaten doğru tanımlı.
- `LeaveDomainTests.cs`: `YearlyEntitlement` yaş istisnası birim testli ve geçiyor (birthDate doğrudan verildiğinde).

**Gerçek kusur:** `EmployeeLeaveProfile.cs` — `BirthDate` alanı "Employee.Contracts'ta henüz açılmadığı için null" yorumuyla açık şekilde eksik. `EmployeeHiredHandler`, profili oluştururken `BirthDate` hiç geçmiyor; `EmployeeHired` event'i de bu alanı taşımıyor. Sonuç: `LeaveBalanceService.RecalculateAsync` her zaman `BirthDate=null` ile çağrılıyor → yaş istisnası hiç tetiklenmiyor, sadece kıdem kademeleri uygulanıyor. Employee modülünde `BirthDate` zaten dolu ama KVKK nedeniyle hiçbir port ile dışa açılmamış.

## 1) Yeni Port Gerekli

`Employee.Contracts`'a yeni: `IEmployeeBirthDateLookup` (tekil + batch metod — `IEmployeeSalaryLookup` ile aynı desen):
```csharp
public interface IEmployeeBirthDateLookup
{
    Task<DateOnly?> GetBirthDateAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, DateOnly>> GetBirthDatesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
}
```
İmplementasyon: `Employee.Infrastructure/Lookups/EmployeeBirthDateLookup.cs` (aynı desen). Kayıt: `EmployeeModule.cs` + `Sense.Worker/Program.cs` (modüller Worker'da manuel bağlanıyor). Event üzerinden DEĞİL port üzerinden — hassas alanlar (`BirthDate`, `DisabilityDegree`) hiçbir integration event payload'ında taşınmıyor, mevcut mimari kural bu.

## 2) Motora Entegrasyon

1. `EmployeeLeaveProfile.Create(...)` sonrası `profile.SetDates(hireDate, leaveAccrualStartDate, birthDate)` çağrılır (metod zaten var, hiç çağrılmıyor).
2. `EmployeeHiredHandler.cs`: `IEmployeeBirthDateLookup` enjekte edilir, `Handle` içinde lookup çağrılıp profile geçirilir.
3. `LeaveBalanceRecalculationJob.cs` (`RecalculateTenantAsync`): tüm profiller için **toplu** (`GetBirthDatesAsync`) doğum tarihi çekilir, `profile.SetDates(...)` ile güncellenir (EF tracked entity, `SaveChangesAsync` otomatik persist eder) — bu hem geriye dönük backfill'i hem günlük senkronizasyonu aynı mekanizmayla çözer (job zaten her gece 01:00 UTC'de çalışıyor).
4. `LeaveBalanceEngine.cs`/`StatutoryAnnualLeave` — değişiklik yok, zaten doğru.

## 3) "Test Yas" Demo Çalışanı — Beklenen Sonuç

56 yaş, 2 yıl kıdem, varsayılan kural seti (AnnualDays=14, SeniorityTiers, AgeExceptionMinDays=20):
- `years=2` → tier tetiklenmez → temel=14.
- `age=56 ≥ OldAgeMin(50)` → `Math.Max(14, 20) = 20` gün/dönem.
- 2 tam dönem → **toplam 40 gün** (kartın acceptance hedefiyle örtüşüyor).

**Not:** Kartın "(14 kıdem + 14 dönem + 12 statüso)" notu koddaki hiçbir kavramla örtüşmüyor — bozulmuş/anlamsız, düzeltilmeli: "20 gün/dönem × 2 dönem = 40 gün, İş K. m.53".

Demo veride "Test Yas" adında bir çalışan **şu an yok** — `DataSeeder.cs`'e eklenmesi gerekiyor (`HireDate=today-2y`, `BirthDate=today-56y`, `EnqueueHiredIfActive`).

## 4) Test Planı

**Domain:** 50+ yaş için simetrik testler (`AnnualType_Age50OrOlder_AppliesTwentyDayFloor`, `AnnualType_UnderFifty_UsesSeniorityTiersOnly`, boundary `age==OldAgeMin`, `TierWinsOverFloor`).

**Wiring/entegrasyon (asıl bug burada):** `EmployeeHiredHandlerTests` (mock lookup, profile.BirthDate eşleşmesi), `LeaveBalanceRecalculationJobTests` (null→dolu BirthDate + ledger accrual güncellemesi — backfill'in gerçekten çalıştığının kanıtı), `EmployeeBirthDateLookupTests` (tekil+batch, tenant izolasyonu).

**E2E:** Demo'da "Test Yas" seed + job sonrası 40 gün görünümü (merge_gate zaten bunu istiyor).

## 5) Riskler

- **Geçmiş hak ediş kayıtlarının yeniden hesaplanması — KRİTİK RİSK.** `Regenerate` her çalıştığında `IsGenerated=true` satırları silip yeniden üretiyor (Usage/ExtraGrant/Adjustment korunuyor) — idempotent ve tasarım olarak güvenli, ama **50+ ve/veya 18- yaşındaki TÜM mevcut kiracıların TÜM çalışanları için bakiyeler otomatik ve geriye dönük olarak artacak**. Bir gecede 14→20 (veya CarryOver ile daha fazla) gün artış, muhasebe/bordro tarafında sürpriz olabilir.
- **Payroll etkisi:** İzin ücreti tahakkuku/kıdem tazminatı hesaplarken kullanılmayan izin günlerini referans alan bir Payroll akışı varsa, ani artış finansal raporları etkiler — kontrol edilmeli.
- **Performans:** Job'a batch lookup ekleniyor — büyük tenant'larda N+1'i önlemek için mutlaka `GetBirthDatesAsync` (batch), tekil metod döngüde çağrılmamalı.
- **İletişim riski:** Bu değişiklik sessizce (arka plan job'uyla) mevcut bakiyeleri değiştirir — canlıya çıkmadan önce HR'a "50+/18- yaşındaki çalışanların izin bakiyeleri İş K. m.53 gereği yükselecek" bilgisi verilmeli; ideal olarak ilk çalıştırma bir dry-run/rapor modu ile önce etkilenecek çalışanlar görülüp onaylanmalı.
- **Sıralama riski:** `IEmployeeBirthDateLookup` Sense.Worker'da kaydedilmezse DI runtime hatası — kayıt adımı gözden kaçırılmamalı.

## Kritik Dosyalar
- `src/Modules/Employee/Sense.Modules.Employee.Contracts/IEmployeeSalaryLookup.cs` (şablon)
- `src/Modules/Employee/Sense.Modules.Employee.Infrastructure/Lookups/EmployeeDocumentEligibilityLookup.cs` (implementasyon deseni)
- `src/Modules/Leave/Sense.Modules.Leave.Application/Employees/EmployeeHiredHandler.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Jobs/LeaveBalanceRecalculationJob.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Domain/Profiles/EmployeeLeaveProfile.cs`
- `src/Sense.Worker/Program.cs`, `src/Modules/Employee/Sense.Modules.Employee.Api/EmployeeModule.cs` (port kaydı)
- `src/Sense.Api/SeedData/DataSeeder.cs` ("Test Yas" demo çalışanı)
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveDomainTests.cs`

---
*Kaynak: 1. Tur A-01, 2. Tur Regresyon §5. Agent board: `docs/agent-board/cards/senseik-007-yas-istisnas.json`.*

# senseik-055 — IEmployeeBirthDateLookup DI kayıt eksikliği (Leave testleri)

## Durum: Uygulandı, yerel commit'e hazır (GATE 1/2 önceden onaylı). Push yapılmadı.

## Kök neden

`EmployeeHiredHandler` (`src/Modules/Leave/Sense.Modules.Leave.Application/Employees/EmployeeHiredHandler.cs`)
constructor'ında `IEmployeeBirthDateLookup` bağımlılığı var (KVKK gereği doğum tarihi `EmployeeHired`
event'inin payload'ında taşınmıyor, bu port üzerinden ayrı okunuyor — 4857 md.53/IV yaş istisnası için).
senseik-007 bu bağımlılığı eklediğinde, senseik-012/014 ajanı yalnız kendi ihtiyacı olan
`OvertimeTests.cs`'teki fixture'ı düzeltmiş, aynı DI kaydına ihtiyaç duyan diğer üç Leave entegrasyon test
dosyasını kapsam dışı bırakmıştı.

Üç dosya (`LeaveIntegrationTests.cs`, `LeaveManagementTests.cs` içindeki `LeaveManagementIntegrationTests`,
`LeaveRequestTests.cs` içindeki `LeaveRequestIntegrationTests`) kendi `ConfigureServices` override'larında
yalnız Leave + Attendance modüllerini gerçek DB üzerinde kayıt ediyor; Employee modülü hiç register
edilmiyor. Bu üç dosya `EmployeeHired`'ı `IEventBus.Publish` ile gerçek `InProcessEventBus` üzerinden
tetikliyor (worker'ın outbox'tan teslim ettiği yolu taklit ediyor), bu da `EmployeeHiredHandler`'ı DI
container'ından çözümlemeye çalışıyor — `IEmployeeBirthDateLookup` için hiçbir implementasyon/stub kayıtlı
olmadığından `System.InvalidOperationException: Unable to resolve service for type 'IEmployeeBirthDateLookup'`
fırlatıyordu.

Production tarafında sorun yok: `EmployeeModule.AddModule` (`src/Modules/Employee/Sense.Modules.Employee.Api/EmployeeModule.cs:121`)
`IEmployeeBirthDateLookup`'ı gerçek `EmployeeBirthDateLookup` implementasyonuyla doğru register ediyor, ve
`Sense.Worker/Program.cs` de ayrıca (Employee modülü Worker'da tam yüklenmediği için) elle register ediyor —
bu yüzden hata yalnız bu üç test dosyasının kendi izole test-DI container'ında ortaya çıkıyordu.

**Branch notu:** Kart açıldığında "19/146 kırmızı" verisi `origin/dev` ucundaki (commit `90de9e2`) test setine
göreydi. Bu worktree'nin başladığı `origin/main` (commit `0d7261f`) 23 commit geride kalmış ve orada Leave
testleri farklı, bağımsız bir nedenle (Attendance modülünün `PendingModelChangesWarning`'i — senseik-054'ün
çözdüğü sorun, henüz main'e merge edilmemiş) 38/128 kırıktı. Bu karışıklığı önlemek için branch `origin/dev`
üzerinden açıldı (main, dev'in bir atası — divergent değil, fast-forward); orada anlatılan hata birebir
doğrulandı (19/146).

## Çözüm — 4 ayrı düzeltme (kapsam ilk tahminden geniş çıktı)

Birthdate lookup'ı üç dosyanın `ConfigureServices`'ine ekleyince kart 19/19'u yeşile çevirdi ama toplam hâlâ
142/146'ydı: aynı erken çökme (HireAsync/EmployeeHired publish'i patlaması) daha önce **üç ayrı, önceden var
olan ve kapsam dışı test açığını** maskeliyormuş. `dotnet test` çalıştırılıp kalan 4 hata tek tek
incelenerek hepsi giderildi:

1. **`IEmployeeBirthDateLookup` — asıl kart konusu.** Üç dosyanın class-seviyesi `ConfigureServices`'ine
   `services.AddSingleton(Substitute.For<IEmployeeBirthDateLookup>());` eklendi. Metod hiç `.Returns(...)`
   ile ayarlanmadı — NSubstitute, yapılandırılmamış `Task<DateOnly?>` dönen metodlar için varsayılan olarak
   `Task.FromResult<DateOnly?>(null)` döner, bu da handler'ın davranışını (doğum tarihi bilinmiyorsa yaş
   istisnası uygulanmaz) production'daki "kayıtlı değilse null" davranışıyla tutarlı tutar. Desen, aynı üç
   dosyada zaten kullanılan `IEmployeeLookup`/`IEmployeeDirectory`/`IHolidayCalendarLookup` stub'larıyla
   birebir aynı.

   **Tasarım notu / kart metnindeki "OvertimeTests.cs'teki düzeltmeyle aynı desen" ifadesinden sapma:**
   `OvertimeTests.cs`'teki `OvertimeIntegrationTests` bu sorunu gerçek implementasyonla çözmüş
   (`services.AddModuleDbContext<EmployeeDbContext>(...)` + `services.AddScoped<IEmployeeBirthDateLookup, EmployeeBirthDateLookup>()`
   — Employee modülünün DbContext'i ve migration'ları da kayıtlı). Burada bilinçli olarak daha hafif
   NSubstitute-stub yolu tercih edildi: (a) bu üç dosya zaten aynı problem sınıfındaki diğer üç lookup'ı
   (yukarıda) stub'lıyor — gerçek implementasyon tek başına tutarsız bir istisna olurdu; (b) hiçbir test
   doğum tarihine bağlı bir senaryoyu (yaş istisnası) doğrulamıyor, o davranış zaten
   `EmployeeHiredHandlerTests.cs` ve `LeaveBalanceRecalculationJobTests.cs`'te ayrıca test ediliyor; (c) üç
   fixture'a ayrıca bir DbContext + migration eklemek test süresini uzatır ve Employee şemasına gereksiz bir
   bağımlılık ekler. Bu üç dosya ileride gerçekten doğum-tarihi-bağımlı bir senaryo test etmeye başlarsa,
   `OvertimeTests.cs`'teki gerçek-implementasyon deseni o zaman uygulanmalı.

2. **`LeaveIntegrationTests.WorkerRegistration_ResolvesEveryLeaveIntegrationEventHandler`** — bu test kendi
   izole `ServiceCollection`'ını metod içinde kuruyor (class-seviyesi `ConfigureServices`'i kullanmıyor), bu
   yüzden #1'deki düzeltme buraya ulaşmıyordu. Aynı stub bu metodun kendi service collection'ına da eklendi.

3. **`LeaveManagementIntegrationTests.Detail_TeamOverlap_ResolvesOrgUnitName`** — birthdate düzeltmesi
   sonrası artık `HireAsync`'i geçip `CreateLeaveRequestOnBehalfHandler`'ı çağırıyordu ama
   `leave.employee.not_in_scope` hatasıyla düşüyordu: testte `_dataScope.CanAccessEmployeeAsync(...).Returns(true)`
   satırı hiç yoktu (dosyadaki üç kardeş testte — satır ~297, ~359, ~466 — bu satır var, bu testte unutulmuş).
   Aynı satır eklendi.

4. **`LeaveRequestIntegrationTests.GetLeaveCalendarHandler_NoOverlap_OverlappingApprovedCountIs0` ve
   `GetLeaveCalendarHandler_IncludesOverlappingApprovedCount`** — `GetLeaveCalendarHandler`
   (`src/Modules/Leave/Sense.Modules.Leave.Application/Calendar/LeaveCalendarFeatures.cs`) constructor'ında
   `IPermissionService`/`IDataScopeService`/`IAccessSettingsService` (hepsi Identity modülü kontratları) var;
   bu fixture Identity modülünü hiç register etmiyor, üçü de DI'da yoktu. Üçü de `AddSingleton` stub olarak
   eklendi; `IAccessSettingsService.IsEnabledForCurrentUserAsync`/`IsEnabledAsync` `true` döndürecek şekilde
   ayarlandı — bu, arayüzün kendi XML doc'undaki gerçek üretim varsayılanını ("anahtar tanınmıyorsa/kiracı
   bağlamı yoksa true — güvenli varsayılan: kısıtlama yok") taklit ediyor; NSubstitute'un kendi varsayılanı
   (yapılandırılmamış `Task<bool>` için `false`) bunun tam tersi olduğundan, taklit edilmezse handler'ın
   "izin verilmeyen kullanıcı → herkese açık takvim" yoluna hiç girmiyor ve org-unit görünürlüğü boşalıyordu.
   `IPermissionService`/`IDataScopeService` yapılandırılmadan bırakıldı (bu iki test takvim izin kapsamını
   değil, org-unit görünürlüğünü test ediyor).

Değiştirilen dosyalar (hepsi test-only, hot-file listesine girmiyor):
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveIntegrationTests.cs`
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveManagementTests.cs`
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveRequestTests.cs`

## Test sonucu

`dotnet test tests/Modules/Sense.Modules.Leave.Tests/Sense.Modules.Leave.Tests.csproj` (Docker SDK
konteyneri, Testcontainers/Postgres, branch `b/birthdate-lookup-test-di-fix` üzerinde, `origin/dev` ucundan):

- Düzeltme öncesi: **19 kırmızı / 127 yeşil / 146 toplam** (hepsi `IEmployeeBirthDateLookup` DI hatası).
- Sadece #1 uygulandıktan sonra (ara adım): **4 kırmızı / 142 yeşil / 146 toplam** — #2/#3/#4'teki maskelenmiş
  açıklar ortaya çıktı.
- Tüm 4 düzeltme sonrası: **146/146 yeşil** (2 bağımsız koşuda doğrulandı, 3m31s ve öncesinde tutarlı).

Başka hiçbir test etkilenmedi.

**Ortam notu:** Bu makinede paralel çalışan başka Claude oturumları/worktree'ler nedeniyle Docker Desktop
test sırasında birkaç kez düştü/bellek yetersizliği verdi (`Cannot allocate memory`, `dockerDesktopLinuxEngine`
pipe hatası) — kodla ilgisi yok, sadece host kaynak baskısı; yeniden denemede sorunsuz geçti.

## Kritik dosyalar
- `src/Modules/Leave/Sense.Modules.Leave.Application/Employees/EmployeeHiredHandler.cs` (bağımlılığın kaynağı, değiştirilmedi)
- `src/Modules/Leave/Sense.Modules.Leave.Application/Calendar/LeaveCalendarFeatures.cs` (GetLeaveCalendarHandler, değiştirilmedi)
- `src/Modules/Employee/Sense.Modules.Employee.Contracts/IEmployeeBirthDateLookup.cs` (kontrat, değiştirilmedi)
- `src/Modules/Employee/Sense.Modules.Employee.Api/EmployeeModule.cs` (production DI kaydı, değiştirilmedi — referans için doğrulandı)
- `src/Shared/Sense.Shared.Contracts/Security/Permissions.cs` (IPermissionService/IDataScopeService/IAccessSettingsService kontratları, değiştirilmedi)
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveIntegrationTests.cs`
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveManagementTests.cs`
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveRequestTests.cs`
- `tests/Modules/Sense.Modules.Leave.Tests/OvertimeTests.cs` (referans: gerçek-implementasyon deseni, değiştirilmedi)

## Açık not

Bu branch main'e merge edilirken, main henüz dev'deki 50 commit'i içermiyorsa, merge sırasında
`LeaveIntegrationTests.cs` / `LeaveManagementTests.cs` / `LeaveRequestTests.cs` dosyalarının main'deki
hallerinin bu dört değişikliği (veya denklerini) hâlâ içerip içermediği kontrol edilmeli.

## Doğrulama (2026-09-19, merge öncesi)

- Branch `dev` (96f9b48) üzerine çakışmasız rebase edildi.
- Windows host'ta native `dotnet test tests/Modules/Sense.Modules.Leave.Tests/Sense.Modules.Leave.Tests.csproj`
  (Testcontainers / Docker Desktop): **146/146 başarılı**, 0 başarısız, 0 atlanan (9m53s).
- Migration yok (yalnız test fixture değişiklikleri). Kart `review` durumunda; dev'e merge'e hazır.

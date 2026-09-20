# senseik-045 — "Eksik bilgi" Rozeti 56/56 Çalışanda Çıkıyor — UYGULAMA NOTU

**Durum: Uygulandı ve doğrulandı (GATE 1/GATE 2 önceden onaylı). `a/missing-info-badge-fix` dalında commit edildi; push yapılmadı.**

Bu kart iki oturumda tamamlandı: ilk oturum `7f9452f` ile asıl değişikliği yaptı ama `dev`'in 40+ commit ilerisinde kalmıştı (rebase edilmemiş, doğrulama yarım). Bu oturum branch'i güncel `dev`'in üzerine rebase etti, iki dosyadaki (`DataSeeder.DemoEmployees.cs`, `DataSeeder.cs`) conflict'leri çözdü ve kalan doğrulama adımlarını tamamladı.

## Değişiklik Özeti

1. **Rozet metnine sayı eklendi** (kabul kriteri #1): `EmployeeCompleteness.IsMissingInfo` yerine `EmployeeCompleteness.MissingFieldCount` (`Ports.cs`) — 6 alanı (NationalId, HireDate, BirthDate, OrgUnitId, ContractType, EmploymentType) tek tek sayıyor, `IsMissingInfo` artık `count > 0`'ın üstüne kurulu. `EmployeeListItemDto.MissingInfoFieldCount` backend'den web'e taşındı (`EmployeeDtos.cs`, `EmployeeQueries.cs`, `employees.service.ts`). `employee-list.tsx` ve `employee-card-grid.tsx` rozeti artık `t("employees:filters.missingInfoBadge", { count })` ile render ediyor — tr/en/de'de `"Eksik bilgi ({{count}})"` / `"Missing info ({{count}})"` / `"Fehlende Angaben ({{count}})"`.
2. **Rozetin tetiklenme mantığı doğrulandı** (kabul kriteri #2): Rozet hâlâ `hasMissingInfo` bayrağına bağlı (`{row.original.hasMissingInfo && <Badge>...}`) — sayı sadece ayırt edicilik katıyor, tetikleme mantığı değişmedi. Demo verisinde artık rastgele üretilen 50 çalışanın ~%20'si (intern/contractor hariç) kasıtlı olarak ContractType/EmploymentType'ı boş bırakılıyor (`NextDemoContractInfo`, `_random.Next(0, 5) == 0`); sabit demo hesapları (owner@, ik@, vb.) her zaman dolu — giriş yaptığın hesapta "Eksik bilgi" görünmesin diye.
3. **Demo veriye örnek belge yüklendi** (kabul kriteri #3): Yeni `DataSeeder.SeedEmployeeDocumentMetadataAsync` — tenant'ın belge tipi kataloğunun tamamını (`DocumentTypeSeed.SystemDefinitions()`) seed'liyor, ilk 8 demo çalışan için onboarding zorunlu tiplerin (11 tip) her biri için ~2/3 ihtimalle `Document` + `EmployeeDocumentRequirement.MarkUploaded`, kalan ~1/3'ü `Missing` durumunda bırakıyor (gerçek dosya yok, sadece metadata). `DataSeeder.cs`'te `SeedSharedCompanyFilesAsync` (senseik-028) çağrısının hemen ardından çağrılıyor.

## Rebase Notu (bu oturum)

Branch `7f9452f`'den (dev'in 0d7261f noktası) güncel `dev`'e (`d5bc322`, 51 commit fark) rebase edildi. İki dosyada conflict çıktı, ikisi de "her iki taraf da sona ekleme yaptı" pattern'i — her ikisi de korunarak çözüldü:

- **`DataSeeder.DemoEmployees.cs`**: `NextDemoEmployeeInfo` imzası hem dev'in yabancı uyruk desteğini (`FixedDemoForeignAccounts`, senseik-009/F-02) hem bu kartın `userRoles`/`ContractType`/`EmploymentType` parametrelerini birlikte alacak şekilde birleştirildi — yabancı hesap dalı da normal dal da artık `ContractType`/`EmploymentType`'ı dolduruyor.
- **`DataSeeder.cs`**: `SeedSharedCompanyFilesAsync` (dev, senseik-028) ve `SeedEmployeeDocumentMetadataAsync` (bu kart) çağrıları art arda sıralandı — ikisi de bağımsız, sırası önemsiz.

Rebase sonrası hiçbir çakışma işareti (`<<<<<<<`/`=======`/`>>>>>>>`) kalmadı; tüm ilgili API'ler (`DocumentTypeDefinition.Create`, `EmployeeDocumentRequirement.Materialize`, `DbSet` isimleri) güncel Documents modülü şemasıyla imza bazında karşılaştırılıp doğrulandı.

## Test Sonuçları

- **`dotnet build Sense.slnx`** (Docker SDK 10.0 konteyneri): **0 hata**, 2200 uyarı (hepsi mevcut `xUnit1051` analyzer uyarısı, bu değişiklikle ilgisiz).
- **`Sense.Modules.Employee.Tests` → `EmployeeCompletenessTests`**: 4/4 geçti (MissingFieldCount 0/3/6 senaryoları + Draft istisnası).
- **Web `vitest run`** (tüm suite, 44 dosya / 510 test): 508 geçti, 2 kartla ilgisiz pre-existing hata:
  - `src/i18n.locales.test.ts` → `attendance.json` `de` — `teamTimesheet.*` anahtarları çevrilmemiş (puantaj motoru/senseik-014 tarafında kalmış, bu kartın dokunmadığı dosya).
  - `src/components/leave/leave-type-dialog.test.tsx` — 5sn timeout; dosya tek başına çalıştırıldığında 4/4 geçti (3.5sn içinde) → Docker host'undaki eşzamanlı konteyner yüküyle ilgili flaky timeout, gerçek regresyon değil.
  - Yeni `employee-card-grid.test.tsx` (3 test: sayı gösterimi, farklı sayı, rozet gizlenmesi) tam suite içinde geçti.
- **`Sense.Tests.Integration` → `DemoEmployeeSeedTests`** ve **`Sense.Modules.Documents.Tests`**: Bu oturumda Docker daemon'ı, muhtemelen bu makinede eşzamanlı çalışan başka oturumların (bkz. proje notu "paralel oturumlar aynı dizinde") yarattığı 40+ asılı `docker.exe` istemci süreciyle tıkandı — `docker ps` gibi temel komutlar bile yanıt vermedi. Bu iki test projesi bu yüzden bu oturumda tekrar koşulamadı; `dotnet build`'in 0 hatayla geçmesi ve `EmployeeCompletenessTests`'in (aynı `EmployeeCompleteness` sınıfını kullanan) geçmesi derleme/mantık doğruluğu için güçlü dolaylı kanıt, ama **`DemoEmployeeSeedTests`'in yeni `IsFixedDemoAccount`/`DemoContractInfoFor` testleri ve `SeedEmployeeDocumentMetadataAsync`'in gerçek bir DB'ye karşı çalıştığı hiç doğrulanmadı** — merge öncesi Docker sakinleştiğinde tek başına (başka ağır konteynerle eşzamanlı olmadan) çalıştırılmalı:
  ```
  dotnet test tests/Sense.Tests.Integration/Sense.Tests.Integration.csproj --filter "FullyQualifiedName~DemoEmployeeSeedTests"
  dotnet test tests/Modules/Sense.Modules.Documents.Tests/Sense.Modules.Documents.Tests.csproj
  ```
- **Web `tsc --noEmit` / `eslint`**: Aynı Docker tıkanıklığı nedeniyle bu oturumda koşulamadı. `vitest`'in `transform` aşaması tüm dosyaları TypeScript olarak derleyip geçtiği için tip hatası riski düşük, ama merge öncesi ayrıca doğrulanmalı.

## Doğrulama Turu (2026-09-19)

Branch güncel `dev`'e (`f85e44f`, senseik-043/029/038/039 merge'leri dahil) rebase edildi; `wip:` commit'i ve ara docs commit'i tek bir conventional commit'e birleştirildi. İki conflict çözüldü, ikisi de "iki taraf da ekledi" türünde: `DataSeeder.DemoEmployees.cs` (senseik-043'ün `DemoGenderFor`/`Gender: gender`'ı ile bu kartın `userRoles`/`ContractType`/`EmploymentType`'ı birleştirildi; iki `EmployeeInfo` dalı da artık üçünü birden dolduruyor) ve `DemoEmployeeSeedTests.cs` (iki kartın testleri de korundu).

**Düzeltilen hata — belge tipi kataloğu yarım seed'leniyordu.** `SeedEmployeeDocumentMetadataAsync` tenant'a sadece onboarding + zorunlu tipleri ekliyordu. `DocumentTypeProvisioningService.EnsureSeededAsync` ise tenant'ta *herhangi bir* tip varsa seed'i atlıyor (`AnyForTenantAsync`) — yani demo tenant'ta koşullu/isteğe bağlı/çalışma süresi/çıkış aşaması tipleri hiç oluşmayacak, "Belge tipleri" ayar sayfası ve `EmployeeHired` üzerinden çalışan gerçek materyalizasyon (koşullu tipler) kalıcı olarak eksik kalacaktı. Artık tam katalog seed'leniyor; örnek gereksinimler hâlâ sadece onboarding-zorunlu tiplerden üretiliyor. Worker'daki `EmployeeHired` materyalizasyonu mevcut gereksinimleri `FindAsync` ile atladığı için çakışma yok.

Gate sonuçları (host `dotnet` 10.0.401 + host node/`corepack pnpm`):
- `dotnet build src/Sense.Api`: 0 hata.
- `Sense.Modules.Employee.Tests` → `EmployeeCompletenessTests`: 4/4 geçti.
- `Sense.Tests.Integration` → `SeedData` (DemoEmployeeSeedTests + DemoOhsSeedTests; senseik-043 cinsiyet testleri ve bu kartın `IsFixedDemoAccount`/`DemoContractInfoFor` testleri dahil, conflict çözümünden sonra): 27/27 geçti.
- `Sense.Modules.Employee.Tests` tam suite: 218/222 — 4 hata Testcontainers tabanlı entegrasyon testlerinde (Payments/Analytics/Subcontractor/Provisioning), Docker daemon bu oturumda yanıt vermez hale geldi (`docker info` 30 sn timeout, `Docker.DotNet` `TaskCanceledException`). Hiçbiri bu kartın dokunduğu koda bağlı değil; Docker sağlıklıyken tekrar koşulmalı.
- Web `tsc --noEmit`: bu kartın dosyalarında 0 hata; `dev`'de zaten var olan 4 hata (`protected-route.test.tsx`, `home.test.tsx` mock alanları) karta ait değil.
- Web `eslint src`: 0 hata, 1 uyarı (`employee-list.tsx:168`, önceden var).
- Web `vitest` (`components/employees`, `pages/employees`, `i18n.locales.test.ts`): 184/185 — tek hata `attendance.json` `de` çevirisi (önceden var, kartla ilgisiz).
- Demo seed'in gerçek DB'ye karşı uçtan uca koşusu (Demo seed profili) bu oturumda yapılamadı (Docker); merge_gate manuel testinde doğrulanmalı.

## Kritik Dosyalar

`src/Modules/Employee/Sense.Modules.Employee.Application/Ports.cs` (MissingFieldCount), `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/{EmployeeDtos,EmployeeQueries}.cs`, `src/Sense.Api/SeedData/{DataSeeder,DataSeeder.DemoEmployees}.cs`, `web/src/pages/employees/employee-list.tsx`, `web/src/components/employees/employee-card-grid.tsx`, `web/src/services/employees.service.ts`, `web/public/locales/{tr,en,de}/employees.json`.

---
*Kaynak: 1. Tur Test Bulgu Dokümanı § C-05, 2. Tur Test Bulgu Dokümanı §4. Agent board: `docs/agent-board/cards/senseik-045-missing-info-badge.json`.*

# senseik-010 — SGK işyeri sicil no, meslek kodu, teşvik kodu (F-03) — UYGULAMA NOTU

## Durum (2026-09-19)

**Uygulandı ve `a/sgk-workplace-codes` üzerinde commit'lendi, `dev`'e rebase edildi (state: review). Push ve merge yapılmadı.** Açık iş: Employee/Recruitment testleri Docker sağlıklıyken yeniden koşulmalı (aşağıdaki "Test sonuçları" bölümüne bakın).

Bilinen riskler:
- `UpdateLocationRequest.SgkIsyeriSicilNo` isteğe bağlı ve varsayılanı null. Alanı göndermeyen bir istemci (ör. mobil ya da eski bir istemci) güncelleme yaptığında kayıtlı değer silinir. Web her zaman gönderiyor.
- `IncentiveDocument` gereksinimi sadece mevcut materialize tetikleyicilerinde (işe giriş/profil değişikliği vb.) hesaplanıyor. Bordro profilinde `IncentiveLawCode` değişince yeniden hesaplanmıyor; bunun için bir Payroll integration event'i gerekecek.

Bu kart önceki bir oturumda `a/employee-sicil-uyruk` (senseik-009) branch'i üzerinde yarım/commit'lenmemiş halde bırakılmıştı. senseik-009 zaten `dev`'e merge olduğu için o branch'teki commit'li kısım (704fa2f) değersizdi; asıl değer taşıyan uncommitted değişiklikler bu oturumda `dev`'den açılan temiz `a/sgk-workplace-codes` branch'ine taşındı (`git stash` + yeni branch + `git stash pop`) ve kabul kriterlerinin geri kalanı tamamlandı. Planın tam metni: `docs/handoffs/employee-sicil-uyruk.md` (senseik-009 + senseik-010 ortak planı; kod hâlâ orada anlatılan mimari kararları izliyor - bu dosya sadece 010'un kapanışını anlatıyor, employee-sicil-uyruk.md üzerine yazılmadı).

## Ne yapıldı (kabul kriterlerine göre)

### 1) Lokasyonlar ekranında SGK işyeri sicil numarası (per-location)

Devralınan kısım zaten büyük ölçüde tamamdı, bu oturumda doğrulandı + web ekranı eklendi:
- `Location.cs` (Organization.Domain): `SgkIsyeriSicilNo` (nullable, `Address` ile aynı trim/normalize deseni), `Create`/`Update`'e opsiyonel parametre.
- `ILocationLookup.GetSgkIsyeriSicilNoAsync(tenantId, locationIds)` (Contracts) + `LocationLookup` (Infrastructure) implementasyonu - Payroll'ün APHB hazırlığı için.
- Migration: `20260918174746_LocationSgkIsyeriSicilNo` (nullable `character varying(30)`).
- `LocationCommands.cs`/`LocationDtos.cs`/`LocationQueries.cs`/`LocationsController.cs`: create/update request+DTO'ya alan eklendi.
- Demo seed: İstanbul HQ lokasyonuna örnek sicil no (`DataSeeder.cs`) - aksi halde manuel QA'da alan hiç görünmüyordu.
- **Web (bu oturumda eklendi):** `web/src/services/locations.service.ts` (`Location`/`LocationInput` tiplerine `sgkIsyeriSicilNo`), `web/src/pages/locations.tsx` (form alanı, tablo kolonu, mobil kart görünümü). i18n: `tr`/`en`/`de` `locations.json`'a `sgkIsyeriSicilNo` anahtarı.
- Testler: `LocationTests.cs` (domain, trim/null), `LocationIntegrationTests.cs` (round-trip + lookup, gerçek Postgres).

### 2) Çalışan kartında SGK meslek kodu + import şablonu

**Zaten `dev`'de mevcuttu** (senseik-009'un Employee domain genişletmesi bu alanı da kapsamış, kart numarası 010 olsa da): `Employee.SgkOccupationCode`, `EmployeeInfo.SgkOccupationCode`, `EmployeeValidation.cs`, `EmployeeMasterImportTemplate.cs`'te `SgkOccupationCode` kolonu, `EmployeeImportValidator.cs`. Bu oturumda ek olarak:
- `IEmployeeLookup.GetSgkOccupationCodeAsync(tenantId, employeeId)` (Contracts) + `EmployeeLookup` (Infrastructure) implementasyonu eklendi - Payroll'ün cross-module okuması için (aksi halde meslek kodu Employee modülü dışına hiç çıkmıyordu, APHB hazırlığı için gerekli).
- Test: `EmployeeSicilNumarasiTests.cs`'e `GetSgkOccupationCodeAsync_ReturnsStoredCode_NullWhenNotSetOrEmployeeMissing` (gerçek Postgres).

### 3) Organizasyon/Şirket profil ekranında teşvik türü + teşvik belgesi bağlantısı

`employee-sicil-uyruk.md` §3'teki mimari karar uygulandı: **Company profiline yazılabilir teşvik alanı EKLENMEDİ** (iki doğruluk kaynağı riski - veri zaten `EmployeePayrollProfile.IncentiveLawCode`'da employee-bazlı duruyor). Bunun yerine üç parça:

1. **Teşvik belgesi bağlantısı (asıl eksik parça, artık bağlı):** `DocumentRequirementMaterializationService.cs`'teki `HasIncentive: false` sabiti kaldırıldı, yeni `Payroll.Contracts.IEmployeeIncentiveLookup.HasActiveIncentiveAsync(tenantId, employeeId, asOf)` çağrısına bağlandı. `IncentiveDocument` ("Teşvik belgesi", zaten Faz4'ten beri seed'liydi, `DocumentRequirementCondition.HasIncentive`) böylece ilk kez gerçekten materialize olabiliyor. `Documents.Application.csproj`'a `Payroll.Contracts` cross-module read contract referansı eklendi.
2. **Read-only özet (Company profil ekranı):** Yeni `Payroll.Application.Profiles.IncentiveSummaryFeatures.cs` (`GetActiveIncentiveSummaryQuery`/`Handler`, `payroll.profile.manage`) + `IEmployeePayrollProfileRepository.ListActiveAsOfAsync` (yeni repository metodu) tenant genelinde aktif `IncentiveLawCode`'ları employee sayısıyla grupluyor. Yeni `IncentivesController` (`GET /v1/payroll/incentives/summary`).
3. **Web:** `web/src/pages/company-profile.tsx`'e "Aktif Teşvik Türleri" kartı - salt okunur, sadece `payroll.profile.manage` izni olanlara görünür, `payroll.service.ts`'e `getActiveIncentiveSummary`. i18n: `tr`/`en`/`de` `company.json`'a `sectionIncentives`/`incentivesHint`/`incentiveBadge`/`noActiveIncentives`.

Testler: `IncentiveDocumentRequirementTests.cs` (yeni - WorkPermitDocumentRequirementTests.cs deseniyle, HasIncentive true/false), mevcut Documents testlerindeki `DocumentRequirementMaterializationService` constructor çağrılarına yeni `IEmployeeIncentiveLookup` parametresi eklendi (4 dosya), `AphbPrefillAndIncentiveSummaryTests.cs`'te `GetActiveIncentiveSummaryHandler` testi.

### 4) Bordro test: APHB/e-Bildirge çıktısında alanların ön-doldurulabilirliği

Gerçek APHB/e-Bildirge export'u bu kartın kapsamında değil (K-7 PO kararı: ilk sürüm dışı - `PayrollPeriodFeatures.cs` yorum satırı). Bu kart sadece üç alanın **birlikte sorgulanabilir** olduğunu kanıtlıyor:
- Yeni `Payroll.Application.Profiles.AphbPrefillFeatures.cs` (`GetAphbPrefillDataQuery`/`Handler`, `payroll.profile.manage`): `EmployeePayrollProfile.SgkWorkplaceLocationId` → `ILocationLookup.GetSgkIsyeriSicilNoAsync` (işyeri sicil no), `IEmployeeLookup.GetSgkOccupationCodeAsync` (meslek kodu), `profile.IncentiveLawCode` (teşvik kodu) - üçü tek DTO'da (`AphbPrefillDataDto`).
- `EmployeePayrollProfilesController`'a `GET .../profile/aphb-prefill` endpoint'i eklendi.
- Test: `AphbPrefillAndIncentiveSummaryTests.cs` - `GetAphbPrefillDataHandler_ComposesAllThreeFields_WhenProfileHasWorkplaceAndIncentive` (üç alan da dolu senaryo) + `..._NoProfileOrWorkplace_ReturnsNullsWithoutFailing` (eksik veri senaryosu, hata fırlatmıyor).

## Test sonuçları

2026-09-19, native `dotnet` 10.0.401, Testcontainers Docker Desktop üzerinden:
- `dotnet build src/Sense.Api` - 0 hata.
- `dotnet ef migrations has-pending-model-changes` (Organization) - "No changes have been made to the model since the last migration."
- Organization.Tests 113/113, Payroll.Tests 63/63, Documents.Tests 120/120, Sense.Tests.Architecture 56/56 - geçti.
- Employee.Tests 215/219, Recruitment.Tests 71/75 - 4'er başarısızlık. Bu koşu başka oturumların da kullandığı Docker daemon'u zorlarken yapıldı; ayrıntı almak için yapılan yeniden koşuda daemon yanıt vermeyi bıraktı (`DockerUnavailableException`, `docker info` askıda). Başarısız testlerin adları alınamadı, yani bu kartla ilgili olup olmadıkları doğrulanamadı. **Merge öncesi bu iki proje Docker sağlıklıyken tekrar koşulmalı.**
- Web: `eslint src` 0 hata (1 önceden var olan uyarı, `employee-list.tsx`). `tsc --noEmit` 4 hata verdi; hepsi dokunulmamış test dosyalarında (`protected-route.test.tsx`, `home.test.tsx`: `roleLabels`/`mfaSetupRequired`), dev'de de var.

Aşağıdaki test sınıfları bu kartla eklendi ya da değişti:
- Organization: `LocationTests`, `LocationIntegrationTests` (Testcontainers/Postgres)
- Employee: `EmployeeSicilNumarasiTests` (yeni `GetSgkOccupationCodeAsync` testi dahil)
- Payroll: `AphbPrefillAndIncentiveSummaryTests` (yeni)
- Documents: `IncentiveDocumentRequirementTests` (yeni), `AssetEventDocumentRequirementHandlersTests`/`EmployeeHiredDocumentRequirementHandlerTests`/`EmployeeProfileChangedDocumentRequirementHandlerTests`/`WorkPermitDocumentRequirementTests` (constructor imza değişikliği - `IEmployeeIncentiveLookup` eklendi)
- Recruitment: `RecruitmentIntegrationTests` (fake `ILocationLookup`'a `GetSgkIsyeriSicilNoAsync` eklendi - önceki oturumdan devralındı)

## Bilinçli olarak yapılmayanlar (kapsam dışı, not düşüldü)

- **PayrollCalculator'daki 5 puanlık SGK teşviki uygulaması** (`PayrollCalculator.cs:206` TODO) - bu kart sadece teşvik KODUNUN veri akışını/görünürlüğünü kapatıyor, hesaplamaya uygulanması ayrı bir P2+ işi (kartın orijinal handoff'unda da böyle işaretli).
- **Gerçek APHB/e-Bildirge dosya export'u** - K-7 PO kararıyla ilk sürüm dışı; bu kart sadece verinin hazır olduğunu kanıtlıyor.
- **Teşvik kanunu kodu kataloğu/doğrulaması** (serbest metin kabul ediliyor, `employee-sicil-uyruk.md` §3 madde 1'in önerdiği "data-driven küçük statik katalog" bu kartta kurulmadı - YAGNI, henüz gerçek ihtiyaç yok) - free-text `IncentiveLawCode` `EmployeePayrollProfilesController`'da zaten önceden vardı, değiştirilmedi.

## Kritik Dosyalar

- `src/Modules/Organization/Sense.Modules.Organization.Domain/Locations/Location.cs`
- `src/Modules/Organization/Sense.Modules.Organization.Contracts/ILocationLookup.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Contracts/IEmployeeLookup.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Contracts/IEmployeeIncentiveLookup.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Application/Profiles/AphbPrefillFeatures.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Application/Profiles/IncentiveSummaryFeatures.cs`
- `src/Modules/Documents/Sense.Modules.Documents.Application/DocumentTypes/DocumentRequirementMaterializationService.cs`
- `web/src/pages/locations.tsx`, `web/src/pages/company-profile.tsx`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § F-03. Plan: `docs/handoffs/employee-sicil-uyruk.md`. Agent board: `docs/agent-board/cards/senseik-010-sgk-isyeri-kod.json`.*

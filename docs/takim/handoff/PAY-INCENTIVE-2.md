# PAY-INCENTIVE-2 handoff: teşvik tanım/atama Application, API, persistence, izin, geçiş

Plan: `PAY-INCENTIVE-PLAN.md` ("Sözleşmeler", "Profil ilişkisi ve geçiş"). Kalıp: PAY-FORMULA-2. Dal `takim/bordro-tesvik-modelleri`, COMMIT YOK.

## Ne yapıldı
- **Application** (`Payroll.Application/Incentives/`):
  - `IncentiveDefinitionFeatures.cs`: list/get (okuma `payroll.trace.view`, `approvedByName` `IUserProfileLookup` ile), `templates` (alreadyAdded), `parameter-catalog` (`IncentiveParameterCatalog.Keys`: `Payroll.Sgk.*`, `Payroll.Unemployment.*`, `Payroll.MinimumWage.Gross`; `ILegislationParameterReader.ListAsync` + `Resolve(bugün)`; yürürlükte olmayan anahtar listede yok), create, from-template (404/409 `template_already_added`/`code_taken`), update, approve/revoke (`payroll.incentive.approve`), activate/deactivate. Onayda legislationKey varlığı kontrolü (`IncentiveLegislationKeys.VerifyExistAsync`; yalnız tanım tam ise, eksikse domain `parameters_incomplete(field)`).
  - `IncentiveAssignmentFeatures.cs`: list (employeeId|periodId), toplu create (≤500 FluentValidation, hep-ya-hiç), PUT, DELETE, `IncentiveChangeFlagger` (`incentive_changed`).
  - `LegacyIncentiveCodeFeatures.cs`: `LegacyIncentiveCodes.Normalize` (yalnız rakam, 5 haneye soldan sıfır), önizleme (`calculationChanges` = önerilen tanım var ve (onaylı && kind a) değil), convert (çakışan SGK atamalı çalışan `skippedEmployeeIds`, kapalı ay `assignment_period_closed` ile tümü reddedilir, profil koduna dokunulmaz).
  - `IncentiveMonthPlan.cs`: `LoadAsync(ay, assignmentRepo, definitionRepo)` = 2 sorgu; `ForEmployee`, `EffectiveForEmployee` (tanım aktif+tarih aralığında), `EffectiveOfCategory` (slice 3 tüketir).
- **API**: `IncentiveDefinitionsController`, `IncentiveAssignmentsController`, `IncentivesController` sonuna `legacy-profile-codes` (GET) ve `legacy-profile-codes/convert` (POST). Mevcut `/incentives/summary` değişmedi.
- **İzin**: `payroll.incentive.manage`, `payroll.incentive.approve` (`PayrollPermissions`, `All` sonuna; resx tr/en). `payroll.dirty.reason.incentive_changed` resx tr/en. `PayslipDirtyFlagReasons.IncentiveChanged`.
- **Persistence**: `PayrollDbContext` (DbSet + `IncentiveDefinitionConfiguration`/`EmployeeIncentiveAssignmentConfiguration`), `Repositories.cs` sonuna iki repo, DI `AddPayrollServices`. `Parameters` = `OwnsOne(...).ToJson("parameters")` (jsonb; alan adları camelCase `rate.value|legislationKey`, `coverageBase` "minimumWage"/"actualSgkBase", `capMultiplier`, `employerSgkRate`, `covers*`). Tekil `(tenant_id, code)`; kısmi tekil `(tenant_id, template_key) WHERE template_key IS NOT NULL`; atama indeksi `(tenant_id, employee_id, valid_from_month)`, `incentive_definition_id`; FK Restrict.
- **Migration**: `20261013015216_AddIncentives` `dotnet ef migrations add` ile üretildi (elle yazılmadı); snapshot yalnız ekleme; `Migrate` pending-model-changes hatası vermiyor (entegrasyon testleri geçti).
- **Domain'e küçük ekleme (dilim 1 kodu)**: `IncentiveParameters.Copy()` (derin kopya) ve `IncentiveDefinition.Normalize` bunu kullanıyor: `IncentiveParameters.Empty`/şablon parametreleri statik paylaşılan örnek; EF owned JSON'da aynı örneğin iki sahibe bağlanmasını önler.
- **Proje referansı**: `Payroll.Application.csproj` -> `Identity.Contracts` (yalnız `IUserProfileLookup`, onaylayan adı için; Contracts-only, mimari kural bozulmadı).

## Kriterler -> kanıt
1. Uçlar/DTO/hata kodları/izinler: `Controllers/Incentive*Controller.cs`, `Application/Incentives/*`; `IncentiveDefinitionFeaturesTests.EveryRequest_DeclaresItsPermission` (17 istek, izin anahtarları), `ThePermissions_AreRegisteredWithPayroll`; 403: `Approve_WithOnlyTheManagePermission_IsForbidden...`, `Create_WithOnlyTheReadPermission...`, `Assign_WithoutThePermission...`, `Convert_WithoutThePermission...` (gerçek `AuthorizationBehaviour`).
2. Şablon iki kez 409: `FromTemplate_TwiceForTheSameTemplate_IsAConflict`, `TemplateHandler_PersistsThe5510TemplateApprovedBySystem` (Postgres). Onay kim/ne zaman/not: `Approve_RecordsWhoWhenAndTheNote...`; geri alma: `Revoke_ClearsTheApproval_KeepsTheNote...`; hesap alanı onayı düşürür: `Update_OfACalculationField_DropsTheApproval` + `Update_ReplacesTheParameters_AndPersistsTheDroppedApproval` (db); yalnız ad: `Update_OfTheNameOnly_KeepsTheApproval`.
3. Dirty flag: `Approve_...FlagsTheAssignedEmployees`, `Update_FlagsOnlyEmployeesAssignedInOpenPeriods`, `Deactivate_ThenActivate_FlagsOnlyOnARealChange`, `Assign_ToSeveralEmployees_...FlagsThemInOpenPeriods`, `Update_EndsAnOpenAssignment_...`, `Delete_...`; Postgres: `AssignHandler_PersistsAssignments_FlagsOnlyOpenPeriods...`, `DefinitionChanges_FlagEveryEmployeeAssignedInAnOpenPeriod_ButNotEndedOnes`.
4. Geçiş: `Normalize_*` ("5510"->"05510"), `LegacyPreview_MapsCodesToDefinitions_AndSaysWhenTheCalculationChanges`, `LegacyPreview_AnUnapprovedRateDiscount_ChangesTheCalculation`, `Convert_CreatesAssignmentsForTheCodeHolders_AndLeavesTheProfilesAlone`, `Convert_SkipsEmployeesWithAnOverlappingSgkAssignment_AndReportsThem`, `Convert_ToAClosedMonth_IsRefused...`, Postgres `LegacyConversion_OverRealRepositories_...`.
5. `IncentiveMonthPlan`: `MonthPlan_LoadsAssignmentsAndDefinitionsOnce_ForAnyNumberOfEmployees` (1 + 1 sorgu), `MonthPlan_OverRealRepositories_...`.
6. Migration: yukarıda; kısmi indeks `TemplateKey_IsUniquePerTenantOnlyWhenSet`, jsonb `Parameters_AreStoredAsCamelCaseJsonb_InTheSameShapeAsTheApi`, `CoverageDefinition_RoundTrips...`, `DraftWithoutParameters_AndTaxAndDeclarationOnlyKinds_RoundTrip`.
7. Kiracı yalıtımı: `Definitions_AreInvisibleToOtherTenants`, `Assignments_AreInvisibleToOtherTenants_...`, `Code_IsUniquePerTenant_...`.

## Kararlar / dikkat
- Convert'te `fromYear/fromMonth` istekten gelir (UI "ilk açık ay"ı önermeli); kapalı ay kapsarsa tüm istek `incentive_assignment_period_closed`. Çalışan seçimi: bugün etkin profil kodunun normalize hali = istek kodunun normalize hali. Önizleme `asOf` alır; convert almaz (bugün).
- Convert yalnız Sgk kategorili tanım kabul eder (aksi `incentive_parameters_invalid(field=kind)`); tanımın DeclarationCode'unun kodla eşleşmesi zorlanmaz.
- >500 çalışan: yeni hata kodu eklenmedi, FluentValidation (genel doğrulama hatası).
- Çalışan yok/yabancı: mevcut `employee.not_found` (PayItemEmployeeNotFound) yeniden kullanıldı.
- Çalıştırma uyarı/atlama kodları (`incentive_not_approved` vb.) dilim 3'te (hesap tarafı).

## Gözden geçirme düzeltmeleri (RET sonrası)
- `UpdateIncentiveDefinitionHandler` artık `IEmployeeIncentiveAssignmentRepository` alır: tanımın ataması varsa (`ListByDefinitionAsync(id, DateOnly.MinValue)`) (a) Sgk<->Tax kategori değişimi `incentive_kind_category_locked` (Conflict; yeni hata kodu PayrollDomainErrors + resx tr/en sona), (b) Sgk kategorisinde DeclarationCode silinmesi `declaration_code_required`. Kategori içi a<->b<->d serbest; atamasız tanımda serbest. Testler: 4 birim + 1 Postgres.
- `incentive_legislation_key_invalid` resx metni tr/en "geçersiz veya bulunamadı" anlamına genişletildi.

## UI (dilim 5) için bilgi notları
- Geçiş önizlemesi HAM koda göre gruplar ("5510" ve "05510" ayrı satır), ama convert NORMALİZE koda göre seçer: tek convert çağrısında "5510" ve "05510" taşıyan çalışanların hepsi birlikte dönüşür. UI bunu göstermeli.
- `PUT /incentive-definitions/{id}`'de `validTo` gönderilmezse silinir (ve takvim alanı değiştiği için onay düşer); validFrom gönderilmezse mevcut değer korunur.
- Şablondan eklenen tanımın `ValidFrom`'u bugündür; önceki açık aya dönüştürülen/atanan atama o ayda etkin sayılmaz (`IsEffectiveInMonth`). UI ValidFrom'u düzenletmeli veya uyarmalı.

## Açık konular (kapsam dışı, dilim 3+ veya karar)
- Tanım değişiminde işaretleme çalışan başına `FlagAsync` sorgusu yapar (PAY-FORMULA ile aynı); büyük kiracıda toplu işaretleme düşünülebilir.
- Riskli alan: `Sense.Modules.Payroll.Infrastructure/Persistence/Migrations/**` (yeni `AddIncentives` + snapshot); insan onayı gerekir.

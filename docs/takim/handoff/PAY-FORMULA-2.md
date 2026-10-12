# PAY-FORMULA-2 handoff: ödeme türü tanımı ve çalışana atama

## Ne yapıldı
Plandaki "Sözleşmeler" birebir uygulandı (`api/v1/payroll`, camelCase, enumlar camelCase string).

- **Domain** (`Payroll.Domain/PayItems/`): `PayItemDefinition` (kod regex `^[A-Z][A-Z0-9_]{1,31}$`, ad <=100, Deduction reddi, Net -> üç bayrak zorunlu, tarih aralığı, formül `PayrollFormula.Parse` ile kayıtta doğrulanır, `UsesValue`, `IsEffectiveInMonth`), `EmployeePayItemAssignment` (ayrı aggregate; ay = ayın 1'i; `Create/Update/EnsureDeletable`, `ExceedsMonthlyLimit`), `PayItemEnums`. `PayrollLimits.MaxPayItemsPerEmployeeMonth=50`, `MaxPayItemAssignmentEmployees=500`. Repository arayüzleri `Repositories.cs` sonuna eklendi.
- **Application** (`Payroll.Application/PayItems/`): tanım list/get/create/update/activate/deactivate; `validate-formula` (her zaman başarı sonucu, `FormulaValidationDto`); `formula-catalog` (`PayItemSampleValues` sabit örnekler); atama list(employeeId|periodId)/toplu create/update/delete; `PayItemChangeFlagger` (işaretleme). `PayslipDirtyFlagService.FlagAsync`'e geriye uyumlu opsiyonel `throughMonth` parametresi eklendi (uç aylı atama için bitişten sonraki açık dönemler işaretlenmesin).
- **API**: `PayItemsController` (`/pay-items`), `PayItemAssignmentsController` (`/pay-item-assignments`). `errorMessage` controller'da `Localizer.Message(error)` ile üretilir (DTO'daki `[JsonIgnore] Error` yalnız bunun için).
- **Persistence**: `PayrollDbContext` konfigürasyonları, `PayItemDefinitionRepository`, `EmployeePayItemAssignmentRepository`, DI (`AddPayrollServices`; Worker zaten `AddPayrollServices` çağırıyor, ayrı blok gerekmedi - komut/sorgu handler'ları API'de).
- **Migration** `20261012224406_AddPayItems` `dotnet ef migrations add` ile üretildi. Tablolar `payroll_pay_item_definitions` (unique `tenant_id+code`), `payroll_pay_item_assignments` (`tenant_id, employee_id, valid_from_month` indeksi, tanıma FK Restrict). `value`/`default_value` numeric(18,4).
- **İzin**: `payroll.payitem.manage` (`PayrollPermissions.PayItemManage`, `All` sonunda). Okuma uçları `payroll.trace.view`. `SystemRoleDefinitions` değişmedi (senkronizer otomatik verir).
- **Hata kodları**: plandaki 12 `payroll.errors.pay_item_*` + `PayItemEmployeeNotFound` (= mevcut `employee.not_found` anahtarı, yeni resx gerekmedi). resx tr/en: hata kodları, izin adı, `payroll.dirty.reason.pay_item_changed`. `PayslipDirtyFlagReasons.PayItemChanged = "pay_item_changed"`.

## Kurallar / kararlar (slice 3-5 bilmeli)
- **Çakışma (ana oturum kararı):** aynı çalışana aynı tanımın tarihleri çakışan ikinci ataması `payroll.errors.pay_item_assignment_overlap` (409); farklı tanımlar serbest. Oluşturmada (hep-ya-hiç) ve PUT bitiş ayı değişiminde uygulanır; uygulama katmanı sorgusu, migration yok. Slice 3: bir çalışan-tanım-ay için en fazla bir atama vardır.
- **Kapalı dönem** = durumu Open olmayan dönem (Approved/Paid/Locked). Dönemi hiç açılmamış ay (gelecek ya da henüz açılmamış geçmiş) kapalı sayılmaz.
- Atama başlangıcı kapalı aysa `pay_item_assignment_period_closed`. Update: kapalı ay kapsamı ve o aylardaki değer değişmez (bitiş ayı kapalı ayları kesemez/yeni kapalı ayı kapsayamaz; değer kapsanan kapalı ay varken değiştirilemez); silme yalnız kapsadığı hiçbir ay kapalı değilse. Pasif tanımın ataması yine kapatılabilir (update) ama yeni atama `pay_item_inactive`.
- Toplu atama hep-ya-hiç: herhangi bir çalışanda hata -> hiçbiri yazılmaz. 500 sınırı tekilleştirilmiş çalışan sayısına; bilinmeyen/başka kiracı çalışanı `employee.not_found` (404). 50 kalem sınırı çakışan aylardaki eşzamanlı sayıya göre (`ExceedsMonthlyLimit`); ihlalde `pay_item_assignment_too_many` (500 sınırı da aynı kod).
- `deger` kullanan formül + atamada değer yok + tanımda `DefaultValue` yok -> `pay_item_assignment_value_required`. Slice 3: değer = `assignment.Value ?? definition.DefaultValue`.
- Tanım değişimi/aktif-pasif: açık dönemlerin en erkenine göre bitmemiş atamaları olan çalışanlar, kendi atama aralığıyla işaretlenir (`PayItemChangeFlagger.FlagDefinitionChangedAsync`). Durumu değişmeyen activate/deactivate işaretlemez.
- `validate-formula`: parse hatası da, örnek değerlerle değerlendirme hatası da (`division_by_zero`, `overflow`) `isValid=false` döner (HTTP 200). `sampleValues` anahtarları küçük harfe çevrilir, bilinmeyenler yok sayılır. `amountBasis` şimdilik kullanılmıyor (sözleşme için kabul edilir).
- Kod yarışı (aynı anda iki create) unique indeks ihlali olarak 409 değil exception olur (mevcut Union kalıbıyla aynı; ön kontrol 409 verir).

## Test
`tests/Modules/Sense.Modules.Payroll.Tests/PayItems/`: `PayItemDomainTests` (kod regex, Net bayrakları, Deduction, tarih, formül hatası, atama kuralları, 50 kalem), `PayItemFeaturesTests` (NSubstitute: 409, 500/501, kapalı dönem, değer zorunlu, 51. kalem, işaretleme, katalog, validate, izin öznitelikleri, `AuthorizationBehaviour` ile 403), `PayItemIntegrationTests` (Postgres: migration uygulanır, unique kod, kiracı yalıtımı, gerçek repo ile işaretleme).

## Açık konular
- Tanım değişiminde işaretleme çalışan başına `FlagAsync` sorgusu yapar; binlerce atamada toplu bir `FlagManyAsync` düşünülebilir.

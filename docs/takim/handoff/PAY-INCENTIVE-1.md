# PAY-INCENTIVE-1 handoff: teşvik tanımı ve atama domain modeli (saf)

## Ne yapıldı
`Payroll.Domain/Incentives/` (persistence/EF/migration yok, dilim 2):
- `IncentiveEnums` (`IncentiveKind` a/b/c/d, `IncentiveCategory` Sgk/Tax, `IncentiveCoverageBase`).
- `IncentiveParameters` (+ `IncentiveRate {Value | LegislationKey}`): `Validate(kind)` yalnız alan biçimi (rate value (0,1], value XOR key, `Payroll.` önekli <=100 key, capMultiplier (0,10], a/c'de coverage alanları ve d'de her parametre reddi); `FirstMissingField(kind)` onay tamlığı; `LegislationKeys` (anahtarın varlığı onay anında Application'da kontrol edilecek).
- `IncentiveDefinition`: kod regex, ad/dayanak sınırı, DeclarationCode yalnız rakam 1..10, MaxMonths 1..120, tarih aralığı, `Category`, `IsComplete`, `Approve(userId, now, note)` (tamlık şart; Sgk'da DeclarationCode yoksa `declaration_code_required`), `RevokeApproval(note)`, `Update` (Kind/Parameters/DeclarationCode/MaxMonths/ValidFrom/ValidTo değişirse onay düşer; Name/LawReference kalır; hatalı güncelleme hiçbir şeyi değiştirmez), `Activate/Deactivate`, `IsEffectiveInMonth`, `CreateFromTemplate`.
- `EmployeeIncentiveAssignment`: PayItem kalıbı + kategori çakışması (Conflict `incentive_assignment_overlap`), MaxMonths (bitiş zorunlu, çalışanın aynı tanımdaki toplam ayı; açık uçlu kardeş atama limiti aşmış sayılır), Sgk'da DeclarationCode zorunlu, kapsanan HERHANGİ bir kapalı ay reddi, Update'te kapalı ay kesme/kapsama reddi + çakışma + MaxMonths, `EnsureDeletable`. Onaysız tanıma atama serbest.
- `IncentiveTemplates` (5 şablon; 5510 yalnız `Payroll.Sgk.EmployerDiscountRate` anahtarı, "05510", onaylı, not "Sistem: mevcut 5 puan davranışı"; diğerleri boş/onaysız).
- `Repositories.cs` sonuna `IIncentiveDefinitionRepository` (+`TemplateExistsAsync`), `IEmployeeIncentiveAssignmentRepository`.
- `PayrollDomainErrors` sonuna 18 `Incentive*` kodu; resx tr/en sonuna metinler.

## Dilim 2'nin bilmesi gerekenler
- Assignment `Create/Update` Application'dan şunları ister: çalışanın tüm atamaları (`existing`) ve `categoryOf(definitionId)` (tanımları `ListByIds` ile yükleyip eşleyin).
- HTTP türleri: domain'de `assignment_overlap` Conflict döner; `code_taken` ve `template_already_added` Application'da `Error.Conflict` ile üretilmeli (domain bunları üretmez). `not_found`, `assignment_not_found`, `template_not_found`, `parameters_incomplete` legislation-key-yok kontrolü Application'da.
- Plan'da karşılığı olmayan hatalar mevcut kodlarla eşlendi: dayanak >200 -> `name_invalid`; maxMonths aralığı, onay/atama notu >500, tanımsız enum -> `parameters_invalid(field=maxMonths|approvalNote|note|kind)`; `parameters_incomplete` ve `parameters_invalid` args `field` taşır; `assignment_exceeds_max_months` args `maxMonths`.
- Sistem onaylı şablonda `ApprovedByUserId` null (kullanıcı yok), `ApprovedAt` = oluşturma anı. `RevokeApproval(note)` notu `ApprovalNote`'ta saklar; calculation-field düzenlemesiyle düşen onayda not temizlenir.
- `IncentiveParameters` init-only record, `OwnsOne(...).ToJson()` için uygun; `IncentiveRate` iç içe record (sahip tip olarak eşlenmeli).
- Sgk kategorisinde DeclarationCode kontrolü Update'te değil yalnız atama oluşturmada (plan: "atama/onay için").

## Test
`tests/Modules/Sense.Modules.Payroll.Tests/Incentives/`: `IncentiveDefinitionTests`, `IncentiveAssignmentTests`, `IncentiveTemplateTests` (116 test, hepsi geçti).

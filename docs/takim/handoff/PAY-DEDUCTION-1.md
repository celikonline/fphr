# PAY-DEDUCTION-1: formüllü kesinti (Direction = Deduction) - dotnet

Plan: `PAY-DEDUCTION-PLAN.md`. Altyapı: PAY-FORMULA-1/2/3. Migration YOK (Direction kolonu mevcuttu; model değişmedi).

## Ne yapıldı
- **Tanım** (`PayItemDefinition`): `Direction = Deduction` kabul edilir. Üç vergi bayrağı kayıtta ne gelirse `false` saklanır (reddedilmez; DTO false döner). `AmountBasis = Net` + kesinti -> `payroll.errors.pay_item_deduction_basis_invalid` (Validation). Enum dışı yön hâlâ `pay_item_direction_not_supported`. Net-kazanç kuralı (`pay_item_net_requires_fully_taxable`) yalnız kazançta.
- **Direction kilidi** (`UpdatePayItemHandler`, ctor'a `IEmployeePayItemAssignmentRepository` eklendi): yön değişiyorsa ve tanımın herhangi bir ataması varsa (`ListByDefinitionAsync(id, DateOnly.MinValue)`) -> `payroll.errors.pay_item_direction_locked` (Conflict). Yön aynıysa depo çağrılmaz.
- **Motor** (`PayrollCalculator`, `PayItemDeduction`, `PayrollCalculationInput.PayItemDeductions`): DEDUCTIONS sırası icra -> avans -> formüllü kesintiler (kod sırası, ordinal) -> sendika. Formüllü toplam `max(0, netBeforeDeductions - icra - avans - sendika)` tavanını aşamaz (sendika kırpılmaz, ama tavanı düşürür); aşan kısım SON koddan başa doğru kırpılır (`PayItemDeductionCap`). Brüt/SGK/GV/damga/işveren maliyeti değişmez. `PayrollCalculationResult.CappedPayItemDeductionCodes` (kırpma sırasıyla: son kod önce).
- **İz**: kesinti başına `PAY_ITEM` adımı DEDUCTIONS'tan hemen ÖNCE (kazanç PAY_ITEM'ları EARNINGS öncesindeki yerinde): Inputs `code,name,direction=deduction,deductionAmount (formül tutarı),variable.*`, kırpıldıysa `capped=1`; **Output = fiilen düşülen tutar** (kırpma sonrası). DEDUCTIONS: `payItemDeductionsTotal` + formül metnine "formüllü kesintiler (kalan nete kadar, kod sırası)" YALNIZ kesinti varken; NET açıklaması da yalnız o zaman genişler. Kesintisiz iz bire bir aynı.
- **Servis**: `PayItemEvaluation.Earnings/Deductions` (iki liste), `EvaluatedPayItem.IsDeduction`. Kesinti `PayItemDeduction`'a çevrilir ve YALNIZ son `BuildInput`'a girer; değişken girdisine ve `SolveExtra` (net->brüt) çözücüsüne girmez. Kırpma: `PayrollWarning("pay_item_deduction_capped", Details = kırpmada İLK kesilen kalem kodu = kod sırasına göre en sondaki)`. Negatif sonuç: 0 + mevcut `pay_item_negative_result`. Formül hatası: mevcut `pay_item_formula_failed` atlaması. Önizleme kesintiyi ve uyarıyı içerir.
- **Pusula** (`PayslipDocumentBuilder`): `direction=deduction` PAY_ITEM adımları Kazançlar'dan ayrılır, "Kesintiler" bölümünde kalem adıyla ayrı satır (PDF + düz metin); "İcra/Nafaka/Avans Kesintileri" satırı DEDUCTIONS - formüllü toplamıdır (çift sayım yok; sendika hâlâ bu satırda). Kesintisiz pusula aynen.
- **pay-item-lines**: `PayItemLineDto` sona `Direction` ("earning" | "deduction", varsayılan "earning", hep dolu) aldı. Kesintide `Amount` = fiilen düşülen tutar.
- **Çeviri**: resx tr/en SONA `pay_item_deduction_basis_invalid`, `pay_item_direction_locked`, `payroll.warning.pay_item_deduction_capped`; web `payroll.json` tr/en/de `warnings.pay_item_deduction_capped` (SONA).

## Testler
Yeni: `PayItems/PayDeductionCalculationTests` (motor, elle hesap: brüt 100.000 -> kesinti öncesi net 75.543,70; kesinti 1.000 -> 74.543,70; sıra, kırpma, deterministik sıra), `PayDeductionFeaturesTests` (kabul/normalizasyon/basis/kilit), `PayDeductionDocumentTests` (pusula + lines), `PayrollPeriodCalculationServiceDeductionTests` (servis: brut*0.01, kazanç+kesinti, kırpma+uyarı, negatif, formül hatası, net kalem, kıst/günlük ücretli `ucretli_gun`, önizleme). `PayrollPeriodCalculationServiceTests` `partial` yapıldı (yardımcıları paylaşmak için).
Davranışı bilerek değişen 3 eski test (silinmedi, yeni davranışa çevrildi): `PayItemDomainTests.Deduction_IsRejected` -> `Deduction_IsAccepted_AndTaxFlagsAreNormalized`; `PayItemDomainTests.Update_KeepsTheCode_AndRevalidates` (ret örneği Net-basis kesinti); `PayItemFeaturesTests.Create_RejectsADeduction_AndABadFormula` -> `..._RejectsADeductionWithNetBasis_...`; ayrıca `UpdatePayItemHandler` ctor çağrıları (3 yer) yeni parametre için.

## BASİTLEŞTİRMELER / SMMM soruları
1. Sıra icra -> avans -> formüllü -> sendika ve "kalan nete kadar, son koddan geriye kırpma" kuralı uygun mu?
2. KARAR (koordinatör): sendika aidatı kırpılmaz ve eski davranış aynı kalır; formüllü kesintilerin kırpma tavanı sendikayı da düşer, böylece net >= 0 (test: `Deductions_AreCappedAfterTheSendikaAidati_...`). "İlk kırpılan kalem" = kırpmada ilk kesilen (kod sırasına göre en sondaki).
3. BES / özel sağlık gibi vergi matrahını düşüren kesintiler kapsam dışı (hep net kesinti).
4. Kesinti toplamının yasal üst sınırı (asgari ücret / ücret koruması, 4857 md.35) uygulanmıyor; yalnız net >= 0.
5. Kırpılan tutar sonraki aya devretmez (kaybolur); uyarı verilir.
6. İz: kırpılmış kesintinin PAY_ITEM Output'u fiilen düşülen tutardır, formül tutarı `deductionAmount` girdisindedir.

## Açık konu (eski not, Docker sonradan düzeldi)
Docker Desktop bu makinede başlamıyordu ("unable to start"), `senseik-test-postgres` erişilemedi: Postgres gerektiren testler (Payroll `*IntegrationTests`, `PayslipDatabaseIntegrationTests` vb.) bu oturumda koşamadı; DB'siz tüm Payroll testleri (934) yeşil, `dotnet build Sense.slnx` yeşil. Model/migration değişmediği için bu testlerin etkilenmesi beklenmez.

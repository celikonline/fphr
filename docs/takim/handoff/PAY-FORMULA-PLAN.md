# PAY-FORMULA: formülle tanımlanan yan ödemeler (Bakanlık sözü madde 3)

Plan: `takim-orkestrator` (2026-10-13). Dilimler sırayla birleşir: PAY-FORMULA-1 → 2 → 3 → 4 → 5. İş kartları `docs/takim/isler/PAY-FORMULA-*.json`.
Madde 5 (ek kazanç/kesinti formülleri) aynı altyapıyı kullanacak; bu işte yalnız kazanç (`Earning`) yönü uygulanır, `Deduction` alanı vardır ama reddedilir.

## Verilen kararlar (açık sorular)
- Formül tanımlama/atama yetkisi: yeni izin `payroll.payitem.manage` (İK/Bordro/Owner rolleri `SystemRolePermissionSynchronizer` ile otomatik alır; `SystemRoleDefinitions` değişmez). Okuma uçları mevcut `payroll.trace.view`.
- Atamada tek parametre `deger`. Toplu atama: çok çalışan seçimi (en fazla 500).
- Kapalı dönemlerde kullanılmış tanımın formülü düzenlenebilir; yalnız açık/gelecek dönemleri etkiler (formül metni hesap izinde saklanır).
- Fonksiyon adları Türkçe + İngilizce birlikte. Ondalık ayırıcı yalnız nokta; virgül yazılırsa `payroll.formula.decimal_comma`.
- Basitleştirmeler: formül başka kaleme başvuramaz; kısmi/limitli istisna yok (bayraklar tam tabi/tam muaf); net kalem yalnız tam vergiye tabiyse; kıst ay oranlaması formülün işi; negatif sonuç 0'a çekilir + uyarı; `PayrollAdjustment` olduğu gibi kalır; APHB/çarşaf/kıdem tazminatı giydirilmiş ücret/muhasebe fişi kapsam dışı.

## Tasarım

### PayItemDefinition (Payroll, `Domain/PayItems/`, kurum geneli)
`Code` (`^[A-Z][A-Z0-9_]{1,31}$`, kurum içinde tek, değişmez), `Name` (≤100), `Direction` (Earning/Deduction; Deduction `pay_item_direction_not_supported`), `AmountBasis` (Gross/Net), `IsSgkSubject`/`IsIncomeTaxSubject`/`IsStampTaxSubject` (varsayılan true; Net kalemde üçü true zorunlu `pay_item_net_requires_fully_taxable`), `Formula` (≤500; sabit tutar da formüldür: `deger`), `DefaultValue?`, `IsActive`, `ValidFrom`/`ValidTo?`.

### EmployeePayItemAssignment (ayrı aggregate, EmployeeId taşır)
`EmployeeId`, `PayItemDefinitionId`, `Value?` (formüldeki `deger`), `ValidFromMonth` (ayın 1'i), `ValidToMonth?`. Tek seferlik: başlangıç=bitiş ayı. Bir çalışana bir ayda en fazla 50 kalem (adlandırılmış sabit). Geçmişe (açık olmayan döneme) atama yok `pay_item_assignment_period_closed`. Silme yalnız başlangıç ayı açık/gelecekteyse; kullanılmış atama bitiş ayı verilerek kapatılır. Mevcut `EmployeeUnionMembership`/`Union` kalıbını ve `PayslipDirtyFlagService.FlagAsync` kalıbını kullan.

### Formül dili (elle yazılmış ayrıştırıcı, `Domain/PayItems/Formulas/`)
`PayrollFormula.Parse(text) → Result<PayrollFormula>`, `Evaluate(variables) → Result<decimal>`. Yalnız decimal, kültürden bağımsız. İşleçler `+ - * /`, unary eksi, parantez; karşılaştırma `< <= > >= = !=` (1/0); mantık `ve/and`, `veya/or`, `degil/not`. Fonksiyonlar: `min`, `max` (≥2 arg), `yuvarla/round(x[,n])` (n 0..4, yarım sıfırdan uzağa), `asagi/floor`, `yukari/ceil`, `mutlak/abs`, `eger/if(koşul,a,b)` (yalnız seçilen dal hesaplanır). Argüman ayırıcı `,` veya `;`.
Güvenlik: kod yürütme/reflection/regex yok; döngü/atama/kullanıcı fonksiyonu yok. Adlandırılmış sınırlar: metin ≤500, token ≤200, derinlik ≤20, tanımlayıcı ≤40, sayı ≤20 karakter. Bilinmeyen değişken/fonksiyon kayıt anında konumla reddedilir. Sıfıra bölme / taşma (|sonuç|>1e9 veya OverflowException) `Result` hatası, exception fırlatılmaz.
Hata kodları: `payroll.formula.{empty,syntax,unknown_variable,unknown_function,argument_count,too_long,too_deep,division_by_zero,overflow,decimal_comma}`; `Error.Args`: position,name,max; `SharedResource{,.en}.resx`'e.
Web'e ayrıştırıcı kopyalanmaz; doğrulama `validate-formula` ucu.

### İzinli değişkenler (saf `PayItemVariables`)
`brut` (esas brüt, kıst/günlük-saatlik oranlaması uygulanmış, fazla mesai ve diğer kalemler hariç; `PayrollCalculator.BaseSalary(input)` ortak yardımcı), `aylik_ucret`, `net_hedef` (net ücretlide anlaşılan net, brütte 0), `gunluk_ucret` (aylik/30), `saatlik_ucret` (aylik/225, `PeriodWageMath` ile tutarlı), `calisilan_gun`, `ucretli_gun` (günlük/saatlikte wagePaidDays, aylıkta paidDays30), `takvim_gunu`, `eksik_gun`, `kidem_yili` (işe giriş→dönem sonu tam yıl; bilinmiyorsa 0), `asgari_ucret` (`snapshot.MinimumWageGross`), `fazla_mesai_saati`, `ay`, `deger`.

## Hesaba entegrasyon
- `PayrollCalculationInput.PayItems: IReadOnlyList<PayItemEarning>?` (null = eski davranış). `PayItemEarning(Code, Name, GrossAmount, SgkSubject, IncomeTaxSubject, StampTaxSubject, Formula, UsedVariables, NetTarget?)`. Formül Application katmanında değerlendirilir, motor saf kalır.
- `PayrollCalculator`: gross = ücret + AdditionalTaxableEarnings + kalem toplamı. `SGK_BASE` = clamp(gross − SGK'dan muaf kalemler + yemek SGK aşımı, taban, tavan); `GV_BASE` = gross − GV'den muaf + yemek GV aşımı − SGK işçi − işsizlik işçi; `STAMP_TAX` = (gross − damgadan muaf) × oran. Asgari ücret istisnaları/kesintiler/icra tavanı değişmez.
- Trace: `EARNINGS` öncesi her kalem için `PAY_ITEM` adımı (`payroll.trace.step.pay_item`), `Formula` = kullanıcının metni, Inputs: code,name,amountBasis,bayraklar,deger,kullanılan değişkenler,netTarget; Output = brüt tutar; formül `asgari_ucret` kullanıyorsa ParameterRefs'e `Payroll.MinimumWage.Gross`. `EARNINGS/SGK_BASE/GV_BASE/STAMP_TAX` adımlarına `payItemsTotal`/`payItems*Exempt` yalnız kalem varsa eklenir; kalem yoksa iz bire bir aynı.
- `PayrollPeriodCalculationService`: o ay için geçerli (aktif tanım + tarih çakışması) atamalar okunur, değerlendirilir, 2 haneye yuvarlanır (sıfırdan uzağa). Formül hatası → yeni atlama nedeni `pay_item_formula_failed` (logda yalnız kod, tutar yok). Negatif → 0 + `PayrollWarning("pay_item_negative_result")`. Net kalem: net ek kazançlarla birlikte tek `NetToGrossSolver.SolveExtra`; bulunan brüt net tutarlara oranla dağıtılır, kuruş farkı son satıra. Net ücretli çalışan: Solve bugünkü gibi kalemsiz, kalemler üstüne eklenir. Deneme bordrosu (önizleme) kalemleri içerir.
- `PayslipDocumentBuilder`: trace'te `PAY_ITEM` varsa Kazançlar tablosunda ayrı satırlar (net kalemde "(net X TL)"), "Toplam Brüt"; yoksa bugünkü tek satır aynen. PDF ve düz metin.
- `GET /periods/{id}/pay-item-lines` en son hesap izinden `PayItemLineDto[]`.
- Değişiklik işaretleme: `PayslipDirtyFlagReasons.PayItemChanged = "pay_item_changed"`; atama eklenince/değişince/silinince o çalışan, tanım değişince/aktif-pasif olunca açık dönemde ataması olan tüm çalışanlar işaretlenir. `PayrollInputHashing` değişmez.

## Sözleşmeler (api/v1/payroll, camelCase, null alanlar gönderilmez)
Tanım uçları: `GET /pay-items?includeInactive=false` (view) → `PayItemDefinitionDto[]`; `GET /pay-items/{id}`; `POST /pay-items` (manage) → Guid; `PUT /pay-items/{id}` (code hariç) → 204; `POST /pay-items/{id}/activate|deactivate` → 204; `POST /pay-items/validate-formula` (manage) → `FormulaValidationDto` (her zaman 200); `GET /pay-items/formula-catalog` (view) → `{variables:[{name,sampleValue}], functions:[{name,aliases[],minArgs,maxArgs?}]}`. Silme ucu yok.
`PayItemDefinitionDto`: `{id, code, name, direction:"earning"|"deduction", amountBasis:"gross"|"net", isSgkSubject, isIncomeTaxSubject, isStampTaxSubject, formula, defaultValue?, isActive, validFrom:"YYYY-MM-DD", validTo?}`. validate-formula isteği `{formula, amountBasis?, sampleValues?: Record<string,number>}`; `FormulaValidationDto`: `{isValid, errorCode?, errorMessage?, position?, usedVariables:string[], sampleResult?}`.
Atama uçları: `GET /pay-item-assignments?employeeId=` veya `?periodId=` (biri zorunlu) → `PayItemAssignmentDto[]`; `POST /pay-item-assignments` (manage) isteği `{payItemDefinitionId, employeeIds:Guid[1..500], value?, fromYear, fromMonth, toYear?, toMonth?}` → `{createdIds:Guid[]}`; `PUT /pay-item-assignments/{id}` `{value?, toYear?, toMonth?}` → 204; `DELETE /pay-item-assignments/{id}` → 204. `PayItemAssignmentDto`: `{id, employeeId, employeeName, payItemDefinitionId, code, name, value?, fromYear, fromMonth, toYear?, toMonth?}`. `PayItemLineDto`: `{employeeId, employeeName, code, name, amount, amountBasis, netTarget?}`.
Hata kodları (`PayrollDomainErrors` + resx tr/en, sona ekleme): `payroll.errors.pay_item_{code_invalid,code_taken,name_invalid,direction_not_supported,net_requires_fully_taxable,date_range_invalid,not_found,inactive,assignment_not_found,assignment_period_closed,assignment_too_many,assignment_value_required,assignment_overlap}` (overlap = 409: aynı çalışana aynı tanımın tarihleri çakışan ikinci ataması; farklı tanımlar serbest). Kod tekilliği 409. Atlama nedeni `pay_item_formula_failed`, uyarı `pay_item_negative_result`, işaretleme nedeni `pay_item_changed` (web etiketleri `payroll.json`).
Tablolar (`payroll` şeması): `payroll_pay_item_definitions` (tenant_id+code tekil indeks), `payroll_pay_item_assignments` ((tenant_id, employee_id, valid_from_month) indeksi; tanıma FK). Migration `dotnet-ef` ile (CLAUDE.md), elle yazılmaz.

## Dilimler
1. **PAY-FORMULA-1 (dotnet)** formül değerlendirici (Domain, saf).
2. **PAY-FORMULA-2 (dotnet)** tanım+atama: Domain/Application/API/persistence/migration/izin/dirty-flag.
3. **PAY-FORMULA-3 (dotnet)** hesaba/trace'e/pusulaya entegrasyon; altın set testleri kalemsiz aynen yeşil. Riskli: para.
4. **PAY-FORMULA-4 (node)** web: ödeme türü tanımları ekranı (`web/src/pages/payroll/pay-items.tsx`, `pay-item-form.tsx`, `formula-editor.tsx`, `web/src/services/payroll-pay-items.service.ts`, rota+menü+i18n tr/en/de).
5. **PAY-FORMULA-5 (node)** web: atama ve dönem dökümü (`period-pay-items.tsx`, çalışan `payroll-section.tsx`).
Paylaşılan dosyalara (`PayrollDomainErrors.cs`, resx, `App.tsx`, `navigation.ts`, `payroll.json`) yalnız sona ekleme.

## SMMM'ye sorulacaklar
1. SGK/GV/damga muafiyeti serbest mi, onaylı listeyle mi sınırlı (kreş, eğitim yardımı, 5510 m.80)? Limitli istisna gerekli mi? 2. Birden çok net kalemde brüt farkının net tutarlara oranla dağıtımı kabul mü? 3. Kalemler kıst ayda otomatik oranlanmalı mı? 4. APHB'de kalemler "prim/ikramiye" kolonuna ayrı yazılmalı mı; SGK tavanı aşan kısım devri? 5. Hangi kalemler kıdem/ihbar giydirilmiş ücrete girmeli? 6. 2 haneye yuvarlama ve negatif → 0 uygun mu? 7. `kidem_yili` işe girişten mi, birleştirilmiş kıdemden mi?

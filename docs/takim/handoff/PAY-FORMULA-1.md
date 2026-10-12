# PAY-FORMULA-1 handoff: formül değerlendirici (Domain, saf)

## Ne yapıldı
`src/Modules/Payroll/Sense.Modules.Payroll.Domain/PayItems/Formulas/` altında saf, decimal tabanlı, kültürden bağımsız formül dili:

- `PayrollFormula` (public): `Parse(string?) -> Result<PayrollFormula>`, `Evaluate(IReadOnlyDictionary<string,decimal>) -> Result<decimal>`, `Text` (kullanıcının metni, iz için), `UsedVariables` (tekil, ordinal sıralı, küçük harf), `Functions` (katalog: `FormulaFunctionInfo(Name, Aliases, MinArgs, MaxArgs?)`).
- `FormulaLimits` (500/200/20/40/20 + `MaxMagnitude` 1e9 + yuvarla basamak 0..4), `PayItemVariableNames` (plandaki 14 değişken, `All`, `IsAllowed`).
- Dahili: `FormulaLexer`, `FormulaParser` (özyinelemeli, exception yok), `FormulaNodes` (AST + değerlendirme), `FormulaFunctions`, `FormulaErrors`.
- Hata kodları `PayrollDomainErrors.Formula*` (sona eklendi) ve `SharedResource{,.en}.resx` (sona eklendi).

## Sonraki dilimler için bilmesi gerekenler
- **Konum 1 tabanlıdır** (metindeki karakter sırası). `Error.Args`: `position` (int), `name` (string; argument_count'ta kanonik Türkçe ad), `max` (int; too_long/too_deep ve sınırlı argument_count'ta). Mesaj yer tutucuları `{position}`, `{name}`, `{max}`.
- Hata türleri: sözdizimi/bilinmeyen ad/sınır = `Validation`; `division_by_zero` ve `overflow` = `Rule`.
- Bilinmeyen değişken/fonksiyon ve argüman sayısı `Parse` anında reddedilir. `Evaluate`'e verilen sözlükte kullanılan bir değişken yoksa `unknown_variable` döner. Sözlük anahtarları küçük harf olmalı (formül metni büyük/küçük harf duyarsız, `ToLowerInvariant`).
- Sınır: |değer| > 1e9 (sabit, ara sonuç, sonuç) `overflow`; 1e9'un kendisi serbest.
- `yuvarla(x; n)`: n tamsayı 0..4 değilse çalışma zamanında `payroll.formula.syntax` (işlev konumuyla). Yarım sıfırdan uzağa yuvarlanır.
- `and`/`or` kısa devre yapar; `eger/if` yalnız seçilen dalı hesaplar. `degil/not` karşılaştırmadan gevşek bağlanır (`degil 2 > 3` = `degil (2 > 3)`). Mantık değeri 1/0; sıfır dışı doğru.
- Virgül: argüman ayırıcı olarak çağrı içinde geçerli (`max(1,5)`=5). Çağrı dışında rakam-virgül-rakam bitişik yazılırsa `decimal_comma` (`1,5`, `(2,5)`); çağrı içinde `min(1,5)` belirsizdir ve ayırıcı sayılır.
- Birim işleçler: yalnız unary eksi (unary artı yok → sözdizimi hatası). `==`, `<>` sözdizimi hatası.
- Ayrıştırıcı web'e kopyalanmaz; slice 2 `validate-formula` ucu `PayrollFormula.Parse` + örnek değerle `Evaluate` çağırır.

## Test
`tests/Modules/Sense.Modules.Payroll.Tests/Formulas/PayrollFormulaTests.cs`: sözdizimi/öncelik, tüm işlevler (TR+EN), tembel `eger`, kısa devre, sınırlar (kenar+aşım), hata kodları ve konumları, tr-TR/en-US eşitliği (kültür testte sabitlenir ve geri alınır), sabit tohumlu (20261013) 10.000 girdilik "asla exception yok", yuvarlama.

# senseik-058 — Çalışan güncellemede sicil no / uyruk / yabancı kimlik / pasaport / SGK meslek kodu kaybı

**Durum: uygulandı, branch `a/employee-update-field-loss` (review).**

## Doğrulama (bug gerçek miydi?)

Evet. İzlenen yol: web `employee-card-sections.tsx` → `updateEmployee(toEmployeeInput(...))` → `PUT /v1/employees/{id}` →
`EmployeeRequest` → `UpdateEmployeeCommand.ToInfo()` → `Employee.UpdateInfo(EmployeeInfo)` → `Apply`.

- `EmployeeInfo` (senseik-009/010 ile) `SicilNumarasi`, `Nationality` (varsayılan `Turkish`), `ForeignIdNumber`,
  `PassportNumber`, `SgkOccupationCode` alanlarını taşıyor ve `Apply` bunları koşulsuz atıyor.
- Ama `EmployeeRequest` / `UpdateEmployeeCommand` / `CreateEmployeeCommand` bu alanları hiç taşımıyordu; `ToInfo()` bunlar için
  varsayılanları (null / `Turkish`) veriyordu. Sonuç: her `PUT` (web'de herhangi bir bölüm kaydı dahil) bu 5 alanı siliyor, yabancı
  uyruklu çalışanı Turkish'e çeviriyordu. (Toplu içe aktarma yolu `ExistingInfo` ile zaten koruyordu — etkilenmedi.)
- `EmployeeDetailDto` ve web tipleri bu alanları göstermiyor; yani istemci için de geri gönderme yolu yoktu.
- `Employee` üzerindeki diğer alanlar taranıp karşılaştırıldı: `Apply`'ın atadığı diğer her alan zaten komutta var;
  `IsEmployerRepresentative`, `IsUndergroundWorker`, `SubcontractorId`, terminasyon/askı alanları `Apply` dışında ayrı setter/komutlarla
  yönetiliyor (etkilenmiyor). Yani kayıp yalnızca bu 5 alanda.

## Düzeltme

- `EmployeeRequest`, `UpdateEmployeeCommand`, `CreateEmployeeCommand`: 5 yeni opsiyonel alan (`Nationality?` nullable). Controller
  create/update'te iletiyor.
- Sözleşme ("keep when absent"): update'te null/yok = kayıtlı değer korunur (`UpdateEmployeeCommand.ToInfo(existing)`, handler
  bunu kullanıyor); gönderilen değer kalıcı olur; metin alanlarında boş/boşluk string = açık temizleme (domain `Normalize` null yapar;
  senseik-044'teki `ClearLeaveAccrualStartDate` bayrağının string karşılığı — string için ayrı bayrağa gerek yok, `""` ile `null`
  ayrılabiliyor). `Nationality` boş olamayacağı için yalnızca değiştirilir. Create'te uyruk verilmezse `Turkish`.
- Doğrulayıcı: `EmployeeInfoRules.Apply`'a opsiyonel `nationalityProvided` predicate'i eklendi; uyruk gönderilmediyse
  uyruk-koşullu 3 kural (99'lu kimlik/Türk, normal TCKN/yabancı, yabancıda kimlik zorunlu) request'ten hüküm verilemediği için
  atlanıyor — yoksa yabancı çalışanın kartı uyruk göndermeden kaydedilirken yanlış 400 dönerdi. Otoriter kontrol yine domain'de
  (`Employee.Validate`, birleştirilmiş `EmployeeInfo` üzerinde).
- Web: `EmployeeInfo` tipine 5 opsiyonel alan eklendi; `toEmployeeInput` bilinçli olarak göndermiyor (kart bunları henüz
  göstermiyor; leaveAccrualStartDate ile aynı gerekçe). Kartta düzenleme UI'ı bu kartın kapsamı dışında (ayrı iş).
- Migration yok (model değişmedi). `EmployeeDetailDto` değişmedi.

## Testler

- `tests/Modules/Sense.Modules.Employee.Tests/EmployeeUpdateFieldPreservationTests.cs` (13 test, Docker'sız): alanlar gönderilmezse
  hepsi korunuyor (uyruk Turkish'e dönmüyor); gönderilen değerler kalıcı; `""`/`"   "` temizliyor; yabancıda kimliği silmek yine
  domain tarafından reddediliyor; validator'ın uyruk-yok/uyruk-var davranışı; create `ToInfo`; request JSON'unda alanlar yokken null.
- `web/src/services/employees.service.test.ts` (2 test): `toEmployeeInput` yeni alanları göndermiyor, override ile geçirilebiliyor.

## Gate

`dotnet build` Employee.Tests + Sense.Api: 0 hata. `dotnet test` Employee.Tests `Category!=Integration`: 191/191 yeşil.
Container'lı entegrasyon testleri çalıştırılmadı (Docker kapalı). Web: `tsc --noEmit` temiz, `eslint src` 0 hata (1 önceden var olan uyarı, `employee-list.tsx`), `vitest employees.service.test.ts` 2/2 yeşil.

# senseik-009 + senseik-010 — Sicil No/Uyruk + SGK İşyeri/Meslek Kodu — UYGULAMA PLANI

**Durum: GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

## Kapsam Notu (önemli keşif)

Kartlar Employee + Organization ile sınırlı tanımlanmış, ancak **Payroll modülü zaten bu alanların bir kısmını modellemiş**:
- `EmployeePayrollProfile.cs` — tarihli (ValidFrom/ValidTo) "Bordro parametresi"; `SgkWorkplaceCompanyId`, `SgkWorkplaceLocationId`, `SgkStatus`, **`IncentiveLawCode` (zaten var)** alanlarını taşıyor.
- `PayrollCalculator.cs:206` — 5 puanlık SGK teşviki TODO: "teşvik kanunu kodu profil alanı gerekiyor" → alan zaten var, sadece kullanılmıyor.

Teşvik verisi sıfırdan eklenecek bir alan değil — var olan ama boş bırakılan bir alan. Bu **Payroll modülüne dokunmayı gerektiren bir kapsam genişletmesi** — GATE 1'de ayrıca onaylanmalı.

`SgkWorkplaceLocationId` bugün sadece bir Guid; işaret ettiği Location'da gerçek "SGK işyeri sicil numarası" (APHB string) hiçbir yerde tutulmuyor — kart 010'un asıl eksik parçası bu.

## 1) Employee Domain Modeline Eklenecek Alanlar

`Employee.cs`, `EmployeeInfo.cs`, `EmployeeProfileEnums.cs`:
- `string? SicilNumarasi` — tenant içi unique iş anahtarı, nullable.
- `Nationality Nationality` (yeni enum: `Turkish=0` default, `Foreign=1` — mevcut kayıtlar otomatik Türk sayılır).
- `string? ForeignIdNumber` — TcKimlik ile aynı algoritma, "99" önekli.
- `string? PassportNumber` — YKN atanmamış yabancı çalışan için.
- `string? SgkOccupationCode` (kart 010).

Ayrı value object'e gerek yok (YAGNI) — mevcut `NationalId` gibi düz property. `EmployeeInfo` record'una sona eklenir, `Employee.Apply`/`Validate`'e işlenir.

**EF Core:** `EmployeeDbContext.cs`'te `NationalId` pattern'i kopyalanır (`HasMaxLength`, unique partial index `WHERE sicil_numarasi IS NOT NULL`). Migration: `EmployeeSicilNumarasiAndNationality` — **tüm sütunlar nullable**, mevcut veri dolu değil.

## 2) Organization/Lokasyon'a Eklenecek

`Location.cs`: `string? SgkIsyeriSicilNo` (mevcut `Address`/`NormalizeAddress` deseni). `LocationCreated`/`Updated` event'lerine eklemek gerekmez (YAGNI, hiçbir consumer ihtiyaç duymuyor). `ILocationLookup`'a `GetSgkIsyeriSicilNoAsync(tenantId, locationIds)` eklenmeli — Payroll'ün APHB üretimi için gerekli (e-Bildirge export'un kendisi kapsam dışı, sadece veri hazırlanıyor). Migration: `LocationSgkIsyeriSicilNo`, nullable. Ayrı "İşyeri" entity'sine gerek yok — Location zaten bu kavramı temsil ediyor.

## 3) Yeni "Teşvik" Alt Varlığı — YAGNI Sonucu: HAYIR

`EmployeePayrollProfile` zaten tarihli/versiyonlu + `IncentiveLawCode` alanına sahip. Gerçek eksikler (yeni entity değil, 3 küçük iş):
1. Katalog/doğrulama yok — free-text riskli; sabit enum da riskli (yeni teşvik kanunları periyodik çıkıyor). Öneri: `LegislationParameterSeed.cs` gibi veri-driven küçük statik katalog (enum DEĞİL).
2. Sinyal bağlanmamış — `DocumentRequirementMaterializationService.cs:69` `HasIncentive: false` sabit, gerçek veriye bağlanmalı.
3. **Veri sahibi çakışması:** kart, teşvik alanının Company profilinde görünmesini istiyor ama veri employee-bazlı. Öneri: Company profilinde yazılabilir alan AÇILMASIN (iki doğruluk kaynağı riski); sadece "aktif teşvik türleri" özet/salt-okunur liste gösterilsin. **Bu mimari karar GATE 1'de netleştirilmeli.**

## 4) TCKN Doğrulamasının Uyruğa Göre Koşullandırılması

`TcKimlik.cs` — algoritma değişmiyor, `IsForeign` (StartsWith("99")) zaten var ama kullanılmıyor. `Employee.Validate`'e:
- Türk + NationalId verilmiş → `IsValid` VE `!IsForeign` (bugün Türk çalışana 99-önekli girilebiliyor — veri kalitesi deliği).
- Yabancı → `IsValid` VE `IsForeign==true`; VE `ForeignIdNumber`/`PassportNumber`'dan en az biri dolu olmalı (yeni kural — bugün hiçbiri yok, "yabancı işçi kaydı açılamıyor" bulgusu bu yüzden geçerli).

`EmployeeValidation.cs` (FluentValidation mirror) aynı kural. `DocumentRequirementMaterializationService.cs:68` `NonTurkishNational: false` sabiti gerçek `Nationality==Foreign` değerine bağlanmalı — bu, `IEmployeeDocumentEligibilityLookup` sözleşmesine `Nationality` eklenmesini gerektirir (WorkPermit belge kuralının hiç tetiklenmemesi iç çelişkisi böyle kapanır).

## 5) TCKN → SicilNumarasi Geçişi (Dış Katmanlar)

Etkilenenler: `IEmployeeLookup.FindActiveByNationalIdAsync` (PDKS eşleştirmesi), `Attendance` modülü import/device-log handler'ları, `EmployeeMasterImportTemplate.cs` (`isMatchKey: true`).

**Geriye dönük uyumluluk gerekli — sert kesim önerilmez:**
1. Import şablonuna `sicilNumarasi` kolonu eklenir (opsiyonel, başlangıçta match-key değil).
2. Eşleştirme "SicilNumarasi verilmişse önce onunla, yoksa NationalId'ye düş" — çift-anahtar.
3. Tenant tüm çalışanlarına sicil atadığında NationalId eşleştirmesi "deprecated ama çalışır" hale alınabilir — **bu kartların kapsamı değil**, sadece dual-key altyapısı kuruluyor.

NationalId maskeleme/şifreleme değişikliği bu kartların kapsamında **yapılmamalı** (ayrı, daha büyük KVKK kararı — not edilsin, aksiyon alınmasın).

## 6) Veri Aktarım Şablonu

Şablon `src/Modules/Employee/.../Import/EmployeeMasterImportTemplate.cs` içinde (DataTransfer sadece jenerik altyapı). `EmployeeImportColumns`'a 5 yeni sabit + `GetColumnsAsync`'e `Options<Nationality>` (generic, ek kod gerekmez) + `Static(...)` kolonlar + `GetSampleRows`'a en az bir yabancı çalışan örneği. `SharedResource.resx`/`.en.resx`'e header/description key'leri. Location'a ayrı şablon gerekmiyor (CRUD ekrandan yönetiliyor).

## 7) Migration Sırası ve Riskler

**Sıra:** (1) Organization `LocationSgkIsyeriSicilNo` (bağımsız, risksiz) → (2) Employee `EmployeeSicilNumarasiAndNationality` (unique partial index, NationalId ile aynı garanti) → (3) Payroll/Documents application-layer değişiklikleri (migration gerektirmez, sadece sinyal bağlama) — Employee migration'ı merge olduktan SONRA (Contracts → Infrastructure → Documents tüketim sırası).

**Nullable/opsiyonel başlangıç zorunlu** — tüm yeni sütunlar nullable/default'lu, `Nationality.Turkish=0` default ile mevcut satırlar sessizce Türk sayılır (davranış değişikliği yok, çünkü TCKN'li kayıtlar gerçekten Türk).

**Test planı:** Domain (Türk+99-önekli reddi, Yabancı+geçerli-TCKN reddi, Yabancı+ikisi-de-boş reddi), `TcKimlik.IsForeign` birim test, `SicilNumarasiTakenAsync` uniqueness, import sample-row + dual-key eşleştirme, `DocumentRequirementMaterializationService` WorkPermit materialize testi (yeni — iç çelişki düzeltmesinin kanıtı), `Location` round-trip, demo seed'e yabancı çalışan + SGK sicil no'lu lokasyon eklenmesi (aksi halde manuel QA'da görünmez).

## Kritik Dosyalar
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/Employee.cs`, `EmployeeInfo.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/EmployeeValidation.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Infrastructure/Persistence/EmployeeDbContext.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Application/Import/EmployeeMasterImportTemplate.cs`
- `src/Modules/Organization/Sense.Modules.Organization.Domain/Locations/Location.cs`
- `src/Modules/Documents/Sense.Modules.Documents.Application/DocumentTypes/DocumentRequirementMaterializationService.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Domain/Profiles/EmployeePayrollProfile.cs` (kapsam genişletme onayı gerektirir)
- `src/Shared/Sense.Shared.Kernel/ValueObjects/TcKimlik.cs`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § F-02, F-03. Agent board: `docs/agent-board/cards/senseik-009-sicil-numarasi.json`, `senseik-010-sgk-isyeri-kod.json`.*

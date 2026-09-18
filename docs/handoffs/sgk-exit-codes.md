# senseik-006 — SGK İşten Çıkış Kodları Doğrulaması ve Düzeltme (F-01) — UYGULAMA PLANI

## Implementasyon Notu (2026-09-18)

**Durum: Uygulandı, yerel commit'e hazır (GATE 1 ve GATE 2 kullanıcı tarafından önceden onaylandı). Push yapılmadı.**

Ne yapıldı (plandaki numaralamaya göre):
- **§1/§2** — `TerminationReasonSeed.cs`'teki 13 satırın `DefaultSgkExitCode` değerleri tabloya göre düzeltildi. `EmployeeJustCause`/`EmployerJustCause` satırları `DefaultSgkExitCode = null` bırakıldı (aile içinden zorunlu seçim, §5 madde 5'in önerisi).
- **§4** — Yeni `SgkExitCodeRules.cs` (Employee.Domain, `Terminations/`): `TerminationType → izinli SGK kod ailesi` eşlemesi, artı iki tüketici için ayrı yardımcı metod: `IsCatalogConsistent` (katalog zamanı — kod boş bırakılabilir) ve `IsTerminationConsistent` (fesih zamanı — birden çok kodlu bir ailede kod zorunlu). `TerminationReasonCommands.cs`'teki Create/Update validator'larına ve `TerminateEmployee.cs`'teki hem validator'a (kısmi, sadece iki alan da override edilmişse) hem handler'a (tam çözümlenmiş çift) `Error.Validation`/FluentValidation ile engelleyici (400) tutarlılık kontrolü eklendi.
- **§2 backfill** — Yeni idempotent `TerminationReasonCorrectionService.cs` (Application): sadece `IsSystem=true` VE hâlâ eski-bilinen-yanlış değerdeki satırları düzeltir; tenant'ın kendi değiştirdiği satırlara dokunmaz. `EmployeeModule.cs`'te DI'a eklendi ama **otomatik çalıştırılmıyor** (startup'a bağlanmadı) — hangi tenant'larda ne zaman çalıştırılacağı bir business/insan kararı (§5 risk 2), bu kart sadece servisi ve testlerini teslim ediyor. Geçmiş fesihlerdeki `Employee.SgkExitCode` anlık görüntülerine KESİNLİKLE dokunulmadı (§5 risk 1).
- **§3/§6 (web)** — `lifecycle-actions.tsx`'teki `TerminateDialog` iki adıma bölündü: Adım 1 (tarih/neden/tür + SGK kodu artık salt okunur `Text`, md.24/md.25 ailesinde ikinci zorunlu `Select` — seçenekler backend'in `allowedSgkExitCodes`'undan, frontend'de tekrar tanımlanmadı) → Adım 2 (özet + "SGK çıkış kodunu onaylıyorum" zorunlu checkbox, geri/onayla). `termination-reasons.tsx`'teki serbest metin SGK alanına, aynı türü kullanan mevcut bir satırdan türetilen izinli-kod ipucu eklendi. Backend DTO'ya (`TerminationReasonDto`) `AllowedSgkExitCodes` (nullable liste, `SgkExitCodeRules`'tan türetilmiş) eklendi.
- **Testler** — Yeni: `TerminationReasonSeedTests.cs`, `SgkExitCodeRulesTests.cs`, `TerminationReasonCommandsTests.cs`, `TerminateEmployeeValidatorTests.cs` (validator-seviyesinde — tam handler senaryoları, örn. "reason default'unun snapshot'landığı" ve "md.24 ailesinde geçerli alt-kodun kabul edildiği", zaten var olan `TerminationIntegrationTests.cs`'e eklenen iki yeni entegrasyon testiyle - `SgkExitCodeConsistency_RejectsMismatchedPairsAndAcceptsFamilyMembers`, `CorrectionService_FixesOnlyUntouchedSystemRowsAndIsIdempotent` - gerçek DB üzerinden kapsandığı için ayrıca ağır mock'lu bir `TerminateEmployeeHandlerTests` yazılmadı). Var olan `TerminationIntegrationTests.cs` senaryolarındaki eski (yanlış) kod literalleri ("29", "99", "23"+FixedTermExpiry gibi) yeni doğru değerlere güncellendi.
- **Test sonuçları** — Docker SDK konteyneri: `dotnet build` → 0 hata. `dotnet test` (Employee modülü, Testcontainers/Postgres ile): termination+SGK filtresiyle 67/67 yeşil; tüm proje 201 testten 197 yeşil, 4 kırmızı — kırmızılar `PaymentsIntegrationTests.cs`'te bu işten bağımsız, önceden var olan bir DI eksikliği (`IUserProfileLookup` kayıtlı değil), bu PR hiç dokunmadı. Web: `pnpm exec tsc --noEmit` (0 hata), `pnpm exec eslint src` (0 hata, 1 önceden var olan ilgisiz uyarı), `pnpm exec vite build` (başarılı).

Hâlâ açık olan sorular (kapsam dışı, kod eklenmedi — sadece not):
- **§5 açık soru 4** — Deneme süresi feshinde işveren (01) / işçi (02) ayrımı yok; tek `ProbationTermination` nedeni var. Business ile netleştirilmeli: yeni bir alt-neden eklenecek mi?
- **§5 açık soru 6** — `Integrations` modülünün webhook alıcıları geçmişte yanlış kodu almış olabilir; SenseİK'nın kontrolü dışında ama müşteri iletişiminde belirtilmeli.
- **§5 risk 1** — Geçmiş fesihlerin `SgkExitCode` anlık görüntüsü düzeltilmedi (bilerek); etkilenen kayıt sayısını raporlayan ayrı bir salt-okunur rapor hâlâ yapılmadı.
- **§5 risk 2** — `TerminationReasonCorrectionService`'in hangi tenant'larda / ne zaman çalıştırılacağı (tek seferlik admin komutu mu, migrator adımı mı) belirlenmedi; şu an sadece DI'da kayıtlı, çağrılmıyor.

---

**Durum: GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

---

## 1) Doğrulanmış SGK Kod Tablosu

Kaynaklar: SGK'nın resmi "İşten Ayrılış Nedenleri" referans sayfası (uyg.sgk.gov.tr/SigortaliTescil/jsp/nedenleryeni.html) ve çapraz kontrol için muhasebetr.com, datassist.com, tahanci.av.tr, harbiyehukuk.com, kadimhukuk.com.tr, demiraydinhukuk.com. Tüm kaynaklar 01-30 arası kodların anlamında birbiriyle **uyumlu** (tek istisna: 06, 07, 29, 35, 41 bazı kaynaklarda "kullanımdan kaldırılmış/bugün atanmamış" olarak not ediliyor — bu, kartın "İstifa=29 hatalı, olması gereken 03" bulgusuyla da örtüşüyor, çünkü 29 zaten resmen boş/iptal bir slot).

| # | Sistemdeki ad (Code) | Şu anki (YANLIŞ) kod | **Doğru SGK kodu** | SGK kodunun resmi anlamı |
|---|---|---|---|---|
| 1 | Ölüm (`Death`) | 21 | **10** | Ölüm |
| 2 | Askerlik (`MilitaryService`) | 10 | **12** | Askerlik |
| 3 | İstifa (`Resignation`) | 29 | **03** | Belirsiz süreli iş sözleşmesinin işçi tarafından feshi (istifa) |
| 4 | İşveren feshi md.18 (`ValidCause_Art18`) | 02 | **04** | Belirsiz süreli iş sözleşmesinin işveren tarafından haklı sebep bildirilmeden feshi (geçerli neden/iş güvencesi) |
| 5 | İşçinin haklı feshi md.24 (`EmployeeJustCause_Art24`) | 19 | **23 / 24 / 25 ailesi** (bkz. §4) | 23=zorunlu neden, 24=sağlık nedeni, 25=işverenin ahlak/iyiniyet kurallarına aykırı davranışı |
| 6 | İşverenin haklı feshi md.25 (`EmployerJustCause_Art25`) | 15 | **26 / 27 / 28 ailesi** (bkz. §4) | 26=disiplin kurulu kararı, 27=zorunlu neden ve tutukluluk, 28=sağlık nedeni |
| 7 | İkale (`MutualRescission`) | 22 | **22** ✅ zaten doğru | Diğer nedenler |
| 8 | Belirli süreli sözleşme sonu (`FixedTermExpiry`) | 03 | **05** | Belirli süreli iş sözleşmesinin sona ermesi |
| 9 | Emeklilik (`Retirement`) | 08 | **08** ✅ zaten doğru | Emeklilik (yaşlılık) veya toptan ödeme nedeniyle |
| 10 | Evlilik / kadın işçi (`MarriageFemale`) | 04 | **13** | Kadın işçinin evlenmesi |
| 11 | Deneme süresi içinde fesih (`ProbationTermination`) | 01 | **01** ✅ işveren tarafı için doğru — bkz. açık soru §5 | 01=işverence, 02=işçi tarafından |
| 12 | Grup içi nakil (`GroupTransfer`) | 27 | **16** | Sözleşme sona ermeden sigortalının aynı işverene ait diğer işyerine nakli |
| 13 | Diğer (`Other`) | 13 | **22** | Diğer nedenler |

Kaynaklar:
- [SGK İşten Ayrılış Nedenleri (resmi)](https://uyg.sgk.gov.tr/SigortaliTescil/jsp/nedenleryeni.html)
- [muhasebetr.com](https://www.muhasebetr.com/sgk-isten-cikis-kodlari/)
- [datassist.com](https://datassist.com/blog/sgk-isten-cikis-kodlari-tum-kodlar-ve-anlamlari)
- [tahanci.av.tr](https://www.tahanci.av.tr/isten-cikis-kodlari-sgk-isten-ayrilma-kodlari/)
- [harbiyehukuk.com](https://www.harbiyehukuk.com/sgk-isten-cikis-kodlari/)

---

## 2) Kod Tabanında Değişecek Dosyalar

**Seed / lookup (asıl düzeltme):**
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Terminations/TerminationReasonSeed.cs` — 13 satırın `DefaultSgkExitCode` alanları §1'deki tabloya göre düzeltilecek. `EmployeeJustCause`/`EmployerJustCause` satırları için tek kod yerine `DefaultSgkExitCode = null` bırakılması ve wizard'da aile içinden seçim zorunlu kılınması önerilir (§4).
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Terminations/TerminationReason.cs` — XML doc'taki "placeholder" notu kaldırılıp doğrulanmış olduğu belirtilmeli.

**Migration:** EF Core şema migration'ı GEREKMİYOR (seed, tenant başına ilk ihtiyaçta `TerminationReasonProvisioningService.EnsureSeededAsync` tarafından koddan yazılıyor). **Ancak veri düzeltme (backfill) adımı GEREKİYOR** (§5) — `EnsureSeededAsync` sadece hiç seed edilmemiş tenantlarda çalışıyor; ayarları bir kez açmış her tenant'ın DB'sinde eski (yanlış) kodlar kalıcı yazılmış durumda.
- Yeni: `src/Modules/Employee/Sense.Modules.Employee.Application/Terminations/TerminationReasonCorrectionService.cs` (öneri) — sadece `IsSystem=true` VE `DefaultSgkExitCode` hâlâ eski bilinen yanlış değere eşit olan satırları düzeltir (tenant kendi özelleştirmesini yapmışsa dokunmaz). İdempotent, tek seferlik reconciliation adımı.

**Uygulama/API katmanı (tutarlılık denetimi için, §4):**
- `TerminationReasonCommands.cs` — create/update validator'a "DefaultTerminationType set edilmişse DefaultSgkExitCode o ailenin içinde olmalı" kuralı.
- `TerminateEmployee.cs` — `TerminateEmployeeValidator`'a aynı tutarlılık kuralı; final `(terminationType, sgkExitCode)` çifti handler içinde doğrulanmalı.
- Yeni domain yardımcı sınıf: `SgkExitCodeRules.cs` (öneri) — `TerminationType -> IReadOnlySet<string>` eşlemesini tek yerde tutar, hem catalog validator'ı hem termination-time validator'ı çağırır.

**Kontratlar:** `EmployeeTerminated` event'i (`EmployeeLifecycleEvents.cs`) SGK kodunu ham string taşıyor — kod değişmiyor, sadece taşınan değer doğru olacak. `Integrations` modülü webhook payload'ına olduğu gibi koyuyor; SGK'ya doğrudan API entegrasyonu yok. Değişiklik gerekmez.

---

## 3) Sihirbaza "Kodu Salt Okunur Göster + Onaylatan Adım"

`web/src/components/employees/lifecycle-actions.tsx`'teki `TerminateDialog`, SGK kodunu düzenlenebilir bir `TextInput` olarak gösteriyor (satır 264-271) — serbest metin, doğrulama yok. Sihirbaz tek adımlı, onay adımı yok.

Önerilen değişiklik:
- SGK kodu alanı → salt okunur gösterim (`Badge`/`Text` + kısa açıklama). Aile-tipi nedenlerde (md.24/md.25) ikinci bir `Select` (23/24/25 veya 26/27/28) zorunlu.
- Formu iki adımlı akışa böl (Mantine `Stepper`): Adım 1 = mevcut form; Adım 2 = özet + "SGK çıkış kodunu onaylıyorum" checkbox (zorunlu) + geri/onayla. Submit yalnız Adım 2'den.
- Aile kod listeleri backend'den (`GET /termination-reasons` yanıtına `allowedSgkExitCodes`) sürülmeli, frontend'de tekrar tanımlanmamalı.
- `web/src/pages/settings/termination-reasons.tsx`'teki serbest metin `TextInput` (satır 318-325) için de aynı risk — izinli kod listesinden seçim veya format uyarısı önerilir.

---

## 4) Fesih Türü ↔ SGK Kodu Tutarlılık Denetimi

**Kural nerede yaşamalı:** Domain katmanında, saf/stateless — `SgkExitCodeRules` (öneri, 5510 kanunundan gelen tenant-bağımsız sabit eşleme).

İki tüketici:
1. **Katalog zamanı** — `TerminationReasonCommands.cs` validator: satır `DefaultTerminationType` set ediyorsa `DefaultSgkExitCode` izinli ailede olmalı.
2. **Fesih zamanı** — `TerminateEmployeeHandler`: kullanıcı override ettiğinde final çift tekrar doğrulanmalı.

```csharp
public static class SgkExitCodeRules
{
    public static IReadOnlySet<string>? AllowedCodes(TerminationType type) => type switch
    {
        TerminationType.ValidCause_Art18 => new[] { "04" }.ToHashSet(),
        TerminationType.EmployeeJustCause_Art24 => new[] { "23", "24", "25" }.ToHashSet(),
        TerminationType.EmployerJustCause_Art25 => new[] { "26", "27", "28" }.ToHashSet(),
        TerminationType.MutualRescission => new[] { "22" }.ToHashSet(),
        TerminationType.Resignation => new[] { "03" }.ToHashSet(),
        TerminationType.FixedTermExpiry => new[] { "05" }.ToHashSet(),
        TerminationType.Other => null,
    };
}
```

Doğrulama **engelleyici hata** olarak modellenmeli (FluentValidation, 400 Problem Details) — advisory/non-blocking uyarı deseni (JobSecurityEvaluator vb.) burada uygun değil, çünkü bu payroll-grade/yasal doğruluk meselesi. Kartın `merge_gate`'i de bunu ("veri doğruluk kontrolü") istiyor.

---

## 5) Riskler / Açık Sorular

1. **Zaten yanlış kodla kayıtlı geçmiş fesihler** — `Employee.SgkExitCode`, her fesihte o anki kodun kalıcı anlık görüntüsü. Toplu otomatik düzeltme YAPILMAMALI (gerçek SGK beyanıyla çelişebilir). Öneri: (a) etkilenen tenant/fesih sayısını raporlayan salt-okunur rapor, (b) HR'a mutabakat uyarısı, (c) düzeltme yalnız kullanıcı onayıyla kayıt bazlı.
2. **Katalog backfill'i tenant özelleştirmesini ezebilir** — `Update` metodu `IsSystem`'e bakmıyor; backfill "hâlâ eski yanlış değerdeyse düzelt" mantığıyla güvenli hale getirilmeli.
3. **İkale (22) ve Diğer (22) çakışması** — SGK'da ikale için ayrı kod yok; raporlama/analitikte ikale ile diğer ayrışamayacak (mevcut kısıt, dokümante edilmeli).
4. **Deneme süresi feshi yönü ayrımı yok** — SGK'da işveren (01) / işçi (02) ayrımı var, sistemde tek neden. Business ile netleştirilmeli: yeni neden eklenecek mi, kapsam dışı mı?
5. **md.24/md.25 ailesi için varsayılan kod seçimi** — Business/UX onayı gerekir: hiç ön-doldurma yapılmasın mı, yoksa en yaygın alt-neden mi varsayılan olsun (öneri: hiçbiri, çünkü yanlış varsayılan riskli).
6. **Diğer modüllerdeki tüketiciler** — `Integrations` webhook alıcısı üçüncü taraf sistemler geçmişte yanlış kod almış olabilir; SenseİK kontrolü dışında ama müşteri iletişiminde belirtilmeli.

---

## 6) Test Planı

**Birim testleri** (`tests/Modules/Sense.Modules.Employee.Tests`):
- `TerminationReasonSeedTests` (yeni) — 13 satırın her biri için doğru kodu doğrulayan parametrik test.
- `SgkExitCodeRulesTests` (yeni) — her `TerminationType` için izinli/izinsiz kod, null durumları.
- `TerminationReasonCommandsTests` — tutarsız `DefaultTerminationType`+`DefaultSgkExitCode` kombinasyonunun validation hatası verdiğini test et.
- `TerminateEmployeeHandlerTests` — reason default'unun snapshot'landığını, uyumsuz override'ın reddedildiğini, md.24/md.25 ailesinde geçerli alt-kodların kabul edildiğini test et.

**Entegrasyon testleri:**
- `TerminationIntegrationTests.cs` — mevcut senaryoları doğru kodlara güncelle; "yanlış SGK kodu ile fesih 400 döner", "md.24 ailesinden geçerli alt-kod ile fesih başarılı" senaryoları ekle.
- Backfill/correction service için: eski kodla seed edilmiş tenant senaryosu — sadece dokunulmamış sistem satırlarının düzeldiğini, tenant özelleştirmesine dokunulmadığını doğrula.

**Frontend (Playwright E2E):**
- SGK kodunun salt okunur göründüğü ve doğru değeri gösterdiği.
- Onay checkbox'ı işaretlenmeden submit edilemediği.
- md.24/md.25 ailesinde alt-kod seçiminin zorunlu olduğu.

**Security-reviewer için not:** İnceleme özellikle §5'teki "geçmiş fesihler" ve "backfill tenant özelleştirmesini ezmemeli" konularına odaklanmalı.

---

## Kritik Dosyalar (Implementasyon için)
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Terminations/TerminationReasonSeed.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Terminations/TerminationReason.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/TerminateEmployee.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Application/Terminations/TerminationReasonProvisioningService.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Application/Terminations/TerminationReasonCommands.cs`
- `web/src/components/employees/lifecycle-actions.tsx`
- `tests/Modules/Sense.Modules.Employee.Tests/TerminationIntegrationTests.cs`

---

*Kaynak: 2. Tur Test Bulgu Dokümanı § F-01. Agent board: `docs/agent-board/cards/senseik-006-sgk-kodlari.json`.*

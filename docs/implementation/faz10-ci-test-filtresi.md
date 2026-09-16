# CI test filtresi analizi (K-8) — `Category=Unit` / `Category=Integration`

**Bağlam:** `pr.yml`/`main.yml`, build sonrası iki ayrı `dotnet test Sense.slnx --filter "Category=<Unit|Integration>"`
adımı çalıştırıyor (ayrıca Architecture testleri filtresiz, üçüncü/ilk bağımsız adım olarak koşuyor —
bu doküman onu etkilemiyor). PO'nun tur 3 açılışında doğruladığı gibi repo genelinde
`[Trait("Category", "Unit")]` kullanan **0** test dosyası var. Bu belge nedenini, kapsamını ve iki seçeneği
çıkarır — **workflow dosyasına dokunulmadı**, karar PO'da.

## Bulgular (bu turda kod okuyarak + grep ile çıkarıldı, `dotnet test` çalıştırılamadı — araç erişimi yok)

- Repoda **24** test projesi var. Bunlardan **19**'u `tests/Modules/*` altında, modül başına bir proje —
  hiçbiri `Testcontainers` paket referansı taşımıyor (`grep -l Testcontainers tests/Modules/*/*.csproj` boş
  döndü); hepsi NSubstitute/fake tabanlı, DB'siz gerçek birim testleri (Reporting.Tests, Analytics.Tests'in
  bu turda okunan içeriği dahil örnek). Bu 19 projede toplam **706** `[Fact]`/`[Theory]` var; **hiçbirinde**
  `Category` trait'i yok.
- `Sense.Tests.Integration` (Testcontainers ile gerçek Postgres kullanan tek proje —
  `TestFixture`/`TenantDbFactory` üzerinden) **13** dosyada **40** test içeriyor, ama bunlardan yalnız
  **10**'u `[Trait("Category","Integration")]` taşıyor: `AccessSettingsIntegrationTests`'teki 2 test +
  `ExampleIntegrationTest.cs`'teki 6 **örnek/şablon** test (gerçek özellik kapsamı değil, dokümantasyon
  amaçlı `TestFixture` kullanım örneği). Gerçek özellik testleri — `Identity/UnitAuthorizationScopeTests`,
  `Identity/EmployeeInvitationTests`, `Identity/PasswordResetTests`, `Identity/PlatformImpersonationTests`,
  `Identity/AccessTypeRoleSyncAndSelfCardTests`, `NotificationsHubTests`, `RequestHeaderMiddlewareTests`,
  `BackgroundJobServiceTests`, `CorrelationIdMiddlewareTests` (toplam ~30 test) — **hiç trait taşımıyor**.
- `Sense.Tests.TenantIsolation` ve `Sense.Tests.Contract` projeleri **iskelet**: `.csproj` var, test dosyası
  **0**. (K-8'in kapsamı değil ama CI'daki iki filtreli adımın neden "çoğunlukla boş" göründüğünü
  tamamlıyor — bu iki proje zaten hiç test katkısı yapmıyor.)

## Sonuç: filtre "yanlış" değil, **trait eksik** — ve etkisi ikisi de sanılandan büyük

`--filter "Category=Unit"` ve `--filter "Category=Integration"` mekanik olarak doğru çalışıyor (vstest
trait eşlemesi). Sorun: bugün itibarıyla **iki filtreli CI adımı da gerçek kapsamın neredeyse tamamını
atlıyor**:
- "Unit" adımı: 706 modül-seviyesi testin **0**'ını çalıştırıyor (hepsi eşleşmiyor → "no tests match filter",
  vstest bunu **hata değil uyarı** sayıyor, adım yeşil kalıyor — sessiz kırmızı/yanlış-yeşil riski, TEST-SONUCU
  15:44 çıktısında da görülen `... ile eşleşen test yok` satırları bunun kanıtı).
- "Integration" adımı: 40 testten yalnız 8'ini (6'sı örnek/şablon, 2'si gerçek) çalıştırıyor; 30 gerçek
  entegrasyon testi (Identity MFA/davet/parola/impersonation, Notifications hub, middleware'ler,
  background job) **hiç koşmuyor**.

Yani bugün CI'nin iki test adımı da (Unit + Integration) repodaki gerçek test gövdesinin **~%5'inden**
azını çalıştırıyor; kalan ~726 test (706 modül birim testi + 30 gerçek entegrasyon testi) yalnızca
geliştiricinin lokalinde (ya da bu takımın PO tarafındaki harici doğrulama script'inde, `TEST-SONUCU.md`,
ki o da muhtemelen filtresiz `dotnet test Sense.slnx` koşuyor — teyit edilmedi) çalışıyor olabilir.

## Seçenek A — filtreyi mevcut adlandırmaya (proje/namespace) çevir
CI adımlarını trait yerine proje/namespace ayrımına dayandır — repodaki **gerçek** ayrım zaten bu:
`tests/Modules/*` = saf birim, `Sense.Tests.Integration` (+ boşsa `TenantIsolation`/`Contract`) = Testcontainers'lı.
Örnek (uygulanmadı, yalnız öneri):
```
# Unit adımı
dotnet test Sense.slnx --filter "FullyQualifiedName!~Sense.Tests.Integration&FullyQualifiedName!~Sense.Tests.TenantIsolation&FullyQualifiedName!~Sense.Tests.Contract"
# Integration adımı
dotnet test tests/Sense.Tests.Integration/ tests/Sense.Tests.TenantIsolation/ tests/Sense.Tests.Contract/
```
(Architecture zaten ayrı adımda, filtresiz — değişmez.)

**Maliyet:** 0 test dosyası değişikliği — yalnızca 2 workflow dosyasında birkaç satır (`pr.yml`, `main.yml`).
**Risk:** Düşük. Bugünkü gerçek ayrımı birebir yansıtıyor (19/19 modül projesi Testcontainers'sız,
doğrulandı). İleride bir modül testi DB'li hale gelirse (bugün yok) o proje `FullyQualifiedName!~` listesine
eklenmesi gerekir — yani ayrım hâlâ elle bakım ister, ama proje-seviyesinde (19 kez değil, yalnız istisna
olduğunda).

## Seçenek B — `[Trait("Category","Unit")]` ekle
`tests/README.md` §"Mark Test Categories (Optional)" bu deseni zaten öneriyor ama hiçbir geliştirici turu
(9 paralel faz + bu modül dahil) uygulamamış — 706 testin 0'ı işaretli. Maliyet, uygulama seviyesine göre:
- **Metod başına** (README'nin gösterdiği kalıp): ~706 `[Fact]`/`[Theory]` + gerçek Integration'daki ~30
  test = ~736 satır ekleme, ~114+13 dosyaya dokunma. Bu turda 9 faz-agent'ı aynı anda test dosyalarına
  yazıyor (R-17) — bu kadar geniş, çoğu dosyaya dokunan bir değişiklik şu an yüksek çakışma riski taşır.
- **Sınıf başına** (her test class'ın üstüne tek `[Trait]`, tüm metodlarına miras): ~114+13 = ~127 dosyaya
  tek satır. Daha ucuz ama yine de büyük, çok-dosyalı bir diff.
- **Assembly başına** (`[assembly: Trait("Category","Unit")]`, proje başına 1 küçük yeni dosya, tüm testlere
  otomatik uygulanır): 19+ birkaç dosya, en ucuz. **Doğrulanamadı** — xunit.v3'ün `TraitAttribute`'unun
  `AttributeTargets.Assembly`'yi destekleyip desteklemediğini bu turda `dotnet build` ile teyit edemedim
  (araç erişimi yok); destekliyorsa maliyet Seçenek A'ya yakın (19 küçük dosya), desteklemiyorsa üstteki
  "sınıf başına" senaryosuna düşer.

**Ayrıca B'nin kalıcı riski:** disiplin bağımlı. README bu deseni "Optional" olarak zaten öneriyordu ve
706 test 0 uyum gösterdi — gelecekte her yeni test dosyası trait'i unutursa CI'den sessizce düşer (bugün
olan tam olarak bu). A, bunu proje-seviyesinde sabitleyerek insan hatasına daha az bağımlı kılıyor.

## Önerim
**A.** Gerekçe: bugünün gerçek ayrımını (proje = kategori) birebir yansıtıyor, sıfır test dosyası
değişikliği ile ~726 "görünmez" testi CI'ye anında geri kazandırıyor, ve gelecekte "trait eklemeyi unutma"
riskini ortadan kaldırıyor (B'nin READMEda zaten önerilmiş olup hiç uygulanmamış olması bunun gerçek bir
risk olduğunun kanıtı). B, yalnız ileride bir modül test projesi içinde hem hızlı hem yavaş/DB'li testler
karışık yaşamaya başlarsa (bugün hiçbiri öyle değil) anlamlı hale gelir — o zaman A'nın proje-seviyesi
ayrımı yetersiz kalır ve B'nin ince taneli işaretlemesi gerekir.

## Kapsam dışı / bu belgeyle çözülmeyen
- `Sense.Tests.TenantIsolation` ve `Sense.Tests.Contract`'ın **boş** olması (0 test) — ayrı bir bulgu,
  hangi fazın/ekibin sorumluluğu olduğu K-8'in konusu değil, PO'ya ayrıca bildirildi (raporda).
- `TEST-SONUCU.md` üreten harici script'in hangi komutu çalıştırdığı (filtreli mi filtresiz mi) teyit
  edilemedi — bu script'e erişimim yok.

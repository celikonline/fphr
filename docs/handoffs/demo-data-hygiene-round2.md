# senseik-043 — Demo Veri Hijyeni Round 2 — UYGULAMA PLANI

**Durum: Uygulandı ve doğrulandı (build + seed testleri yeşil) — review. `a/demo-data-hygiene-round2` dalında; push/merge yapılmadı.**

## Özet

1. Tur Test Bulgu Dokümanı § B-07/C-06'nın işaret ettiği, senseik-017'nin kapsamı dışında kalan (o kart org şeması odaklıydı) dört İngilizce/anlamsız demo veri alanı düzeltildi:

1. **Demo departman adları** — `SeedDataConstants.DemoDepartments`: `Sales`/`Engineering`/`HR`/`Finance` → `Satış`/`Yazılım Geliştirme`/`İnsan Kaynakları`/`Finans`. Bu literal, aynı zamanda seed edilen `OrgUnit.Name` olarak DB'ye yazılıyor (bkz. `DataSeeder.cs` `CreateOrgUnitOrThrow(..., spec.Name)`), yani düzeltme doğrudan demo ortamındaki gerçek departman adını değiştiriyor.
2. **Demo şirket adı** — `DataSeeder.cs`: kök `OrgUnit` (Company tipi) adı `"Headquarters"` → `"Genel Merkez"`. `Company` entity'sinin kendi adı yok, adı bağlı olduğu `OrgUnit`'ten geliyor, o yüzden tek satır yeterli.
3. **Demo çalışma takvimi adı** — `"General work schedule"` literal olarak hiçbir seed dosyasında yok; `WorkScheduleAssignmentService.EnsureDefaultScheduleAsync` her tenant için varsayılan çalışma takvimini DB'ye bir **resource-key** (`attendance.work_schedule.default.name`) olarak yazıyor, API bunu `IErrorLocalizer` ile istek anındaki kültüre göre çeviriyor (bkz. `WorkScheduleTextLocalizer` yorum satırı). Kök neden: nötr/varsayılan `SharedResource.resx`'teki Türkçe çeviri zaten vardı ama modülün geri kalanındaki "çalışma takvimi" terminolojisiyle tutarsızdı ("Genel **çalışma tablosu**"). `SharedResource.resx`'te bu anahtarın değeri `"Genel Çalışma Takvimi"` olarak düzeltildi (İngilizce `SharedResource.en.resx`'e dokunulmadı — İngilizce arayüz zaten "General work schedule" göstermeye devam ediyor, bu beklenen davranış). Bu, demoya özel değil paylaşılan bir kaynak string olduğundan tüm tenantlara (gerçek olanlar dahil) da fayda sağlıyor.
4. **58 demo çalışanın cinsiyeti** — hepsi `Gender.Unspecified` idi (Raporlar → Cinsiyet Dağılımı'nda anlamsız görünüyordu). Ayrıntı aşağıda.

## Cinsiyet Dağılımı — Nasıl Belirlendi

Kök neden: `EmployeeInfo.Gender` varsayılan değeri `Unspecified`; `DataSeeder.DemoEmployees.cs`'teki `NextDemoEmployeeInfo` hiçbir zaman bu alanı set etmiyordu.

Çözüm, isim havuzunu cinsiyete göre ikiye ayırıp seçimi alternatif havuzdan yapmaya dayanıyor — bu hem isim/cinsiyet uyumsuzluğu riskini (ör. "Ayşe" adına "Erkek" etiketi) ortadan kaldırıyor hem de matematiksel olarak tam %50/%50 sonucu garanti ediyor:

- `SeedDataConstants.cs`: eski tek `TurkishFirstNames` (56 isim) dizisi **`TurkishMaleFirstNames`** (39 isim) ve **`TurkishFemaleFirstNames`** (17 isim, "Saime" dahil) olarak ikiye bölündü. `TurkishLastNames`'e dokunulmadı (Türkçe soyadları cinsiyetsiz).
- `DataSeeder.DemoEmployees.cs` → `GenerateDemoUsersWithRoles`: 50 rastgele üretilen çalışan için, her slotta `userCount % 2` çift/tek sırayla erkek/kadın isim havuzundan seçim yapılıyor (`userCount` 0..49 arası eşit sayıda çift/tek → tam 25 erkek + 25 kadın).
- `DataSeeder.DemoEmployees.cs` → yeni `DemoGenderFor(string firstName)` yardımcı metodu: seçilen adın `TurkishFemaleFirstNames` (+ özel olarak `"Olena"`, yabancı sabit hesap adı, isim havuzunda değil) kümesinde olup olmadığına bakarak `Gender.Female`/`Gender.Male` döndürüyor — yani cinsiyet, ismin *kendisinden* türetiliyor, ayrı bir state olarak taşınmıyor; `GenerateDemoUsersWithRoles`'daki havuz seçimiyle asla çelişemez.
- `NextDemoEmployeeInfo` artık her iki `EmployeeInfo` oluşturma dalında da (`Nationality.Foreign` dahil) `Gender: DemoGenderFor(user.FirstName)` geçiyor.
- 8 sabit login hesabı: Ahmet (M), Ayşe (F), Mehmet (M), Fatma (F), Ali (M), Zeynep (F), "Test Yaş" (isim havuzunda yok → `DemoGenderFor` varsayılanı Male), Olena (F, özel case). Sonuç: 4 Erkek + 4 Kadın.
- **Toplam: 25+4=29 Erkek, 25+4=29 Kadın — 58 çalışanda tam %50/%50.**

## Dokunulan Dosyalar

- `src/Sense.Api/SeedData/SeedDataConstants.cs` — departman adları Türkçeleştirildi; `TurkishFirstNames` → `TurkishMaleFirstNames`/`TurkishFemaleFirstNames`.
- `src/Sense.Api/SeedData/DataSeeder.cs` — "Headquarters" → "Genel Merkez"; departman adı literal eşleşmeleri (`"Sales"`/`"Engineering"`) Türkçe karşılıklarına güncellendi; ilgili yorum satırı güncellendi.
- `src/Sense.Api/SeedData/DataSeeder.DemoEmployees.cs` — `"HR"` literal eşleşmesi güncellendi; `GenerateDemoUsersWithRoles` cinsiyete göre alternatif isim havuzu seçimi; yeni `DemoGenderFor` + `DemoFemaleFirstNames`; `NextDemoEmployeeInfo` her iki `EmployeeInfo` dalında `Gender` set ediyor.
- `src/Sense.Api/SeedData/DataSeeder.CompanyEvents.cs` — `"Engineering"` departman eşleşmesi ve etkinlik açıklama metni Türkçeleştirildi.
- `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx` — `attendance.work_schedule.default.name` değeri `"Genel Çalışma Takvimi"` olarak düzeltildi (terminoloji tutarlılığı, modülün geri kalanı zaten "çalışma takvimi" diyor).
- `src/Sense.Api/SeedData/README.md` — organizasyon şeması açıklaması Türkçe departman adlarıyla güncellendi (dev dokümantasyonu, müşteri karşısına çıkmıyor).
- `tests/Sense.Tests.Integration/SeedData/DemoEmployeeSeedTests.cs` — `SeedDataConstants.DemoDepartments` üzerinden `"HR"` araması yapan satır ve o testteki elle kurulmuş `hrDepartment` OrgUnit'inin adı yeni Türkçe departman adına (`"İnsan Kaynakları"`) güncellendi (`DemoTitleFor` unit adını `DemoDepartments.Name` ile eşleştirdiği için gerekliydi — aksi halde test kırılırdı). Diğer testlerdeki bağımsız/elle kurulmuş `"Headquarters"`/`"Sales"`/`"Engineering"` literal'leri `SeedDataConstants`'a bağlı olmadığından (yalnızca `ResolveDemoOrgUnit`'in nesne kimliği mantığını test ediyorlar) değiştirilmedi.

## Build/Test — Doğrulama turu (2026-09-19)

İlk oturumda disk dolu olduğu için yapılamayan derleme/test doğrulaması tamamlandı:

- `dev` üzerine rebase edildi (çakışma yok).
- `dotnet build src/Sense.Migrator`: 0 hata. `Sense.Api`, `Sense.Tests.Integration` ile birlikte derlendi: 0 hata.
- `dotnet test tests/Sense.Tests.Integration --filter FullyQualifiedName~Sense.Tests.Integration.SeedData`: **16/16 yeşil**.
- Yeni testler (`DemoEmployeeSeedTests`): erkek/kadın isim havuzları kesişmiyor; her havuz adı kendi cinsiyetine eşleniyor; sabit hesaplar (`Olena` → Kadın, `Test` → Erkek). Bunun için `DataSeeder.DemoGenderFor` `private` → `internal` yapıldı (`InternalsVisibleTo("Sense.Tests.Integration")` zaten var).
- Repo genelinde (web/e2e dahil) eski İngilizce departman/şirket literal'lerinin (`"Sales"`, `"Engineering"`, `"HR"`, `"Headquarters"`) `SeedDataConstants`'a bağlı başka kullanımı yok; Module testlerindeki aynı literal'ler bağımsız kurulumlar.
- Migration yok (yalnız seed verisi + resx değeri).

**Not:** Seed daha önce çalıştırılmış bir demo veritabanı eski İngilizce departman/şirket adlarını ve `Unspecified` cinsiyetleri korur; demo ortamında görmek için DB yeniden seed edilmeli. `attendance.work_schedule.default.name` ise DB'de anahtar olarak tutulduğundan tüm tenantlarda anında yeni değeri gösterir.

## Kapsam Dışı / Not

- `SharedResource.en.resx`'teki İngilizce "General work schedule" metnine dokunulmadı — İngilizce arayüz seçildiğinde bu metin İngilizce kalmaya devam edecek, bu beklenen/istenen davranış (yalnızca Türkçe demo sunumu hedefleniyor).
- Diğer test dosyalarındaki (`OrgChartBuilderTests`, `EmployeeIntegrationTests`, vb.) `"Sales"`/`"Engineering"`/`"Headquarters"` literalleri kendi bağımsız test kurulumları, `SeedDataConstants`'tan beslenmiyor — kapsam dışı bırakıldı.

---
*Kaynak: 1. Tur Test Bulgu Dokümanı § B-07, C-06. Agent board: `docs/agent-board/cards/senseik-043-demo-data-hygiene-round2.json`.*

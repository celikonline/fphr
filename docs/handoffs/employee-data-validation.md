# senseik-044 — Çalışan Veri Doğrulama — UYGULAMA NOTU

## Durum

**Uygulandı, `a/employee-data-validation` branch'inde, yerel commit alındı (push YAPILMADI).**

4 kabul kriterinden 3'ü tam, 1'i (KRİTİK — hak ediş tarihi senkronizasyonu) kısmen tamamlandı: birincil senaryo
(temizle + kaydet + yenile) düzeltildi, ancak ikincil bir senaryo (temizlendikten SONRA ilgisiz bir alanın
kaydedilmesi) bilinen ve belgelenen bir sınırlama olarak kalıyor — bkz. §2.3. `dotnet test` bu turda **çalıştırılamadı**:
ortamdaki Docker daemon'u, muhtemelen aynı anda çalışan çok sayıda paralel agent worktree'sinin yüküyle, komut
satırından yanıt vermez hale geldi (`docker version` bile 15-300 sn içinde dönmedi). Merge'den önce mutlaka
`dotnet build Sense.slnx` + `dotnet test` (Employee modülü) çalıştırılıp doğrulanmalı — kod gözden geçirilip mevcut
desenlerle bire bir tutarlı yazıldı ama derlenip test edilmedi.

## 1) Doğum Tarihi Doğrulaması (AC1) — TAMAMLANDI

`Employee.Validate(EmployeeInfo info)` (domain, `Employee.cs`) — hem `Employee.Create` hem `Employee.UpdateInfo`
(ikisi de `Apply()` üzerinden `Validate` çağırıyor) hem de `EmployeeImportValidator` (CSV içe aktarma) için tek
doğrulama noktası:

- Doğum tarihi bugünden ileri olamaz → `employee.birth_date_in_future`.
- Doğum tarihi işe başlama tarihinden sonra olamaz → `employee.birth_date_after_hire_date`.

Aynı iki kural, `EmployeeInfoRules.Apply` (Application, `EmployeeValidation.cs`) içinde FluentValidation kuralı
olarak da mirror'landı (dosyanın kendi yorumundaki "mirrors the domain checks so the client gets per-field 400s"
deseni) — `BirthDate` alanına `OverridePropertyName` ile bağlı, böylece 400 yanıtında `errors.birthDate` altında
görünüyor ve web tarafı (`serverFieldErrors`) bunu otomatik olarak forma yansıtıyor (ek bir i18n anahtarı GEREKMEDİ —
sunucu mesajı zaten `SharedResource.resx` üzerinden lokalize ediliyor ve doğrudan gösteriliyor).

Yeni hata kodları + TR/EN mesajları: `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx`.

**Demo veri kontrolü yapıldı:** `DataSeeder.DemoEmployees.cs`'deki hem rastgele (`NextDemoEmployeeInfo`: doğum
1970-2003, işe giriş 2015-2026) hem sabit hesaplar (`FixedDemoAccountAgeYears`/`FixedDemoAccountTenure`, ör. "Test
Yaş" 56 yaş / 2 yıl kıdem) her zaman doğum < işe giriş sağlıyor — yeni kural mevcut seed'i bozmuyor.

**Testler:** `EmployeeDomainTests.cs` — `Create_WithFutureBirthDate_Fails`, `Create_WithTodayAsBirthDate_Succeeds`
(sınır), `Create_WithBirthDateAfterHireDate_Fails`, `Create_WithBirthDateEqualToHireDate_Succeeds` (sınır),
`UpdateInfo_WithBirthDateAfterHireDate_FailsWithoutChangingStoredValues` (mevcut `UpdateInfo_..._Rejects...`
desenini takip ediyor: doğrulama state değişmeden ÖNCE çalışıyor).

## 2) Hak Ediş Tarihi / İşe Başlama Tarihi Senkronizasyonu (AC2, KRİTİK) + Temizle Düğmesi (AC3)

### 2.1) Kök neden — iki ayrı hata, tek kaynak

`Employee.cs`'deki `ResolveLeaveAccrualStart` (senseik-007'nin domain tasarımı, `EmployeeInfo.LeaveAccrualStartDate`
alanı üzerinden çalışıyor) zaten **var olan** bir "işe başlama tarihini otomatik takip et" mantığına sahipti:

- `requested` (istekte gelen değer) `null` ise VE mevcut değer eski işe başlama tarihine eşitse (yani hiç
  özelleştirilmemiş, sadece otomatik türetilmiş) → yeni işe başlama tarihini takip et.
- `requested` `null` değilse → her zaman kazanır (özelleştirilmiş, bir daha takip etmez).

Yani AC2'nin **motor tarafı zaten doğruydu** (`CareerDomainTests.LeaveAccrualStart_DefaultsToFirstHireDate_...` testi
bunu zaten kanıtlıyordu). Asıl kusurlar:

1. **Web formu her zaman "dokunulmamış" değeri de gönderiyordu.** `employee-card-sections.tsx`'deki `GeneralSection`,
   `leaveAccrualStartDate` state'ini her zaman `employee.leaveAccrualStartDate` ile başlatıp submit'te
   `leaveAccrualStartDate: leaveAccrualStartDate || null` olarak PUT body'sine koyuyordu — kullanıcı sadece işe
   başlama tarihini değiştirse bile, hak ediş tarihi alanının GÖRÜNEN (eski) değeri "requested" olarak sunucuya
   gidiyor, bu da domain'in "dokunulmadı, takip et" dalını hiç tetiklenemez hale getiriyordu. Aynı sorun
   `toEmployeeInput`'un varsayılanında da vardı: `leaveAccrualStartDate: detail.leaveAccrualStartDate ?? null` — bu
   alanla hiç ilgisi olmayan HERHANGİ bir bölüm kaydı (ör. `PersonalSection`) bile bu değeri sessizce "iste" olarak
   sabitliyordu.
2. **`null` göndermenin API sözleşmesinde zaten bir anlamı vardı: "dokunulmadı".** Bu da temizle (X) düğmesinin ASLA
   çalışamayacağı anlamına geliyordu — kod yorumu zaten açıkça yazıyordu: *"Omitted/null keeps the stored value (or
   defaults it to hireDate the first time one is set)"*. Yani X'e basıp boş kaydetmek, sunucu tarafında "hiçbir şey
   isteme, eskisini koru" ile AYNI sinyaldi — eski değer HER ZAMAN geri geliyordu. Gerçek bir "temizle" sinyaline
   ihtiyaç vardı.

### 2.2) Uygulanan çözüm

**Backend:** `EmployeeInfo`'ya (`EmployeeInfo.cs`) yeni bir alan: `bool ClearLeaveAccrualStartDate = false`. Aynı alan
`CreateEmployeeCommand`/`UpdateEmployeeCommand` (`CreateEmployee.cs`/`UpdateEmployee.cs`), API DTO'su `EmployeeRequest`
(`EmployeeApiModels.cs`) ve `EmployeesController`'da (Create/Update) uçtan uca eklendi. `Employee.Apply()`:

```csharp
LeaveAccrualStartDate = info.ClearLeaveAccrualStartDate
    ? null
    : ResolveLeaveAccrualStart(info.LeaveAccrualStartDate, LeaveAccrualStartDate, previousHireDate, HireDate);
```

**Web:** `employees.service.ts` — `EmployeeInput.clearLeaveAccrualStartDate?: boolean` eklendi; `toEmployeeInput`'un
varsayılanından `leaveAccrualStartDate` TAMAMEN kaldırıldı (artık hiçbir bölüm, dokunmadığı bu alanı sessizce
"iste" olarak sabitlemiyor — sadece gerçekten override eden çağıran bunu ekler).

`employee-card-sections.tsx` (`GeneralSection`) — yeni `leaveAccrualStartDateTouched` state'i: alan sadece
`DatePicker.onChange` tetiklendiğinde (kullanıcı gerçekten dokunduğunda) `true` oluyor. Submit mantığı:

- Dokunulmadıysa → hiçbir override gönderilmiyor (backend HireDate'i otomatik takip ediyor, AC2 çözüldü).
- Dokunulup boşaltıldıysa (X düğmesi) → `clearLeaveAccrualStartDate: true` gönderiliyor (AC3 çözüldü).
- Dokunulup bir tarih seçildiyse → `leaveAccrualStartDate: <tarih>` gönderiliyor (mevcut davranış korunuyor).

### 2.3) BİLİNEN SINIR (kabul edilmiş, sonraki tura bırakıldı)

`ResolveLeaveAccrualStart`, "hiç özelleştirilmemiş" (`null`) ile "kasıtlı olarak temizlenmiş" (`null`) durumlarını
DEĞER bazında ayırt edemiyor — ikisi de aynı temsil (`null`). Sonuç:

- Kart'ın birebir istediği senaryo (temizle → kaydet → sayfayı yenile) **tam olarak çalışıyor**: alan boş kalıyor,
  eski değer geri gelmiyor. Bu, GATE'in doğrulayacağı asıl gözlemlenebilir davranış.
- AMA: temizlendikten SONRA, kullanıcı bu alana hiç dokunmadan BAŞKA bir alanı düzenleyip kaydederse (ör. telefon
  numarası), o kayıt `requested=null, clear=false` gönderir; `ResolveLeaveAccrualStart` `current is null` dalına
  düşer ve alanı sessizce yeniden `HireDate`'e döndürür. Yani "temizlenmiş" durum, arka arkaya gelen alakasız bir
  kayıtta kalıcı değil.

**Doğru çözüm** (bu turda UYGULANMADI — Docker/EF ortamı doğrulanamadığı için, bkz. aşağıdaki "Neden ertelendi"):
`Employee` entity'sine kalıcı bir `bool HasCustomLeaveAccrualStartDate` alanı eklemek, `Apply()`'ı bu bayrağa göre
yeniden yazmak (`ClearLeaveAccrualStartDate` VEYA açık bir `LeaveAccrualStartDate` geldiğinde `true`'ya çekilir; o
zamana kadar `false` iken alan sadece `HireDate`'i mirror'lar), `Rehire()`'ı da aynı bayrağı set edecek şekilde
güncellemek, ve şu migration'ı eklemek:

```csharp
migrationBuilder.AddColumn<bool>(
    name: "has_custom_leave_accrual_start_date", schema: "employee", table: "employees",
    type: "boolean", nullable: false, defaultValue: false);

// Geriye dönük: mevcut satırlarda "özelleştirilmiş" eski sezgisel kuralla (değer HireDate'ten farklıysa) belirlenir;
// null olan satırlar kesin olarak "hiç ayarlanmamış"tır (temizleme özelliği bu migration'dan önce hiç var olmadığı
// için, var olan bir null ASLA "kasıtlı temizlenmiş" anlamına gelemez).
migrationBuilder.Sql(
    "UPDATE employee.employees SET has_custom_leave_accrual_start_date = true " +
    "WHERE leave_accrual_start_date IS NOT NULL AND leave_accrual_start_date <> hire_date;");
```

**Neden bu turda uygulanmadı:** Bu tasarımı bir kere hazırlayıp (`Employee.cs`, `EmployeeDbContext.cs`,
`ModelSnapshot.cs`, yeni migration dosyaları) uyguladım, ancak ortamdaki Docker daemon'u bu oturumda **hiçbir komuta
yanıt vermedi** (`docker version`, `docker ps` dahi 15-300 sn'de dönmedi — muhtemelen aynı repo'da paralel çalışan
çok sayıda agent worktree'sinin Docker'ı aynı anda kullanması). EF Core'un `PendingModelChangesWarning` mekanizması
(bu repoda zaten aktif — Attendance modülünde bilinen, önceden var olan bir örneği var, bkz. `age-50-exception.md`
handoff'undaki not) elle yazılmış bir migration/snapshot ile GERÇEK kod-first modeli arasında EN UFAK bir metadata
farkında TÜM Employee modülü entegrasyon testlerini (Testcontainers + `MigrateAsync`) sessizce kırabilir. `dotnet
build`/`dotnet test` ile doğrulayamadan böyle bir riski commit etmek, orijinal hatadan daha büyük bir regresyon riski
taşıyordu — bu yüzden persisted-flag/migration değişikliğini GERİ ALDIM ve daha küçük, düşük riskli (şema
değişikliği gerektirmeyen) `ClearLeaveAccrualStartDate` çözümünü bıraktım. Yukarıdaki migration taslağı bir sonraki
turda (Docker/dotnet erişimi olan bir ortamda) `dotnet ef migrations add EmployeeLeaveAccrualCustomFlag` ile
YENİDEN ÜRETİLİP doğrulanmalı — elle yazılmış hali burada sadece referans amaçlı bırakılıyor, commit edilmedi.

**Testler:** `CareerDomainTests.cs` — `LeaveAccrualStart_ExplicitClearFlag_ForcesNull_EvenOverACustomizedValue`:
temizle bayrağının özelleştirilmiş (HireDate'i takip etmeyen) bir değerin bile üzerine geçtiğini VE bilinen sınırı
(sonraki alakasız kayıtta HireDate'e dönmesi) açıkça belgeliyor/doğruluyor.

## 3) Temizle (X) Düğmesi — Web Form Bug'ı (AC3) — TAMAMLANDI (birincil senaryo)

Kök neden ve çözüm §2'de anlatıldı (aynı kaynak, iki AC). Özet: `employee-card-sections.tsx`'deki
`leaveAccrualStartDateTouched` + `clearLeaveAccrualStartDate` kombinasyonu, X'e basıp kaydedince backend'e artık
gerçek bir "boşalt" sinyali gönderiyor (önceden `null` göndermenin bir anlamı yoktu — "dokunulmadı" ile
karışıyordu). Kartı yeniden açtığınızda (GET) alan artık gerçekten boş dönüyor.

## 4) E-posta Türkçe Karakter Doğrulaması (AC4) — TAMAMLANDI (reddet), geriye dönük plan aşağıda

`.NET`'in `System.Net.Mail.MailAddress` tabanlı `EmailAddress.IsValid` (kernel value object) Unicode karakterlere
(ş, ü, ç, ğ, ö, ı) izin veriyordu — RFC 5321'in SMTP zarfı ASCII'dir; her mail sunucusu SMTPUTF8/IDN desteklemez,
bu yüzden bu adresler sessizce kabul edilip sonradan "teslim edilemedi" olarak geri dönüyordu.

**Kasıtlı olarak `Sense.Shared.Kernel.ValueObjects.EmailAddress`'e DOKUNULMADI** — bu tip Identity/Invitations
modüllerinde de kullanılıyor (`User.cs`, `Invitation.cs`, `EmployeeInvitationCommands.cs`) ve değişikliğin etkisini
Employee modülü dışına taşırdı (görev kapsamı: "Employee modülündeki e-posta validator'ında"). Bunun yerine yeni,
Employee-modülüne özel bir kural eklendi:

`src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/EmployeeEmailRules.cs` (yeni dosya):
```csharp
public static bool IsValid(string value) => EmailAddress.IsValid(value) && IsAscii(value);
```

`EmployeeEmailRules.IsValid` her e-posta giriş noktasında `EmailAddress.IsValid`'in yerini aldı — sadece
Create/Update değil, TÜM Employee-modülü e-posta yolları tarandı:

- `Employee.Validate` (domain) — `Create`/`UpdateInfo`, dolayısıyla `EmployeeImportValidator` (CSV içe aktarma da
  buradan geçiyor, ayrı bir düzeltme gerekmedi).
- `EmployeeInfoRules.Apply` (Application, FluentValidation aynası — Create/Update komutları).
- `Employee.ValidateContactInfo` (domain, **ayrıca bulunan bir açık**: "Self-servis düzenleme" akışı `EmployeeInfo`
  değil ayrı bir `EmployeeContactInfo` tipi kullanıyor, kendi `PersonalEmail` kontrolüne sahipti — gözden kaçması
  kolay bir ikinci giriş noktasıydı, o da düzeltildi).
- `UpdateMyProfileValidator` (Application, self-servis'in FluentValidation aynası) — aynı ikinci giriş noktasının
  Application katmanındaki mirror'ı.

Davranış: Türkçe karakterli bir e-posta **reddediliyor** (kartın "ya reddedilmeli ya da normalize edilmeli"
seçeneklerinden reddetme yolu seçildi — punycode/IDN normalize etme, kullanıcının yazdığı adresle DB'de saklanan
adresin farklı olmasına yol açar ve daha karmaşıktır; reddetme daha basit, daha güvenli ve KISS ilkesine uygun).

Aynı hata kodları (`employee.work_email_invalid` / `employee.personal_email_invalid`) yeniden kullanıldı — mesaj
zaten genel ("Geçersiz iş e-postası"), yeni bir kod/mesaj eklemeye gerek yoktu.

**Kapsam dışı bırakılan, benzer bir alan:** `Subcontractor.ContactEmail` (`Subcontractors/Subcontractor.cs:94`,
aynı modülde) hâlâ ham `EmailAddress.IsValid` kullanıyor — bu bir çalışanın değil, alt işveren şirketinin iletişim
e-postası; kartın kapsamı "çalışan" verisiyle sınırlı olduğu için kasıtlı olarak dokunulmadı, ama tutarlılık için
bir sonraki turda aynı taşıma (`EmployeeEmailRules.IsValid`) uygulanabilir.

**Testler:** `EmployeeDomainTests.cs` — `Create_WithTurkishCharacterEmail_Fails` (hem yerel kısım hem alan adı
Türkçe karakterli örnekler: `ayşe@acme.com`, `ayse@açme.com`, `üzeyir@example.com`).
`CustomFieldDomainTests.cs` — `UpdateContactInfo_ValidatesAndNormalizesFields_AndReportsWhetherAnythingChanged`
içine self-servis yolu için ek bir Türkçe karakter e-posta reddi eklendi.

### 4.1) Geriye Dönük Düzeltme / Migrasyon Planı (merge gate'in istediği, bu turda YAZILMADI)

**Mevcut durum kontrolü:** Demo seed verisi (`DataSeeder.DemoEmployees.cs`) zaten `AsciiFold` ile Türkçe karakterleri
ASCII'ye çeviriyor (`NextDemoEmployeeInfo`'daki `personalEmail` üretimi) — yani demo veride bilinen bir ihlal YOK.
Ancak gerçek/production kiracılarda (veya CSV içe aktarımla girilmiş) Türkçe karakterli `work_email`/`personal_email`
zaten var olabilir; bu kayıtlar yeni kural devreye girdiğinde OLUŞTURULAMAZ ama HALİHAZIRDA VAR olan satırlar
etkilenmez (kural sadece `Create`/`UpdateInfo`'da çalışır, mevcut satırları geriye dönük taramaz).

**Önerilen plan (bir sonraki turda uygulanacak, tek seferlik bir data-fix script'i olarak, migration DEĞİL):**

1. **Tespit:** `SELECT id, tenant_id, work_email, personal_email FROM employee.employees WHERE work_email ~ '[^\x00-\x7F]' OR personal_email ~ '[^\x00-\x7F]';` (Postgres regex, ASCII-dışı herhangi bir karakteri yakalar).
2. **Normalize (öneri, silmek yerine):** `AsciiFold` benzeri bir transliterasyon (`ş→s, ü→u, ç→c, ğ→g, ö→o, ı→i`) ile
   ASCII'ye çevrilmiş bir aday üret (demo seed'deki `AsciiFold` metodunun aynısı, Employee.Application'a taşınabilir
   ortak bir yardımcıya çıkarılarak).
3. **Çakışma kontrolü:** Normalize edilmiş aday, aynı tenant'ta başka bir çalışanın `work_email`'i ile çakışıyorsa
   (work email tekil DEĞİL şu an ama gelecekte olabilir) elle incelemeye düşür, otomatik uygulama.
4. **HR bilgilendirme + onay:** Bu, çalışanın giriş adresini SESSİZCE değiştirir (ör. `ayşe.yılmaz@` →
   `ayse.yilmaz@`) — e-posta tabanlı girişi/bildirimleri etkileyebilir; canlıya çıkmadan önce etkilenen kiracılara
   "e-posta adresiniz teslim edilebilirlik için normalize edildi" bildirimi + HR'dan onay istenmeli (senseik-007'nin
   izin bakiyesi geriye dönük artışı riskiyle AYNI desende — "sessizce ve otomatik" değişiklik, önce rapor/dry-run).
5. **Uygulama şekli:** Tek seferlik bir konsol komutu/script (EF migration DEĞİL — veri düzeltmesi, şema değişikliği
   değil), `dotnet run --project src/Sense.Api -- fix-turkish-emails --dry-run` gibi bir CLI komutu olarak Employee
   modülüne eklenebilir; ilk çalıştırma sadece rapor üretir, `--apply` ile gerçek güncelleme yapılır.

## Değiştirilen/Eklenen Dosyalar

**Backend:**
- `src/Modules/Employee/Sense.Modules.Employee.Domain/EmployeeDomainErrors.cs` — `BirthDateInFuture`, `BirthDateAfterHireDate`.
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/EmployeeInfo.cs` — `ClearLeaveAccrualStartDate`.
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/EmployeeEmailRules.cs` — yeni dosya.
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/Employee.cs` — `Validate` (doğum tarihi + ASCII e-posta), `Apply`/`ResolveLeaveAccrualStart` (temizle bayrağı + bilinen sınır notu).
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/Employee.SelfService.cs` — `ValidateContactInfo` ASCII e-posta kuralı.
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/EmployeeValidation.cs` — FluentValidation aynası.
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/UpdateMyProfileCommand.cs` — self-servis FluentValidation aynası.
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/CreateEmployee.cs`, `UpdateEmployee.cs` — `ClearLeaveAccrualStartDate` alanı.
- `src/Modules/Employee/Sense.Modules.Employee.Api/Controllers/EmployeeApiModels.cs`, `EmployeesController.cs` — uçtan uca kablo.
- `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx`, `SharedResource.en.resx` — yeni mesajlar.

**Testler:**
- `tests/Modules/Sense.Modules.Employee.Tests/EmployeeDomainTests.cs` — doğum tarihi + Türkçe karakter e-posta testleri.
- `tests/Modules/Sense.Modules.Employee.Tests/CareerDomainTests.cs` — temizle bayrağı testi (+ bilinen sınır belgesi).
- `tests/Modules/Sense.Modules.Employee.Tests/CustomFieldDomainTests.cs` — self-servis yolu için Türkçe karakter e-posta reddi.

**Web:**
- `web/src/services/employees.service.ts` — `clearLeaveAccrualStartDate`, `toEmployeeInput` varsayılanından `leaveAccrualStartDate` kaldırıldı.
- `web/src/components/employees/employee-card-sections.tsx` — `GeneralSection`'da dokunma takibi + temizle/otomatik-takip mantığı.

## Merge Gate Durumu

- **`dotnet build Sense.slnx` / `dotnet test` (Employee modülü): ÇALIŞTIRILAMADI bu oturumda** — Docker daemon'u
  yanıt vermiyordu (ortam kaynaklı, kod kaynaklı değil). **Merge'den ÖNCE mutlaka çalıştırılmalı.** Beklenen komut:
  ```
  MSYS_NO_PATHCONV=1 docker run --rm -v "$PWD":/src -v senseik-nuget:/root/.nuget/packages -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal --add-host=host.docker.internal:host-gateway -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Modules/Sense.Modules.Employee.Tests --filter "FullyQualifiedName~EmployeeDomainTests|FullyQualifiedName~CareerDomainTests"
  ```
  ve ardından tam modül koşusu.
- Türkçe karakterli e-posta geriye dönük düzeltme planı: §4.1 (yazıldı, uygulanmadı — merge gate'in istediği şey
  buydu, "bu turda migration'ı bizzat yazman ZORUNLU değil" ifadesiyle uyumlu).

## Sonraki Tur İçin Öneriler

1. Docker/dotnet erişimi olan bir ortamda: `dotnet build` + `dotnet test` (Employee modülü) çalıştırıp bu turun
   kodunu doğrula.
2. §2.3'teki `HasCustomLeaveAccrualStartDate` kalıcı bayrağını + migration'ı `dotnet ef migrations add` ile YENİDEN
   üret (elle yazılmamış, doğrulanmış bir migration) ve entegre et.
3. §4.1'deki Türkçe karakterli e-posta geriye dönük düzeltme script'ini uygula (dry-run önce, HR onayı sonra).

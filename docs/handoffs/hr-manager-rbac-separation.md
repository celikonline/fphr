# senseik-050 — HR Manager / System Administrator RBAC Ayrımı — UYGULAMA PLANI

## Implementasyon Notu

**Durum: GATE 1 ve GATE 2 onaylandı, uygulandı, branch `a/hr-manager-rbac-separation` (dev'den).**

### Yapılan değişiklik

`src/Modules/Identity/Sense.Modules.Identity.Application/Roles/SystemRoleDefinitions.cs` — `HR` (görünen ad "HR
Manager" / "İK Yöneticisi") sistem rolünün include listesi hâlâ `"identity.users.*"` joker'ini taşıyor (hot-file
append-only kuralı gereği include dizisine dokunulmadı), ama Exclude dizisine üç anahtar eklendi:

- `identity.users.manage` — "Create/edit users and assign roles" (kullanıcı oluşturma/düzenleme **+ herhangi bir role
  atama**; `CreateUserCommand`, `UpdateUserCommand`, `SetUserRolesCommand`, `SetUserAccessTypeCommand`,
  `SetPlatformStaffCommand` ve — bugünkü kod yoluyla — `ResetUserMfaCommand` bu tek izinle korunuyor, bkz.
  `IdentityPermissions.cs` + `UserRolesAccess.cs`/`UserStatus.cs`/`CreateUser.cs`/`UpdateUser.cs`).
- `identity.users.suspend` — "Suspend/enable users" (`SetUserStatusCommand`, `UserStatus.cs:16`).
- `identity.users.reset_mfa` — "Reset a user's 2FA" (kod yolunda fiilen kullanılmıyor — 2FA sıfırlama bugün
  `identity.users.manage` ile korunuyor, `IdentityPermissions.cs:15` yorumunda "Kullanılmıyor — backlog B-3-01"
  olarak işaretli — yine de görev talimatındaki üç kalemi birebir karşılamak ve B-3-01 hayata geçerse otomatik
  doğru davranmak için include joker'inden hariç tutuldu).
- `identity.users.impersonate` zaten hariçti, değişmedi.

HR Manager artık şunları **kaybetti**: kullanıcı oluşturma/düzenleme, herhangi bir kullanıcıya rol atama/erişim
türü değiştirme, kullanıcı askıya alma/etkinleştirme, yönetici tarafından 2FA sıfırlama, platform personeli
bayrağı değiştirme. Bunların hepsi artık yalnız **System Administrator** (Owner/Admin — joker `"*"` ile) elinde.

HR Manager'da **kalanlar**: `identity.users.view` (kullanıcı listesini görüntüleme), `identity.users.invite`
(işe alım/onboarding için kullanıcı davet etme — `identity.users.*` joker'inden hariç tutulmadı, bilinçli), ve
`identity.roles.view` (rol listesini görüntüleme, ayrı satırda zaten vardı, dokunulmadı).

### Neden Exclude'a ekleme, Include'dan çıkarma değil

`docs/implementation/paralel-gelistirme-plani.md` §5 bu dosyayı "sıcak dosya" işaretliyor: kural sadece **ekleme**,
mevcut satırları yeniden sıralama/biçimlendirme yok. `"identity.users.*"` include joker'ini daraltıp
`"identity.users.view", "identity.users.invite"` gibi açık anahtarlara bölmek satırı yeniden yazmak anlamına
gelirdi; onun yerine — dosyada zaten kullanılan desen olan (Timekeeper'ın `"attendance.*"`den `attendance.shift.lock`
vb. anahtarları hariç tutması, HRSpecialist'in `PermissionPatterns.Sensitive`'i hariç tutması gibi) — üç anahtar
Exclude dizisine eklendi. Diff `git diff` ile doğrulandı: yalnız HR'ın Exclude satırı değişti, dosyanın geri kalanı
bire bir aynı.

### `SystemRolePermissionSynchronizer` — yetki azaltma senaryosu doğrulaması

`Role.SyncPermissionsUnchecked` (`src/Modules/Identity/Sense.Modules.Identity.Domain/Roles/Role.cs:96-123`) zaten
genel amaçlı diff uyguluyor: `_permissions.RemoveAll(p => !target.ContainsKey(p.Permission))` — yeni tanımda
artık eşleşmeyen (bizim durumumuzda `identity.users.manage/.suspend/.reset_mfa`) satırları **kaldırıyor**, eksik
olanları ekliyor, kapsamı farklı olanları güncelliyor. Bu mekanizma senseik-050'den önce de vardı ve zaten
`AccessTypeRoleSyncAndSelfCardTests.SystemRoleSync_AddsMissingAndRemovesStalePermissions_...` testiyle genel
olarak kanıtlanmıştı; bu kartta HR'a özel, gerçek bir izin **azaltma** senaryosunu hedefleyen ayrı bir test eklendi
(bkz. Testler). API başlangıcında `SystemRolePermissionSyncHostedService` bunu tüm kiracılar için otomatik
çalıştırıyor — mevcut kiracıların HR rolündeki `identity.users.manage` vb. satırları bir sonraki API başlatmasında
(ya da manuel `SyncAllTenantsAsync` çağrısında) otomatik temizlenecek, elle migrasyon gerekmiyor.

### Pre-existing test güncellemesi (davranış değişikliği nedeniyle gerekli)

`tests/Sense.Tests.Integration/Identity/AccessTypeRoleSyncAndSelfCardTests.cs` içindeki
`AccessType_OnlyOwnerTouchesOwner_AdminManagesAdminAndEmployee_HrNeither_LastOwnerKept` testi eskiden HR'ın
`identity.users.manage`'e sahip olduğunu ama Owner/Admin'e rol değişikliği yaparken `AccessTypeGuard`'ın
domain-seviyeli `user.access_type_forbidden` hatasıyla durdurulduğunu doğruluyordu; Owner/Admin **olmayan** bir
hedefin erişim türünü değiştirmesine ise izin veriyordu (`SetUserAccessTypeCommand(targetId, Employee)` başarıyla
dönüyordu). Bu davranış artık geçersiz: HR `identity.users.manage`'i hiç taşımadığı için istek
`AuthorizationBehaviour`'da genel `auth.forbidden` (`ErrorCodes.Forbidden`) ile daha erken reddediliyor —
`AccessTypeGuard`'a hiç ulaşmıyor. Test dört satırlık HR bloğunda güncellendi: dördü de artık
`ErrorCodes.Forbidden` bekliyor (üçü zaten Forbidden bekliyordu, hata kodu `AccessTypeForbidden`'dan
`Forbidden`'a değişti; dördüncüsü — Employee'ye no-op değişiklik — eskiden başarıyla dönerken artık reddediliyor).
`AccessTypeGuard`'ın kendisi ölü kod değil: `ITAdmin` sistem rolü hâlâ `"identity.*"` joker'iyle
`identity.users.manage`'i taşıyor, dolayısıyla Owner/Admin geçişlerinde ITAdmin için hâlâ anlamlı.

## Testler

Tümü Docker `mcr.microsoft.com/dotnet/sdk:10.0` konteyneriyle (host'ta dotnet yok) çalıştırıldı ve yeşil:

1. **Unit — `tests/Modules/Sense.Modules.Identity.Tests/Roles/SystemRoleDefinitionsTests.cs`**
   Yeni test: `HR_LosesIdentityManagementPermissions_ButKeepsUserViewAndInvite` — `Resolve(HR)` artık
   `identity.users.manage/.suspend/.reset_mfa/.impersonate` içermiyor, `identity.users.view/.invite` ve
   `identity.roles.view` içermeye devam ediyor. Proje: **12/12 yeşil** (10 eski + 2 yeni; katalog fixture'ına
   `identity.users.*` anahtarları eklendi).
2. **Integration — `tests/Sense.Tests.Integration/Identity/AccessTypeRoleSyncAndSelfCardTests.cs`**
   - Yeni test: `SystemRoleSync_RemovesStaleIdentityManagementGrant_FromExistingHrRole` — gerçek yetki **azaltma**
     senaryosu: bir kiracı seed edilir, HR rolüne elle (yeni tanımı bypass ederek) eski `identity.users.manage/
     .suspend/.reset_mfa` satırları eklenir ("bu değişiklikten önce seed edilmiş kiracı" simülasyonu),
     `SyncAllTenantsAsync` çağrılır → `1` değişen rol döner, üç izin de kaldırılmış olarak doğrulanır
     (`IPermissionService.HasAsync` false), `identity.users.view/.invite` dokunulmamış (true) olarak kalır;
     ardından HR kullanıcısıyla gerçekten `SetUserRolesCommand`/`SetUserStatusCommand`/`ResetUserMfaCommand`
     çağrılır ve `ErrorCodes.Forbidden` ile reddedildiği doğrulanır (izin katmanında gerçekten etkili olduğu,
     salt DB satırı değil); tekrar `SyncAllTenantsAsync` çağrısı `0` döner (idempotent).
   - Güncellenen test: `AccessType_OnlyOwnerTouchesOwner_AdminManagesAdminAndEmployee_HrNeither_LastOwnerKept`
     (yukarıda açıklanan HR bloğu).
   - Ad-hoc filtre: `--filter "FullyQualifiedName~AccessTypeRoleSyncAndSelfCardTests"` → **4/4 yeşil**.
   - Tüm `Sense.Tests.Integration.Identity` namespace'i (regresyon taraması): **31/31 yeşil**.
3. **Unit — tüm Identity modülü** (`tests/Modules/Sense.Modules.Identity.Tests`): **97/97 yeşil**.
4. `dotnet format Sense.slnx --include <değişen 3 dosya> --verify-no-changes` — biçimlendirme farkı yok.

Not: ilk entegrasyon koşusunda host'ta paralel çalışan başka worktree/agent build'leri yüzünden bir kez
"Cannot allocate memory" (satellite assembly restore hatası) alındı — kod kaynaklı değil, tekrar çalıştırınca
geçti; bu transient host kaynak çakışması dışında hiçbir başarısız test kalmadı.

## Salary/Bank Accounts ayrımı — TASARIM ÖNERİSİ (bu kartta implement EDİLMEDİ)

Görev talimatı bu ikisinin aynı kişide birleşmesini önlemeyi ya da en azından riski belgeleyip mümkünse basit bir
maker-checker öneri istiyor; kapsamı net tutmak için burada yalnız **tasarım** var, kod değişikliği yok.

### Mevcut durum (risk)

`SystemRoleDefinitions.cs`'te "Edit Salary" (`employee.salary.edit`) ve "Edit Bank Accounts"
(`employee.bank_accounts.edit`) **aynı rolde birleşiyor**, iki yerde:

- **HR** (satır ~62-67): include'da `"employee.*"` joker'i var, `PermissionPatterns.Sensitive`
  (`*.salary.*` dahil) Exclude'da yok → HR hem maaş hem banka hesabı düzenleyebiliyor. Bu senseik-050'nin
  kapsamı olan **kimlik** yönetimi yetkilerinden ayrı bir risk ailesi (compensation/finans SoD), bu kartta
  dokunulmadı.
- **PayrollSpecialist** (satır ~79-82): include'da açıkça `"employee.salary.*", "employee.bank_accounts.*"`
  birlikte listeleniyor — aynı kişi hem maaş miktarını değiştirip hem parayı gideceği banka hesabını
  değiştirebiliyor; klasik "kurgu maaş + kendi/suç ortağının hesabı" dolandırıcılık deseni.
  `SystemRoleDefinitionsTests.SalaryEditAndBankAccounts_GoToHrAndPayroll_ButSalaryNeverToHRSpecialistOrManager`
  testi bu birleşmeyi zaten **bilinçli olarak** doğruluyor (ürün kararı, bu kartın kapsamı değişmedi) —
  yani bugünkü sistemde bu iki yetkinin ayrı kişilerde olması **zorunlu değil**, tenant kendi rol/kullanıcı
  atamasıyla ayırabilir ama sistem bunu dayatmıyor.

### Önerilen tasarım: maker-checker (ikinci onay) — ayrı kart

Büyüklük gerekçesiyle (yeni onay durumu, bildirim, UI, muhtemelen Workflow modülü entegrasyonu) bu kartta
**implement edilmiyor**; ayrı bir agent-board kartı önerisi:

1. **Mekanizma**: `employee.salary.edit` veya `employee.bank_accounts.edit` gerektiren komutlar (muhtemelen
   `Sense.Modules.Employee.Application`'da maaş/banka hesabı güncelleme handler'ları) doğrudan uygulanmak yerine
   `PendingSensitiveChange` (adı örnek) taslağı olarak kaydedilir; ikinci bir kullanıcı (aynı yetkiye sahip ama
   `currentUser.UserId != requester`) onaylayana kadar etkin olmaz. Bu, mevcut `Workflow` modülünün
   `ApprovalRequest`/`ApprovalTask` altyapısına (bkz. `src/Modules/Workflow/...`) doğal olarak oturur — yeni bir
   onay motoru yazmak yerine oradaki soyutlamalar genişletilebilir.
2. **Kapsam önerisi**: yalnız *aynı çalışan kaydında* hem maaş hem banka hesabı değişikliğini **aynı gün**
   birleştiren durumları değil — daha basit ve daha az yıkıcı bir ilk adım olarak yalnız
   `employee.bank_accounts.edit` (dolandırıcılık riski daha yüksek — para akışını değiştirir) için maker-checker
   zorunlu kılınabilir; `employee.salary.edit` mevcut tek-onaylı akışta kalabilir. Bu, "büyük iş" riskini
   azaltır ve önce en yüksek riskli yüzeyi kapatır.
3. **Alternatif/daha basit ilk adım** (bu risk azaltımı tek başına ayrı, küçük bir kart olabilir): banka hesabı
   değişikliklerinde zorunlu bildirim — değişikliği yapan kişi dışında en az bir Owner/Admin'e (veya ayrı bir
   "Finance" rolüne) anlık bildirim + denetim kaydı vurgusu (`identity.audit.sensitive_view` ile filtrelenebilir
   bir "hassas alan değişti" etiketi). Bu, onayı engellemez ama tespit süresini kısaltır — maker-checker'dan
   çok daha ucuz, hemen uygulanabilir bir ara adım.
4. **Roller üzerinde etkisi**: `PayrollSpecialist`'in `employee.salary.*` + `employee.bank_accounts.*` birlikte
   include'u aynen kalır (maker-checker rol ayrımı değil, işlem ayrımı yapar — aynı kişi iki işlemi de
   başlatabilir ama banka hesabı değişikliği başka biri onaylamadan etkinleşmez). HR'ın `employee.*` joker'i
   üzerinden aynı iki izne sahip olması da aynı şekilde etkilenir.
5. **Test etkisi**: `SystemRoleDefinitionsTests.SalaryEditAndBankAccounts_GoToHrAndPayroll_...` testi rol
   düzeyinde değişmez (izin ataması aynı kalıyor); yeni testler işlem-seviyesinde olur (ör.
   `BankAccountEditRequiresSecondApprover_WhenApproverIsSameAsRequester_IsRejected`).

**Bu kartta yapılmayanlar (bilinçli):** yukarıdaki maker-checker/bildirim mekanizmasının kodu, `PayrollSpecialist`
veya `HR`'ın salary/bank_accounts izinlerinin ayrılması (ürün kararı olarak zaten bilinçli birleşik, testle
korunuyor) — bunlar ayrı bir agent-board kartına (`senseik-0XX-salary-bank-maker-checker` önerilir) taşınmalı.

## Değişen Dosyalar

- `src/Modules/Identity/Sense.Modules.Identity.Application/Roles/SystemRoleDefinitions.cs` — HR Exclude'a 3 anahtar
  (yalnız gerekli satır, diğer satırlara dokunulmadı).
- `tests/Modules/Sense.Modules.Identity.Tests/Roles/SystemRoleDefinitionsTests.cs` — katalog fixture'ına
  `identity.users.*` anahtarları + yeni test.
- `tests/Sense.Tests.Integration/Identity/AccessTypeRoleSyncAndSelfCardTests.cs` — yeni senkronizasyon/azaltma
  testi + mevcut AccessType testinin HR bloğu güncellemesi.

## Backlog Önerileri

1. `senseik-0XX-salary-bank-maker-checker` — yukarıdaki tasarımın implementasyonu (Workflow modülü entegrasyonu,
   tahmini orta-büyük iş).
2. `IdentityPermissions.UsersResetMfa` (`identity.users.reset_mfa`) backlog B-3-01: bugün hiçbir handler'da
   kullanılmıyor, 2FA sıfırlama fiilen `identity.users.manage` ile korunuyor. Bu kart onu HR'ın Exclude'una
   gerekçe gereği ekledi ama gerçek kod yolunu değiştirmedi — B-3-01 kararı (kaldır ya da gerçekten
   `ResetUserMfaCommand`'a bağla) hâlâ bekliyor; bağlanırsa bu kartın Exclude eklemesi zaten doğru davranacak.

---
*Kaynak: agent-board kartı `docs/agent-board/cards/senseik-050-hr-manager-rbac-separation.json` (bu çalışma
sırasında worktree'de bulunamadı — görev talimatındaki ayrıntılara göre uygulandı).*

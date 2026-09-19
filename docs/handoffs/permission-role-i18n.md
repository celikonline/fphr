# senseik-040 — İzin/Rol İsimleri Türkçeleştirme — UYGULAMA NOTU

## Durum: GATE 1/2 onaylı, uygulandı, branch `a/permission-role-i18n`.

## Özet

Kapsam üç ayrı bulguya ayrıldı; ikisi zaten büyük ölçüde tamamlanmıştı, üçüncüsü (permission display name'leri
ve Profilim/Roller rol adı tutarsızlığı) gerçek işti.

1. **Rapor kırılım değerleri (Promotion/LateralMove/Demotion/TitleChange/Unspecified/WithinFirstSixMonths):
   zaten tam Türkçe.** `web/public/locales/{tr,en,de}/reports.json` → `kpi.dimensions.*` anahtarları mevcut ve
   `workforce-kpi-panel.tsx`'in `dimensionLabel()` fonksiyonu zaten `t("reports:kpi.dimensions.${dimension}", { defaultValue: dimension })`
   ile çözüyor. Dokunulmadı.
2. **17 sistem rolü adı/açıklaması: zaten tam Türkçe (backend .resx).** `SharedResource.resx`'te
   `roles.{code}.name` / `roles.{code}.description` (17 rol × 2 = 34 anahtar) hepsi Türkçe, `.en.resx`'te İngilizce
   karşılıkları da mevcut. `RolesController.Localize()` bunu zaten kullanıyor. Dokunulmadı.
3. **~170 izin display name'i: 16/20 modülde literal İngilizce idi (gerçek bug).** `IdentityPermissions.cs`
   (18 izin) permission KEY'ini DisplayName olarak kullanıp `PermissionsController`'da
   `Localizer.MessageOrText(...)` ile resx'ten çözüyor (zaten doğru desen) — ama `PerformancePermissions.cs`,
   `TrainingPermissions.cs`, `AnnouncementsPermissions.cs` (3 modül) DisplayName/Group'u doğrudan Türkçe literal
   string olarak tanımlıyor. **Geri kalan 16 modül (Analytics, Assets, Attendance, DataTransfer, Documents,
   Employee, Expense, Integrations, Leave, Notifications, Onboarding, Organization, Payroll, Recruitment,
   Reporting, Workflow — toplam 150 izin) DisplayName/Group'u literal İNGİLİZCE string olarak tanımlıyordu**
   (`MessageOrText` bir kelime içinde boşluk gördüğü an anahtar aramadan olduğu gibi döndürüyor — bkz.
   `ErrorLocalizer.MessageOrText`), yani PermissionsController'dan geçtikten sonra bile hep İngilizce kalıyordu.
4. **Rol adı Profilim ⇄ Kullanıcılar→Roller tutarsızlığı: gerçek bug, ayrı kök neden.** İki ayrı çeviri kaynağı
   vardı: "Kullanıcılar → Roller" (`RolesController.Localize`) backend resx'ten okuyordu (`roles.hr.name` =
   "İK Yöneticisi"), "Profilim" (`account.tsx`) ise `web/public/locales/*/account.json`'daki ayrı, elle
   bakımı yapılan `roleNames` sözlüğünden okuyordu (`HR` = "İK"). İki tablo zamanla birbirinden sapmıştı — örnek
   farklar: Admin ("Yönetici" vs "Sistem Yöneticisi"), HR ("İK" vs "İK Yöneticisi"), Manager ("Birim Yöneticisi"
   vs "Yönetici"), ExternalAccountant ("Dış Muhasebeci" vs "Mali Müşavir (Dış)"), Contractor ("Yüklenici" vs
   "Dış Kaynak").

## Yapılan Değişiklikler

### 1. 16 modülün izin kataloğu Türkçeleştirildi (backend, `*Permissions.cs`, sadece DisplayName/Group/Description)

`src/Modules/{Analytics,Assets,Attendance,DataTransfer,Documents,Employee,Expense,Integrations,Leave,
Notifications,Onboarding,Organization,Payroll,Recruitment,Reporting,Workflow}/**/​*Permissions.cs` — Performance
modülünün mevcut deseni (DisplayName/Group doğrudan Türkçe literal, `Localizer.MessageOrText` boşluklu string'i
olduğu gibi geçirir) şablon alındı. **Permission KEY'leri (`"employee.salary.edit"` gibi) hiçbiri değişmedi** —
yalnız üçüncü pozisyonel parametre (DisplayName), Group ve Description Türkçeleştirildi. `Identity` modülüne
dokunulmadı (zaten resx-key deseniyle doğru çalışıyordu); `Performance`/`Training`/`Announcements`'a dokunulmadı
(zaten Türkçe idi).

Terminoloji tutarlılığı için kullanılan grup adları: Analitik, Zimmet, Puantaj, Veri Aktarımı, Belgeler, Özlük,
Harcama, Entegrasyonlar, İzin, Bildirimler, Oryantasyon, Şirket Yapısı, Bordro, İşe Alım, Raporlar, Onay Süreçleri.

### 2. Rol adı tutarlılığı — backend + frontend

- **Backend** (`src/Modules/Identity/Sense.Modules.Identity.Api/Controllers/AuthController.cs`,
  `MeController.Get`): `RolesController.Localize()` ile birebir aynı mekanizma eklendi — sistem rolü kodları için
  `Localizer.MessageOrText(SystemRoleCodes.NameKey(code))` çağrılıyor, `/v1/identity/me`'nin döndürdüğü
  `MeDto.Roles[].Name` artık "Kullanıcılar → Roller" ekranıyla **aynı resx anahtarından** geliyor.
- **Frontend** (`web/src/store/auth.store.ts`): `User` tipine `roleLabels: string[]` eklendi (`toUser()` artık
  `me.roles.map(r => r.name ?? r.code)`); `roles: string[]` (kodlar) geriye dönük uyumluluk için korundu.
  `toUser` test edilebilirlik için export edildi.
- **Frontend** (`web/src/pages/account.tsx`): rol rozetleri artık `t(\`account:roleNames.${role}\`, role)` yerine
  doğrudan `user.roleLabels[index]` render ediyor — ayrı, senkronize tutulması gereken bir çeviri tablosu yok.
- **Ölü kod temizliği**: `web/public/locales/{tr,en,de}/account.json`'daki `roleNames` bloğu (asıl tutarsızlık
  kaynağı) silindi; hiçbir yerden referans kalmamıştı.

**Bilinen sınır (kapsam dışı bırakıldı):** Backend rol/izin yerelleştirmesi yalnız `SharedResource.resx` (tr,
nötr) ve `SharedResource.en.resx` (en) olarak var — CLAUDE.md'nin belgelediği mevcut kural. `de` için backend
resx'i yok; bir Almanca kullanıcı için `/me` ve `/roles` ikisi de artık aynı şekilde (tr'ye düşerek) davranıyor —
önceden Profilim ekranı en azından kendi ayrı `account.json` çevirisinden gerçek Almanca gösteriyordu. Bu, iki
ekranı **tutarlı** kılmanın kaçınılmaz bir yan etkisi; gerçek düzeltme (backend'e `SharedResource.de.resx`
eklemek) bu kartın kapsamı dışında, ayrı bir kart önerilir.

## Eklenen/İlgili Testler

**Backend** (`tests/Sense.Tests.Architecture/OnionArchitectureTests.cs`, ikisi de docker `dotnet test` ile
doğrulandı — 54/54 yeşil, tam liste):
- `PermissionCatalogs_DoNotExposeUntranslatedEnglishDisplayNames` — reflection ile her modülün `*Permissions.All`
  kataloğunu toplar, DisplayName/Group'ta `View|Manage|Create|Read|Edit|...` gibi yaygın İngilizce eylem
  kelimelerini regresyon olarak yakalar (teknik ödünç kelimeler — Webhook, API, PDKS — kasıtlı olarak listede
  değil).
- `SystemRoleNames_HaveResourceEntriesInBothLocales` — 17 sistem rolünün `roles.{code}.name`/`.description`
  anahtarlarının hem `SharedResource.resx` hem `.en.resx`'te dolu olduğunu doğrular (Profilim/Roller
  tutarlılığının dayandığı mekanizma).

**Frontend** (host Docker `node:24-alpine` ile `pnpm exec vitest run` — yeni testler 4/4 yeşil):
- `web/src/store/auth.store.test.ts` (yeni) — `toUser()`: `roleLabels` backend'in gönderdiği localize edilmiş
  adı sırayla taşıyor, `name: null` durumunda kod'a düşüyor.
- `web/src/pages/users-roles.test.tsx` (yeni) — `RolesTab` component render testi: mock `listRoles`/
  `listPermissionGroups` Türkçe veri döndürünce rol adı ve izin listesi Türkçe render oluyor; ham izin
  anahtarı/İngilizce etiket asla ekrana çıkmıyor; katalogda eşleşme yoksa (boş `permissionGroups`) ham anahtara
  düşüyor (asla boş göstermiyor). Bu testin çalışması için `RolesTab` `export` edildi (önceden modül-private).
- `web/src/i18n.locales.test.ts` (mevcut desen, değişmedi) — tüm namespace'lerde tr/en/de anahtar/interpolasyon
  paritesi ve `[XX]` placeholder taraması; bu kart hiçbir frontend i18n JSON anahtarı eklemedi/değiştirmedi
  (yalnız `account.json`'dan ölü `roleNames` bloğunu sildi, üç dilde de simetrik).

**Doğrulama:**
- `pnpm exec tsc --noEmit` temiz.
- `pnpm exec eslint src` temiz (1 ilgisiz pre-existing uyarı, `employee-list.tsx` — dokunulmadı).
- `pnpm exec vitest run` — yeni/ilgili dosyalar 100% yeşil. Tüm paket: 1 ilgisiz pre-existing hata
  (`de/attendance.json` `teamTimesheet.week/month` çevrilmemiş placeholder — bu kartın dokunmadığı dosya,
  senseik-008 handoff'unda da not düşülmüş).
- `dotnet build Sense.slnx` (Docker SDK 10.0, `-m:1`) — 0 Error, sadece pre-existing NuGet güvenlik
  uyarıları/analyzer uyarıları.
- `dotnet test tests/Sense.Tests.Architecture` — 54/54 yeşil (2 yeni dahil).

## Değişen Dosyalar

**Backend (izin katalogları, sadece DisplayName/Group/Description):**
`src/Modules/{Analytics,Assets,Attendance,DataTransfer,Documents,Employee,Expense,Integrations,Leave,
Notifications,Onboarding,Organization,Payroll,Recruitment,Reporting,Workflow}/**/*Permissions.cs`

**Backend (rol adı tutarlılığı):**
`src/Modules/Identity/Sense.Modules.Identity.Api/Controllers/AuthController.cs`

**Backend (test):**
`tests/Sense.Tests.Architecture/OnionArchitectureTests.cs`

**Frontend:**
`web/src/store/auth.store.ts`, `web/src/pages/account.tsx`, `web/src/pages/users-roles.tsx` (export `RolesTab`),
`web/public/locales/{tr,en,de}/account.json` (`roleNames` bloğu silindi)

**Frontend (yeni test):**
`web/src/store/auth.store.test.ts`, `web/src/pages/users-roles.test.tsx`

## Dokunulmayan / Kapsam Dışı

- Rapor kırılım değerleri ve 17 sistem rolü adı/açıklaması (zaten tam Türkçe, madde 1-2'de açıklandı).
- `Identity`, `Performance`, `Training`, `Announcements` modüllerinin izin katalogları (zaten doğru desendeydi).
- Backend'e `SharedResource.de.resx` eklenmesi (bkz. "Bilinen sınır" — ayrı kart önerilir).
- `de/attendance.json`'daki pre-existing çevrilmemiş placeholder (bu kartın dokunmadığı dosya).

---
*Kaynak: `docs/agent-board/cards/senseik-040-permission-role-i18n.json` (worktree'de mevcut değildi, görev
tanımı doğrudan orkestratör talimatından alındı).*

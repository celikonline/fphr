# senseik-052 — Rota Yetkilendirme Koruması (E-04) — UYGULAMA NOTU

**Durum: GATE 1/2 ön onaylı, uygulandı ve doğrulandı (2026-09-19), branch `a/route-authorization-guard`, review. Push yok.**

## Özet

- **Sol menü filtrelemesi (kabul kriteri 2): zaten mevcuttu, dokunulmadı.** `web/src/hooks/use-nav-visibility.ts`
  (`useVisibleItems`) hem `web/src/layouts/app-layout.tsx`'teki `NavList` (sidebar + footer) hem
  `web/src/pages/settings/settings-home.tsx`'teki Ayarlar hub'ı zaten `NAV_ITEMS`/`NAV_FOOTER_ITEMS`/
  `SETTINGS_SECTIONS`'daki `permissions`/`accessSetting` alanlarına göre filtreliyor - yetkisi olmayan bir
  başlık sidebar'da hiç görünmüyor. Bu, muhtemelen önceki bir paralel kart (workflow inbox/sidebar collapse
  civarı) tarafından zaten eklenmişti. `navigation.ts`'e (hot file) sadece doğrulama turundaki tek değerlik düzeltme (aşağıda #2) dışında dokunulmadı.
- **Gerçek boşluk (kabul kriteri 1): URL çubuğundan doğrudan gidiş.** Menüde görünmeyen bir rotaya adres
  çubuğuna yazarak gidildiğinde, React Router sayfayı olduğu gibi render ediyordu. Çoğu sayfa zaten kendi
  içinde `usePermission(...) + <NoAccess/>` (`web/src/components/no-access.tsx` - "Bu sayfaya erişiminiz
  yok") deseniyle bunu ele alıyordu (audit-log.tsx, employee-list.tsx, work-schedules.tsx ve ~45 sayfa
  daha) ama bu satır tek tek her sayfaya elle eklenmiş ve en az 4 sayfada unutulmuştu:
  - `users-roles.tsx` (kartın kendi örneği) - `identity.users.view`/`identity.roles.view` hiç kontrol
    edilmiyordu, sadece düzenleme aksiyonları `identity.users.manage` ile gizleniyordu.
  - `settings/company/locations` (`locations.tsx`) - hiçbir permission kontrolü yok.
  - `settings/company/profile` (`company-profile.tsx`) - sadece `identity.tenant.manage` (düzenleme)
    kontrol ediliyordu, görüntüleme (`identity.tenant.view`) hiç kontrol edilmiyordu.
  - `employees/org-chart` (`employees/org-chart.tsx`) - sadece `employee.employees.read` (kart açılabilir mi)
    kontrol ediliyordu; sayfanın kendi permission'ı (`employee.orgchart.view`) ve `showOrgChart` access
    setting'i hiç kontrol edilmiyordu.

  API zaten veriyi döndürmüyordu (backend `[RequiresPermission]` bozulmadı) ama bu 4 rotada kullanıcı boş/
  bozuk bir ekran görüyordu - tam olarak E-04'ün tarif ettiği "veri korunuyor ama UX yanıltıcı" durumu.

## Çözüm: `RouteGuard` — mevcut mekanizmanın rota seviyesine yükseltilmesi

Tek tek unutulma riskini ortadan kaldırmak için, sayfa başına elle eklenen deseni (`usePermission` +
`<NoAccess/>`) rota seviyesinde TEK bir yerden, AYNI alt yapıyı kullanarak uyguluyoruz - yeni bir yetki
kontrolü ya da farklı bir "erişim yok" ekranı icat etmiyoruz:

1. **`web/src/config/route-permissions.ts` (yeni).** `NAV_ITEMS`/`NAV_FOOTER_ITEMS`/`APP_LAUNCHER_ITEMS`/
   `SETTINGS_SECTIONS`'ı (`navigation.ts`'ten sadece okunuyor) path -> `Gated`
   ({ permissions?, accessSetting? }) haritasına dönüştürür. Yeni bir permission listesi TANIMLANMIYOR;
   `navigation.ts` değişince harita otomatik güncel kalır (drift riski yok).
2. **`web/src/hooks/use-nav-visibility.ts`**: `Gated` interface'i `export` edildi (tek satırlık değişiklik)
   ki `route-permissions.ts` ve `route-guard.tsx` aynı şekli paylaşsın.
3. **`web/src/components/route-guard.tsx` (yeni).** `useLocation()` ile geçerli path'i alır,
   `getRouteAccess(pathname)`'i `useVisibleItems([gate])` içine besler - MENÜ FİLTRELEMESİYLE BİREBİR AYNI
   fonksiyon. Sonuç `false` ise mevcut `<NoAccess/>` bileşenini render eder ("Bu sayfaya erişiminiz yok" -
   zaten 45+ sayfada kullanılan aynı ekran), `true` ise `<Outlet/>` ile gerçek sayfayı render eder.
4. **`web/src/components/mfa-setup-guard.tsx`**: MFA zorunluluğu yoksa artık doğrudan `<Outlet/>` yerine
   `<RouteGuard/>` render ediyor (tek satır) - böylece rota kontrolü MFA yönlendirmesinden SONRA, sayfa
   render edilmeden ÖNCE devreye giriyor. `app-layout.tsx`'e hiç dokunulmadı (MfaSetupGuard zaten
   `AppLayout`'un `<main>`'inde tek entegrasyon noktasıydı).

Dinamik rotalar (`employees/:id/:tab?`, `payroll/periods/:id` vb.) path haritasında yok - bilinçli kapsam
dışı bırakıldı (YAGNI): bu detay sayfaları zaten kendi sekme bazlı permission kontrollerini yapıyor
(örn. `employee-card.tsx` de `noAccessTitle` kullanıyor) ve liste sayfası zaten route-guard ile korunuyor.

## Test

- `web/src/components/route-guard.test.tsx` (yeni, 4 test): yetkisi olmayan kullanıcı için `<NoAccess/>`
  gösteriyor, yetkili kullanıcı için sayfayı gösteriyor, haritada kaydı olmayan rota her zaman görünür,
  `accessSetting` kapalıyken de engelliyor. `@/store/auth.store` ve `@/config/route-permissions` mock'landı;
  gerçek `tr/common.json` ile `I18nextProvider` kullanıldı (inbox.test.tsx / users-roles.test.tsx'teki
  kurulum deseni).
- `web/src/components/mfa-setup-guard.test.tsx` (güncellendi): `RouteGuard` artık `MfaSetupGuard`'ın
  `Outlet`'i yerine geçtiği için mock'lanan auth store state'ine `user.permissions` eklendi (`/app/employees`
  rotasının gerçek `route-permissions.ts` haritasındaki yetkisini karşılayacak şekilde) - dosyanın kendi
  odağı (MFA yönlendirmesi) değişmedi, sadece composition'ın çalışması için gereken mock genişletildi.
- `web/src/config/route-permissions.test.ts` (yeni, 6 test, gerçek `navigation.ts`'e karşı): users-roles
  kaydı, büyük/küçük harf + sondaki `/` normalizasyonu, org-chart'ın accessSetting'i, açık rotaların
  kayıtsız kalması, her yetkili ayar kartının haritada olması, aynı path'in iki farklı yetkiyle
  tanımlanmaması.
- Manuel test planı (merge_gate): `bordro@demo.local` (PayrollSpecialist - `DataSeeder.DemoEmployees.cs`
  satır 302, `identity.*` yetkisi yok; `SystemRoleDefinitions.cs` ile doğrulandı) ile giriş yapıp
  `/app/settings/system/users-roles`'a gidince "Bu sayfaya erişiminiz yok" görünüyor, sol menüde
  "Kullanıcılar & Roller" başlığı hiç yok.

## Doğrulama turu (2026-09-19)

Branch güncel `dev`'e (`f85e44f`) rebase edildi (conflict yok), kabul kriterlerine göre gözden geçirildi. Bulunan ve
düzeltilen açıklar:

1. **Harita araması URL yazımına duyarlıydı.** React Router eşleşmesi büyük/küçük harfe duyarsız ve sondaki
   `/`'ı yok sayıyor; `getRouteAccess` ise birebir `Map.get` yapıyordu - `/app/settings/system/users-roles/`
   veya `/app/Settings/System/Users-Roles` sayfayı açıp RouteGuard'ı atlıyordu. `normalizePath` (küçük harf +
   sondaki `/`'ları kırp) hem harita anahtarlarına hem aramaya uygulandı.
2. **Harcama kategorileri: menü yetkisi backend'den dardı.** `SETTINGS_PERMISSIONS.expenseCategories` sadece
   `expense.categories.manage` içeriyordu, ama sayfa (`expense-categories.tsx`) ve API
   (`ExpenseCategoryQueries.cs`, `[RequiresPermission("expense.categories.view")]`) görüntüleme için
   `expense.categories.view` kullanıyor. RouteGuard ile bu, sadece görüntüleme yetkisi olan kullanıcıyı
   sayfadan tamamen kilitleyecekti - listeye `expense.categories.view` eklendi (`navigation.ts`, tek değer).
3. **Backend yetki adları doğrulandı:** `navigation.ts`'teki tüm yetki anahtarları (58 adet, `expense.categories.view` dahil) `src/**/*.cs`
   içinde tanımlı; yazım farkı yok.

Kapsam kontrolü: haritada olmayan korumalı rotalar (`employees/:id`, `payroll/periods/:id`,
`recruitment/postings/:id`, `settings/data-transfer/jobs/:id`, `performance/team-goals`)
sayfa içinde kendi `usePermission` kontrolünü yapıyor (grep ile doğrulandı).

Gate sonuçları (host node, `corepack pnpm`):
- `tsc --noEmit`: bu branch'in dosyalarında 0 hata. `dev`'de zaten var olan 4 hata (`protected-route.test.tsx`,
  `home.test.tsx` - `roleLabels`/`mfaSetupRequired` eksik mock alanları) bu karta ait değil.
- `eslint src`: 0 hata, 1 uyarı (`employee-list.tsx`, önceden var).
- `vitest run` (tam suite): 515/517 geçti. 2 hata karttan bağımsız: `i18n.locales.test.ts` (attendance `de`
  çevirisi, önceden var) ve `home.test.tsx` donut testi (tam suite yükünde flaky; tek başına geçiyor, `dev`
  worktree'sinde de aynı).

## Değiştirilen / Eklenen Dosyalar

- `web/src/config/route-permissions.ts` (yeni)
- `web/src/config/route-permissions.test.ts` (yeni)
- `web/src/config/navigation.ts` (`expenseCategories`'e `expense.categories.view` eklendi)
- `web/src/components/route-guard.tsx` (yeni)
- `web/src/components/route-guard.test.tsx` (yeni)
- `web/src/hooks/use-nav-visibility.ts` (`Gated` export edildi, davranış değişmedi)
- `web/src/components/mfa-setup-guard.tsx` (`<Outlet/>` yerine `<RouteGuard/>`)
- `web/src/components/mfa-setup-guard.test.tsx` (mock genişletildi)

## Kapsam Dışı / Sonraki Adım Önerisi

- `locations.tsx`, `company-profile.tsx`, `employees/org-chart.tsx` sayfalarının kendi içindeki eksik
  `usePermission` + `<NoAccess/>` kontrolleri bu kartla DOLAYLI olarak kapatıldı (RouteGuard artık bu 3
  sayfayı da rota seviyesinde koruyor) ama sayfaların kendisi hâlâ o iç kontrolü yapmıyor - defense-in-depth
  için ayrı bir küçük kart önerilir (düşük risk, RouteGuard zaten kapsıyor).
- Dinamik detay rotaları (`employees/:id`, `payroll/periods/:id`, `recruitment/postings/:id`,
  `data-transfer/jobs/:id`) route-permissions haritasında yok - path pattern eşleme eklenmedi (YAGNI,
  bu rotalar zaten kendi iç kontrollerini yapıyor). İleride ihtiyaç olursa `route-permissions.ts`'e
  basit bir prefix/pattern eşleyici eklenebilir.

---
*Kaynak: 1. Tur Test Bulgu Dokümanı § E-04. Agent board: `docs/agent-board/cards/senseik-052-route-authorization-guard.json`.*

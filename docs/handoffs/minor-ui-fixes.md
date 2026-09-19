# senseik-042 — Küçük UI hataları — UYGULAMA PLANI

**Durum: Uygulandı (GATE 1/GATE 2 onaylandı). `a/minor-ui-fixes` dalında commit edildi; push yapılmadı.**

## Özet

Kart `senseik-042`'deki üç bulgu (B-11 halka grafik merkezi, B-12 etiketsiz çift rozet, B-13 ilk-tıklama-tepkisizliği) düzeltildi. B-13 "ORTA ama yaygın etki" olarak işaretlenmişti — kök neden araştırması, aşağıda anlatıldığı gibi, tek ve tekrarlanan bir kaynağa çıktı: `useAuthStore`'un `persist` hidrasyon boşluğu.

## B-11 — Halka grafik merkezi (`web/src/pages/home.tsx`)

`DepartmentDistributionWidget`'ın merkezi, en büyük departmanın adını ve sayısını gösteriyordu (`largest?.name` / `largest?.value`) — kart, merkezde toplam çalışan sayısının görünmesini istiyor. Merkez artık `t("home:departmentDistribution.total")` ("Toplam") etiketi + `total` (tüm departmanların toplamı) gösteriyor; `largest` değişkeni ve onu üreten sıralama varsayımı kaldırıldı. Yeni `home:departmentDistribution.total` anahtarı üç dilde eklendi (`tr`: "Toplam", `en`: "Total", `de`: "Gesamt"). Test edilebilirlik için merkezdeki toplam `<Text>`'e `data-testid="department-distribution-center-total"` eklendi (altındaki footer satırında da aynı sayı ayrı bir amaçla — "kaç kişi" özeti — zaten gösteriliyordu, iki `Text`'in aynı metni taşıması test sorgusunu belirsizleştiriyordu).

## B-12 — Etiketsiz çift "Aktif" rozeti (`web/src/pages/employees/employee-card.tsx`, `web/src/components/employees/employee-badges.tsx`)

`EmployeeStatusBadge` (`employees:status.Active` → "Aktif") ve `AccountStatusBadge` (`employees:accountStatus.active` → "Aktif") aynı metni üretiyor; çalışan kartı başlığında (`CardHeaderBlock`) yan yana, aralarında ayırt edici hiçbir şey olmadan render ediliyorlardı. Diğer kullanım yerlerinde (liste kolonları, org-chart, grid — hepsi `employee-list.tsx`, `org-chart.tsx`, `employee-card-grid.tsx`, `employee-card-sections.tsx`) bu belirsizlik yok: ya kolon başlığı bağlamı veriyor ya da tek rozet gösteriliyor — bu yüzden ortak bileşene global bir metin değişikliği yerine opsiyonel bir `label` prop'u eklendi (`label ? \`${label}: ${text}\` : text`), yalnızca `employee-card.tsx`'teki çift-gösterim çağrısında kullanıldı: `EmployeeStatusBadge status label={t("employees:card.statusLabel")}` ("Çalışan") / `AccountStatusBadge status label={t("employees:card.accountStatusLabel")}` ("Hesap"). Sonuç: "Çalışan: Aktif" / "Hesap: Aktif". Üç dilde `employees:card.statusLabel` / `accountStatusLabel` eklendi.

## B-13 — KRİTİK: "+ Talep", "Yeni çalışan ekle", "Düzenle" ilk tıklamada tepkisizlik

### Kök neden (kesin bulundu, doğrulandı)

`useAuthStore` (`web/src/store/auth.store.ts`), `user`/`features` alanlarını (dolayısıyla **her** `usePermission`/`useAccessSetting`/`hasPermission` kontrolünü) zustand `persist` middleware'i üzerinden localStorage'dan geri yüklüyor. `token` senkron okunuyor (`readStoredToken()`, doğrudan `localStorage.getItem`), ama `user`/`features` yalnızca `persist`'in kendi hidrasyon akışı tamamlandığında mevcut oluyordu — ve bu akış store oluşturulduktan **sonra** tetikleniyordu, önce değil. `ProtectedRoute` bu boşluğu hiç beklemiyordu: store'un ilk (boş) haliyle hemen render ediyordu.

Sonuç: sayfa her yüklendiğinde (giriş sonrası ilk yönlendirme, F5 yenileme, sekme yeniden açma), `usePermission`/`useAccessSetting`'e bağlı **her** aksiyon düğmesi ("+ Talep" kartları, "Yeni çalışan ekle", "Düzenle", "Org Şeması" vb.) hidrasyon tamamlanana kadar DOM'da **hiç yoktu** — sonra aniden beliriyordu. Kullanıcının düğmenin olacağı yere attığı ilk tık boşluğa düşüyor (ya da komşu bir öğeye kayıyor, çünkü kardeş düğmelerin belirmesi yatay hizalamayı kaydırıyor); yalnızca hidrasyon bitip düğme gerçekten oturduktan sonraki tekrar tık "çalışıyor" gibi görünüyor. Aynı örüntü, aynı `usePermission`/`useAccessSetting` çağrısına dayanan HER ekranda tekrarlanıyor — kartın "birden fazla dosyada aynı buton-açma mantığı kopyalanmış olabilir" varsayımı isabetliydi, ama kopyalanan şey bir buton-açma fonksiyonu değil, ortak `useAuthStore` okuma deseniydi.

### Düzeltme

1. `web/src/store/auth.store.ts`: yeni `hasHydrated: boolean` alanı (başlangıçta `false`), `persist` seçeneklerine `onRehydrateStorage` eklendi — hidrasyon bitince (başarılı/başarısız/hiç veri yok fark etmez) `hasHydrated: true` yapılıyor.
   - **Dikkat edilmesi gereken ikinci bir hata (bu oturumda bulundu ve düzeltildi):** `onRehydrateStorage`'ın döndürdüğü callback, bu zustand sürümünde (5.0.15) senkron storage için **senkron** çalışıyor — yani `export const useAuthStore = create(...)` ataması bitmeden, `create(...)` çağrısının içinde. Callback içinde doğrudan `useAuthStore.setState(...)` çağırmak `ReferenceError: Cannot access 'useAuthStore' before initialization` fırlatıyor; bu hata zustand'ın kendi senkron "thenable" sarmalayıcısı tarafından sessizce yutuluyor (gerçek bir `Promise` değil, `unhandledrejection` de görünmüyor) — yani `hasHydrated` **hiçbir zaman** `true` olmuyordu ve ProtectedRoute sonsuza dek yükleme ekranında kalıyordu. Bunu ancak `auth.store.test.ts`'i gerçekten çalıştırarak yakaladım (bkz. testler). Düzeltme: modül seviyesinde `let setAuthState` değişkeni, store creator'ının `(set, get) => {...}` gövdesinde `setAuthState = set` ile dolduruluyor (creator her zaman hidrasyondan önce çalışıyor), `onRehydrateStorage` da `useAuthStore` yerine `setAuthState` kullanıyor.
2. `web/src/components/protected-route.tsx`: `hasHydrated` `false` iken ne korumalı içerik ne de `/login` yönlendirmesi render ediliyor — yalnızca nötr bir `Loader` (`data-testid="protected-route-loading"`). `hasHydrated` `true` olduğunda mevcut `isAuthenticated()` mantığı aynen çalışıyor. Sonuç: kabuk ya tamamen izinsiz (login'e yönlendirme) ya da tamamen izinli+dolu render ediliyor — düğmelerin "yarı dolu" bir anda belirip gözden kaybolduğu ara durum ortadan kalkıyor.

### Neden bu düzeltme B-13'ü tam olarak kapatıyor

- Etkilenen üç örnek de (`+ Talep` → `requests.tsx` kart görünürlüğü `usePermission`'a bağlı; `Yeni çalışan ekle` → `employee-list.tsx` `canCreate = usePermission(...)`; `Düzenle` → `employee-card-sections.tsx` `EditButton` `canUpdate && !editing` ile gated) hep aynı kaynaktan (`useAuthStore`) okuyor; tek noktadan (ProtectedRoute) hidrasyonu beklemek hepsini aynı anda düzeltiyor — her ekranda ayrı ayrı "isLoading" state'i eklemeye gerek kalmıyor.
- `refreshMe()` (App.tsx, ağ çağrısı) hâlâ arka planda çalışıyor ve daha güncel izinleri getirebilir — bu, hidrasyon boşluğundan **farklı** bir mekanizma (stale-while-revalidate), kapsamımız dışında, kart bunu talep etmiyordu.

## Testler

Host'ta node/pnpm yok (`project-windows-toolchain.md`) — tüm testler `node:24-alpine` container'ında (`corepack enable` + isimli volume'lar) çalıştırıldı.

- `web/src/pages/home.test.tsx` — `DepartmentDistributionWidget`: merkez etiketinin toplamı gösterdiğini, departman adının **hiç** görünmediğini doğruluyor.
- `web/src/components/employees/employee-badges.test.tsx` — etiketsiz haliyle iki rozetin aynı "Aktif" metnini ürettiğini (regresyon belgesi), `label` prop'uyla ayrıştığını doğruluyor.
- `web/src/store/auth.store.test.ts` — `hasHydrated`'ın kendiliğinden `true` olduğunu doğruluyor (bu test, yukarıdaki `ReferenceError` hatasını canlı olarak yakaladı — ilk yazımda kırmızıydı).
- `web/src/components/protected-route.test.tsx` — üç senaryo: (1) hidrasyon bitmeden ne düğme ne login redirect, sadece loading; (2) hidrasyon bitip oturum yoksa `/login`; (3) hidrasyon bitip oturum varsa gated düğme **ilk render'da zaten mevcut** ve `userEvent` ile art arda gönderilen iki hızlı tıklamanın **ikisi de** kaydediliyor (`onClick` 2 kez çağrılıyor) — çift-tıklama yarışının artık var olmadığının kanıtı.
- Yeni `web/src/test-setup.ts` + `vitest.config.ts`'e `setupFiles` eklendi (`@testing-library/jest-dom/vitest`) — daha önce hiç bağlı değildi, `toBeInTheDocument`/`toHaveTextContent` gibi matcher'lar mevcut değildi.

Tam paket: `pnpm exec vitest run` → **389/390 geçti**. Tek başarısız test (`src/i18n.locales.test.ts` › `attendance.json` › `de carries no untranslated placeholder markers`) bu oturumdan **önce** var olan, dokunulmayan bir dosyayla (`de/attendance.json`) ilgili, önceden var olduğu doğrulandı (git diff'te bu dosya yok).

`pnpm exec tsc --noEmit` → temiz (1882 dosya, 0 hata). `pnpm exec eslint src` → 0 hata, 1 önceden var olan uyarı (`employee-list.tsx`, dokunulmadı).

## Değişen dosyalar

- `web/src/pages/home.tsx` — merkez etiket total'a çevrildi
- `web/src/pages/home.test.tsx` (yeni)
- `web/src/pages/employees/employee-card.tsx` — rozet `label` prop'ları
- `web/src/components/employees/employee-badges.tsx` — `label` prop eklendi
- `web/src/components/employees/employee-badges.test.tsx` (yeni)
- `web/src/store/auth.store.ts` — `hasHydrated` + `onRehydrateStorage` + `setAuthState` closure fix
- `web/src/store/auth.store.test.ts` (yeni)
- `web/src/components/protected-route.tsx` — hidrasyon gate'i
- `web/src/components/protected-route.test.tsx` (yeni)
- `web/src/test-setup.ts` (yeni), `web/vitest.config.ts` (setupFiles bağlandı)
- `web/public/locales/{tr,en,de}/home.json`, `employees.json` — yeni çeviri anahtarları

## Bilinmeyenler / sonraki adım için notlar

- Gerçek tarayıcıda manuel doğrulama yapılmadı (bu ortamda tarayıcı/E2E aracı yok) — merge gate'in "manuel test: 5+ farklı düğme ilk tıklamada tutarlı çalışıyor" kısmı bir sonraki insan incelemesinde teyit edilmeli.
- `refreshMe()`'in kendisi hâlâ async ve `hasHydrated`'dan bağımsız — izinler admin tarafından değiştirildiyse, ilk render'daki (persist'ten gelen) izinler kısa süre eski kalabilir; bu, B-13'ün kapsamı dışında ayrı, çok daha nadir bir senaryo.

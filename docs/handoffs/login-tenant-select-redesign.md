# senseik-025 — Login şirket seçim popup'ı yeniden tasarımı

## Implementasyon Notu

**Durum: GATE 1 ve GATE 2 onaylandı, uygulandı, branch `a/login-tenant-select-redesign`.**

- `web/src/pages/auth/login.tsx` — tenant seçim `Modal`'ının içeriği yenilendi: `UnstyledButton` + `ThemeIcon`
  (sabit `Building2` ikonu) satırı yerine, her tenant için gerçek bir Mantine `Card` (`component="button"`,
  `type="button"`, tıklanabilir) + `Avatar` (`name={tenant.name}` `color="initials"` — tenant adından otomatik
  baş harf + tutarlı renk üretiyor, ayrı bir logo alanı `TenantChoice` tipinde yok). İsim ve slug `truncate`
  ile tek satırda tutuluyor (dar ekranda taşma yok).
- Modal'a `closeButtonProps={{ "aria-label": t("common:close") }}` eklendi — önceden X butonunun erişilebilir
  adı yoktu (boş `aria-label`, sadece SVG ikon); artık ekran okuyucu ve test sorguları için "Kapat" etiketi var.
  Bu amaçla `LoginPage`'in `useTranslation` çağrısına `"common"` namespace'i eklendi (`auth:*` yanında).
- `Modal` `size="sm"` ile biraz daraltıldı, `radius` artık tema varsayılanından geliyor (`Modal.extend` zaten
  `radius: "lg"` set ediyordu — tekrar override etmedim).
- **Kapatma/kilitlenme davranışı zaten doğruydu, kod değişikliği gerekmedi, sadece doğrulandı:** `onClose={() =>
  setTenantChoices(null)}` sadece `tenantChoices` state'ini temizliyor; `handleSubmit`'teki `try/finally`
  select_tenant dalında `return` etse bile `finally`'deki `setIsLoading(false)` her zaman çalışıyor, yani modal
  açıldığı anda form zaten kilitli değil. E-posta/şifre alanları `disabled={isLoading}`'e bağlı, o da false
  olduğu için X/ESC/backdrop ile kapatınca form anında etkileşimli. Form state (email/password) kapatma sırasında
  hiç dokunulmuyor — korunuyor (bilerek, temizlemedim; kart bunun "makul" olduğunu, sıfırlamanın da makul
  olduğunu söylüyor — kilitlenmeme önceliğiydi, o sağlandı).
- `web/src/pages/auth/login.module.css` — `.tenantOption` kaldırıldı, yerine `.tenantCard`: `Card`'ı native
  `<button>` olarak render ettiğimiz için tarayıcı buton reset'leri (`appearance: none`, `font: inherit`, `margin:
  0` vb.) eklendi, hover'da hafif `translate` + `box-shadow` (markaya tutarlı, `.submit` butonuyla aynı dil),
  `:focus-visible` outline, `:disabled` state.
- Mobil: ek bir responsive değişiklik gerekmedi — Mantine `Modal` zaten viewport'a göre daralıyor, kart satırları
  `Group`/`Stack` ile flex, `truncate` sayesinde uzun şirket adları/slug'lar taşmıyor.

**Testler (hepsi yeşil, Docker `node:24-alpine` üzerinden doğrulandı — host'ta node/pnpm bu oturumda PATH'te
değildi):**
- `web/src/pages/auth/login.test.tsx` (yeni, 3 test):
  1. `login()` `select_tenant` döndürünce her tenant için kart/avatar satırı açılıyor (isim + slug görünür,
     buton erişilebilir adı isim içeriyor) ve X butonuyla (artık `t("common:close")` etiketli) kapanıyor.
  2. ESC ile kapatma + kapattıktan sonra form kilitlenmiyor doğrulaması: email/şifre/submit `disabled` değil,
     kapatmadan önce yazılan e-posta korunmuş, farklı bir e-posta ile tekrar submit edilebiliyor (`login` 2.
     kez farklı argümanla çağrılıyor, `navigate("/app")` tetikleniyor).
  3. Kart listesinden bir tenant seçmek `login(email, password, tenant.slug)` ile ikinci çağrıyı tetikliyor,
     başarılı olunca modal kapanıyor ve `/app`'e yönlendiriyor.
- Doğrulama (Docker `node:24-alpine`, `corepack enable` + `pnpm install --filter web...`):
  `pnpm exec tsc --noEmit` temiz, `pnpm exec eslint src` (tüm `web/src`, sadece bu değişikliğe özgü değil) temiz,
  `pnpm exec vitest run src/pages/auth/login.test.tsx` 3/3 yeşil.
- Not: proje genelinde `@testing-library/jest-dom` matcher'ları (`toBeInTheDocument`, `toBeEnabled` vb.) hiçbir
  yerde register edilmemiş (`vitest.config.ts`'te `setupFiles: []`) — mevcut tek test dosyası
  (`leave-type-dialog.test.tsx`) da bunlardan kaçınıyor. Aynı konvansiyona uyup düz `expect(...).toBeNull()` /
  `.disabled` property kontrolleriyle yazdım; jest-dom'u global setup'a bağlamak bu kartın kapsamı dışında.

## Değişen Dosyalar
- `web/src/pages/auth/login.tsx`
- `web/src/pages/auth/login.module.css`
- `web/src/pages/auth/login.test.tsx` (yeni)

## Kritik Dosyalar (referans)
- `web/src/store/auth.store.ts` — `TenantChoice` tipi (`tenantId`, `slug`, `name`; logo alanı yok)
- `public/locales/tr/auth.json` — `selectCompany.title/subtitle`
- `public/locales/tr/common.json` — `close` (Modal X butonu için yeni kullanılan anahtar)
- `web/src/lib/mantine-theme.ts:260` — `Modal.extend({ defaultProps: { radius: "lg" }, ... })`

---
*Kaynak: Kullanıcı isteği (2026-09-18). Agent board: `docs/agent-board/cards/senseik-025-company-select-popup.json`.*

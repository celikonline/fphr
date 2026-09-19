# senseik-023 — Üst header bar navigasyon sonrası kayboluyor — ARAŞTIRMA SONUCU

**Durum: kod incelemesiyle kök neden DOĞRULANAMADI. Kod değişikliği yapılmadı, branch
`a/header-bar-visibility-fix` boş (senseik-020'nin üstünde, ek commit yok). Bu dosya branch'te
commit'lenmedi (talimat gereği "hiçbir dosyayı değiştirmeden commit atma") — worktree'de duruyor,
elle taşınabilir/commit'lenebilir.**

## Yapılan inceleme

1. **`web/src/App.tsx` routing tablosu (satır 104-194):** `/app` altındaki **tüm** route'lar
   (index/dashboard dahil, `employees`, `leaves`, `settings/*`, `payroll/*`, `attendance/*`, ... —
   toplam ~50 route) tek bir `<Route path="/app" element={<ProtectedRoute><AppLayout /></ProtectedRoute>}>`
   altında `Outlet` ile nest'leniyor. Header ve sidebar `AppLayout` içinde, `Outlet`'in DIŞINDA,
   üst seviyede render ediliyor — hiçbir alt route header'ı bypass edemez. Sadece login/invite/
   forgot-password/reset-password/approve/kariyer public route'ları `AppLayout` dışında (beklenen).
2. **`web/src/layouts/app-layout.tsx` (satır 240-276, `AppLayout`):** Header (`<header>`, Brand +
   Greeting + AppsLauncher) hiçbir route/state koşuluna bağlı değil — route'a göre koşullu render
   yok, `display:none` yapan bir sınıf yok. `RouteBoundary` (Suspense + error boundary) `<main>`
   içinde, header'ın ve sidebar'ın ALTINDA/yanında, dışında değil — `Suspense` fallback'i veya hata
   ekranı sadece `<main>` alanını değiştirir, header'a dokunmaz.
3. **`web/src/layouts/app-layout.module.css`:** Header/sidebar'ı koşullu gizleyen hiçbir kural yok
   (`.header`, `.sidebar`, `.footerNav` sabit border/background kuralları; tek koşullu kural
   `.navLink[data-collapsed]` — nav öğesi etiketini gizliyor, header'la ilgisi yok).
4. **`position:fixed` / `z-index` / `fullScreen` / `display:none` taraması** (`web/src/**`):
   13 dosyada eşleşme, hepsi incelendi:
   - `unit-authorization-dialog.tsx`, `leave-type-dialog.tsx`, `leave-request-dialog.tsx`,
     `payments-section.tsx`, `bulk-result-dialog.tsx`, `assets-section.tsx`,
     `recruitment-form-templates.tsx`, `recruitment-automation-rules.tsx`, `payments.tsx`,
     `org-chart.tsx`: hepsi Mantine `<Modal fullScreen={isMobile} ...>` — sadece **mobilde** ve
     sadece **kullanıcı açtığında** tam ekran oluyor (bir sayfaya girer girmez otomatik açılan yok).
     Masaüstünde `fullScreen` hiç devreye girmiyor.
   - `org-chart.module.css`: eşleşme yok (grep false-positive, dosya boş kaldı).
   - `login.module.css`: `z-index:-1` — sadece login sayfasının dekoratif arka plan şekilleri,
     `AppLayout` kapsamı dışında.
   - `LoadingOverlay` / `<Overlay` / portal-target kullanımı: repo genelinde **hiç yok**.
5. **`RouteBoundary` (`web/src/components/route-boundary.tsx`):** İki kopyası var — biri
   `App.tsx`'te tüm `<Routes>`'u saran dış boundary (public sayfalar için), biri `AppLayout`
   içinde `<main>`'i saran iç boundary. İkisi de sadece kendi sardıkları alanı etkiliyor; iç
   boundary header/sidebar'ı hiç kapsamıyor (kod: `app-layout.tsx:265-270`, header/sidebar
   `RouteBoundary`'nin dışında render ediliyor).

## Sonuç

**Kullanıcının canlı gözlemi ("menüden bir yere tıklayınca header kayboluyor, sadece dashboard'da
var") ile kod okuması arasındaki çelişki kod seviyesinde çözülemedi.** Routing tablosu ve layout
kodu, dashboard dahil TÜM `/app/*` sayfalarının aynı `AppLayout`'u (aynı header+sidebar) kullandığını
gösteriyor; bunu route bazlı bypass eden veya CSS/z-index ile görsel olarak kapatan hiçbir kod yolu
bulunamadı.

**Olası açıklamalar (doğrulanmadı, sadece hipotez):**
- **Bayat derlenmiş imaj**: `docs/implementation` notlarına göre bu ortam `docker compose` ile
  statik build edilmiş imajlar üzerinden çalışıyor (`senseik-web` nginx, hot-reload yok) — kullanıcı
  header'ın zaten AppLayout'a taşındığı bir düzeltmeden ÖNCEKİ bir build'i görüyor olabilir.
  `docker compose build senseik-web && docker compose up -d senseik-web` ile güncel build'in
  ayakta olduğu teyit edilmeli.
- **Tarayıcı cache'i / service worker** eski bir JS chunk'ı sunuyor olabilir.
- **Belirli bir chunk yüklenirken** (`RouteBoundary`'nin `RouteFallback`'i) header görünür kalıyor
  olmalı ama kullanıcı bunu "kayboldu" olarak algılamış olabilir (fallback ortada bir `Loader`
  gösteriyor, header'ı gizlemiyor) — düşük olasılık ama ekran görüntüsüyle doğrulanabilir.

## Önerilen sonraki adım

Kod değişikliği yapılmadı — **kullanıcıdan tam tekrar üretme adımları istenmeli**:
1. Hangi menü öğesine tıklandığında oluyor (spesifik path)?
2. Hangi tarayıcı/cihaz (masaüstü mü mobil mi — mobilde `w-14` icon rail zaten farklı görünüyor,
   "header yok" algısı sidebar'la karışmış olabilir mi)?
3. Ekran görüntüsü veya ekran kaydı mümkünse.
4. Sorunu görüntülerken bakılan ortam güncel `docker compose` build'i mi (yoksa lokal `vite dev`
   sunucusu mu)?

Bu bilgiler olmadan kod tarafında güvenle daha fazla değişiklik yapmak riskli (mevcut, doğru
çalışan routing/layout yapısını bozma riski).

## İncelenen dosyalar
- `web/src/App.tsx`
- `web/src/layouts/app-layout.tsx`
- `web/src/layouts/app-layout.module.css`
- `web/src/components/route-boundary.tsx`
- `web/src/components/structure/unit-authorization-dialog.tsx`
- `web/src/components/leave/leave-type-dialog.tsx`
- `web/src/components/leave/leave-request-dialog.tsx`
- `web/src/pages/settings/recruitment-form-templates.tsx`
- `web/src/pages/settings/recruitment-automation-rules.tsx`
- `web/src/components/employees/payments-section.tsx`
- `web/src/components/employees/bulk-result-dialog.tsx`
- `web/src/components/employees/assets-section.tsx`
- `web/src/pages/payments.tsx`
- `web/src/pages/employees/org-chart.tsx`
- `web/src/pages/auth/login.module.css`

---
*Kaynak: Kullanıcı bildirimi (2026-09-18). Agent board: `docs/agent-board/cards/senseik-023-header-bar-bug.json`.*

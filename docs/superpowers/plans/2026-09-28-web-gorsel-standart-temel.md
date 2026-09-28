# Plan — Web görsel standart, alt proje 1 (tema + kabuk)

Spec: [../specs/2026-09-28-web-gorsel-standart-temel-design.md](../specs/2026-09-28-web-gorsel-standart-temel-design.md)
Yürütme: kod `takim-gelistirici-node`, inceleme `takim-gozden-gecirici`, görsel doğrulama ana oturum.

| # | Adım | Doğrulama |
|---|---|---|
| 1 | Fontlar: Inter paketini çıkar, Bricolage Grotesque + Onest ekle (`pnpm add` web'de), `main.tsx` importları | `pnpm exec vite build` fontları paketliyor; `grep -r Inter web/src` boş |
| 2 | Tema: `fontFamily`/`headings` (ağırlık + harf aralığı), `respectReducedMotion` | `tsc`, mevcut testler yeşil |
| 3 | Token'lar: `index.css`'e `--sense-rail-*` (açık/koyu), sayfa atmosferi, tablo rakamları tabular | Tarayıcıda hesaplanan değerler |
| 4 | Kabuk: `.rail` sınıfı (marka + masaüstü + mobil ray), `.sidebar` ayarlar alt menüsüne kalır; rayda ikon rengi Buz, seçili vurgu | `app-layout.test.tsx`, `sidebar-nav.test.tsx`, `settings-subnav.test.tsx` yeşil |
| 5 | İmza: `.rail .navLink[aria-current="page"]` özlük sekmesi (içerik renginde zemin + ters köşe kıvrımları), daraltılmış rayda da | Ekran görüntüsü: genişletilmiş/daraltılmış, açık/koyu |
| 6 | Kalite tabanı: rayda görünür odak halkası; geçişler `prefers-reduced-motion` altında kapalı; 375px taşma yok | Klavye Tab turu; `scrollWidth` kontrolü |
| 7 | Kapı: `node .claude/takim/betikler/test-kapisi.mjs`, eslint, vite build; gözden geçirici | Kapı yeşil + inceleme raporu |

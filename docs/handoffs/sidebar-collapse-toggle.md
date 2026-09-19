# senseik-020 — Sol menü küçültme/büyütme (collapse/expand)

**Durum: uygulandı, branch `a/sidebar-collapse-toggle`.**

## Ne yapıldı

- `web/src/layouts/app-layout.tsx`: `AppLayout`'a `useLocalStorage<boolean>({ key: "senseik.sidebar.collapsed", defaultValue: false })`
  (`@mantine/hooks`) eklendi — masaüstü sidebar'ın daraltılmış/genişletilmiş tercihi artık kullanıcı tetikli ve
  kalıcı (per-viewer localStorage, kritik/paylaşılan state değil).
- Yeni `SidebarToggle` bileşeni (aynı dosyada, `AppsLauncher`'ın hemen üstünde): `PanelLeftClose`/`PanelLeftOpen`
  (lucide-react) ikonlu, tooltip'li `ActionIcon`. Sadece masaüstü sidebar'ının altında render ediliyor
  (`hidden md:flex` — mobil rail'de hiç yok).
- Masaüstü `<aside>` genişliği artık `collapsed` durumuna göre `w-52` / `w-14` arasında değişiyor ve
  `NavList collapsed={collapsed}` prop'unu alıyor (önceden sabit `collapsed={false}`'du).
- Mobil rail `<aside>` **dokunulmadı** — hâlâ sabit `w-14` + `collapsed` her zaman `true`; masaüstü tercihinden
  bağımsız.
- Daraltılmış haldeki menü öğeleri zaten mevcut `NavItemLink` tooltip mekanizmasıyla erişilebilir kalıyor
  (kod değişmedi, `collapsed` prop'u artık kullanıcı kontrollü).
- Her iki `<aside>`'a test için `data-testid="sidebar-desktop"` / `data-testid="sidebar-mobile"` eklendi (iki
  taraf da aynı nav öğelerini render ettiği için RTL sorguları scope'lanmadan çakışıyordu).
- i18n: `nav.collapseSidebar` / `nav.expandSidebar` anahtarları `tr/en/de navigation.json`'a eklendi (tooltip +
  aria-label metni).
- `web/src/config/navigation.ts`'e dokunulmadı (kart talimatına göre gerek yoktu).

## Testler

`web/src/layouts/app-layout.test.tsx` (yeni, 5 test, RTL + vitest):
1. Varsayılan olarak genişletilmiş, `w-52`, nav etiketleri görünür.
2. Toggle'a tıklanınca `w-14`'e daralıyor, etiketler kayboluyor, `aria-label`/tooltip ile erişilebilir kalıyor.
3. İkinci tıklamada tekrar genişliyor.
4. **Kalıcılık regresyonu**: daraltıp unmount + yeniden mount edince (sayfa yenileme simülasyonu) tercih
   localStorage'dan geri geliyor (`w-14` ile başlıyor).
5. **Mobil rail regresyonu**: masaüstü toggle'ı kaç kez tıklanırsa tıklansın mobil `<aside>` her zaman `w-14`,
   etiketsiz.

Doğrulama (host Docker üzerinden, `CLAUDE.md` toolchain notuna göre): `pnpm exec tsc --noEmit` temiz,
`pnpm exec eslint src` temiz (1 ilgisiz pre-existing uyarı, `employee-list.tsx`, bu kartın dokunmadığı dosya),
`pnpm exec vitest run` → yeni 5 test yeşil, toplam 387/388 yeşil (1 ilgisiz pre-existing hata:
`de/attendance.json` `teamTimesheet.week/month` çevrilmemiş placeholder — bu kartın dokunmadığı dosya, base
branch'te de mevcut).

## Kritik dosyalar
- `web/src/layouts/app-layout.tsx`
- `web/src/layouts/app-layout.test.tsx` (yeni)
- `web/public/locales/{tr,en,de}/navigation.json`

---
*Kaynak: Kullanıcı isteği (2026-09-18). Agent board: `docs/agent-board/cards/senseik-020-menu-collapse.json`.*

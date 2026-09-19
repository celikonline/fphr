# senseik-024 — Takvim: Outlook tarzı gün/hafta/ay hücreli görünüm

**Durum: GATE 1 ve GATE 2 onaylandı, uygulandı, branch `b/leave-calendar-outlook-view` (dev'den).**

## Yapılan

`web/src/pages/leave/leave-calendar.tsx` mevcut custom CSS Grid ay görünümü korunarak Outlook tarzı
gün/hafta/ay geçişine genişletildi. Yeni kütüphane eklenmedi (YAGNI) — mevcut grid + "lane" çakışma
algoritması aynen genişletildi.

- **View switcher**: Mantine `SegmentedControl` (Gün/Hafta/Ay), üst navigasyon satırının sağında.
- **State**: eski `month` state'i `anchor: Date` + `view: CalendarView` ikilisine dönüştürüldü. `dates`
  useMemo'su view'a göre dallanıyor — ay: `startOfMonth` (değişmedi), hafta: yeni `startOfWeek` (Pazartesi
  başlangıç, `attendance/shift-plan.tsx`'teki `mondayOf` ile aynı yerel-saat kuralı), gün: tek elemanlı dizi.
- **Navigasyon** (`shift`, önceki/sonraki/bugün butonları, üst başlık): view'a göre 1 ay / 7 gün / 1 gün
  kaydırıyor; "Bugün" `anchor`'ı `new Date()`'e resetliyor.
- **Ay görünümü**: dokunulmadı — aynı grid, aynı lane algoritması, aynı hücre boyutu.
- **Hafta görünümü**: aynı desktop grid bileşeni yeniden kullanılıyor, sadece gün kolonları daha geniş
  (`6.5rem` min, ay görünümünde `1.75rem`) ve bar içeriğine gün sayısı ekleniyor (`typeLabel · N gün`) —
  daha fazla alan olduğu için daha fazla detay gösteriliyor (görev maddesi 3).
- **Gün görünümü**: grid tamamen devre dışı; mevcut "dar ekran" gün-listesi bileşeni (`dayGroup`, artık
  ortak bir fonksiyona çıkarıldı) tam genişlikte, her zaman görünür ana içerik oluyor — o günün her izin
  kaydı satır satır listeleniyor (görev maddesi 4). Aynı `dayGroup` fonksiyonu ay/hafta görünümlerinde dar
  ekran fallback'i olarak da kullanılıyor (kod tekrarı kaldırıldı).
- **Lane algoritması**: değişmedi — zaten `dates.length`/`from`'a göre genel çalışıyordu, hafta görünümünde
  otomatik doğru sonuç veriyor. Gün görünümü lane kullanmıyor (tek gün için gerek yok, düz liste yeterli) —
  bu bilinçli bir tasarım kararı, kabul kriterindeki "uygun şekilde uyarlanıyor" ifadesiyle uyumlu.
- **Performans / API parametrizasyonu**: `getLeaveCalendar` servis fonksiyonu değişmedi — zaten
  `{from, to, orgUnitId}` alıyordu. Görev maddesi 6, `dates` dizisinin view'a göre daralmasıyla otomatik
  sağlandı: ay ≤31 gün (mevcut davranış), hafta 7 gün, gün 1 gün — hafta/gün görünümü ay görünümünden daha
  az veri çekiyor, ayrı bir "visible range" mekanizması eklemeye gerek kalmadı.
- **i18n**: `calendar.view.{day,week,month}` ve `calendar.previous/next{Day,Week}` anahtarları tr/en/de
  `leave.json`'a eklendi (mevcut `calendar.previous/next` ay görünümü için korundu).

## Testler (`web/src/pages/leave/leave-calendar.test.tsx`, yeni — 4 test, hepsi yeşil)

1. Ay → Hafta → Gün geçişi: her adımda `getLeaveCalendar`'a giden `from`/`to` aralığının daraldığını (ay tam
   ay, hafta 7 gün, gün tek gün) ve verinin her view'da render edildiğini doğruluyor.
2. Gün görünümünün, aynı gün için birden fazla izin kaydı olan bir çalışanınkiler dahil, her kaydı ayrı satır
   olarak listelediğini doğruluyor.
3. Lane algoritmasının ay görünümünde (regresyon — mevcut davranış) aynı çalışanın çakışan iki kaydını farklı
   grid satırlarına (lane) koyduğunu doğruluyor.
4. Aynı testi hafta görünümünde tekrarlıyor — lane algoritmasının hafta görünümüne de doğru uyarlandığını
   kanıtlıyor.

Not: jsdom, Tailwind'in `hidden md:block` / `md:hidden` medya sorgusu class'larını değerlendirmediği için
grid ve dar-ekran listesi aynı anda DOM'da oluyor — testler `.bar` CSS-module class'ı üzerinden grid'e özgü
elemanlara scope ediliyor (`gridBars` helper'ı), belirsiz `getByText` eşleşmelerini önlemek için.

## Doğrulama

- `pnpm exec tsc --noEmit`: temiz.
- `pnpm exec eslint src`: 0 hata (1 ilgisiz pre-existing uyarı, `employee-list.tsx` — bu kartın dokunmadığı
  dosya).
- `pnpm exec vitest run`: 390/391 yeşil. 1 ilgisiz pre-existing hata: `i18n.locales.test.ts` →
  `de/attendance.json` `teamTimesheet.week/month` çevrilmemiş placeholder — bu kartın dokunmadığı dosya,
  `git diff` ile doğrulandı (base branch'te de mevcut).

## Kritik Dosyalar

- `web/src/pages/leave/leave-calendar.tsx`
- `web/src/pages/leave/leave-calendar.test.tsx` (yeni)
- `web/public/locales/{tr,en,de}/leave.json`

---
*Kaynak: Kullanıcı isteği (2026-09-18). Agent board: `docs/agent-board/cards/senseik-024-calendar-outlook-view.json`.*

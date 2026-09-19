# senseik-038 — İzin Türü Düzenleme Penceresi Bozuk Açılıyor (BLOKAJ) — UYGULAMA NOTU

## Durum

GATE 1 ve GATE 2 önceden onaylandı, uygulandı. Branch: `b/leave-type-edit-modal-fix` (senseik-039 ile birlikte,
aynı Leave UI alanına dokunduğu için tek branch/agent olarak yürütüldü).

## Kök Neden

`web/src/components/leave/leave-type-dialog.tsx` — modal içindeki her `Fieldset` (Mantine `<fieldset>` render
eder) doğrudan altında bir `<div className="grid gap-4 sm:grid-cols-2">` (veya `sm:grid-cols-3`) barındırıyor.
`fieldset` elemanı tarayıcılarda varsayılan olarak **min-content** genişlikte boyutlanır (bu, CSS'in eski/tuhaf
bir tarayıcı davranışıdır — normal `div`lerde görülmez). Bu min-content sınırlaması, doğrudan altındaki CSS
Grid'in `grid-template-columns` değerinde üretilen `minmax(0, 1fr)` kolonlarıyla çakışıyor: grid, fieldset'in
empoze ettiği daraltılmış genişliğe göre yeniden hesaplanıyor ve pratikte **0px genişliğe çöküyor**. Sonuç:
"Düzenle" tıklanınca form, tek karakter genişliğinde dikey bir şeride dönüşüyordu — hiçbir izin türü
düzenlenemiyordu (bu yüzden BLOKAJ/kritik önceliğe alınmıştı).

Kök neden, kart açılmadan önce zaten DOM üzerinden tespit edilip doğrulanmıştı (bkz. orijinal handoff talebi);
bu oturumda önce `leave-type-dialog.tsx`'in GÜNCEL hâli okunarak doğrulandı: senseik-021/senseik-041'in eklediği
`FormModal` sarmalayıcısı (`web/src/components/ui/form-modal.tsx`) zaten uygulanmış durumdaydı — bu sarmalayıcı
yalnızca modal'ı sola hizalıyor (`styles={{ inner: { justifyContent: "flex-start" } }}`), `Fieldset`/grid
genişlik sorununa hiç dokunmuyor. Yani kök neden hâlâ aynen geçerliydi.

## Düzeltme

Dosyadaki **6 grid'in tamamına** `w-full` (width: 100%) eklendi — 2'si (Genel bölümü: kod/ad; Kurallar bölümü:
min/maks gün + birim) doğrudan `Fieldset`'in altında olduğu için doğrudan etkileniyordu, diğer 4'ü (Limit
bölümündeki negatif bakiye/tahakkuk/devir grid'leri ve Kurallar bölümündeki checkbox grid'i) `Fieldset` içindeki
bir `Stack`'in altında olsa da aynı riske karşı tutarlılık ve gelecekteki bir yeniden düzenlemeye karşı
dayanıklılık için aynı şekilde düzeltildi (kabul kriterinin istediği gibi).

Satır bazında (mevcut haliyle, `w-full` eklendikten sonra):
- L160 — Genel bölümü, kod + ad (`sm:grid-cols-2`)
- L261 — Limit bölümü, negatif bakiye + limit günü (`sm:grid-cols-2`)
- L285 — Limit bölümü, tahakkuk sıklığı/zamanlaması/bazı (`sm:grid-cols-3`)
- L315 — Limit bölümü, devir modu + devir maks günü (`sm:grid-cols-2`)
- L351 — Kurallar bölümü, min/maks ardışık gün + birim (`sm:grid-cols-3`)
- L395 — Kurallar bölümü, checkbox'lar (`sm:grid-cols-2`)

`web/src/components/leave/leave-request-dialog.tsx` da aynı `FormModal`/`Fieldset` ailesini kullanıyor ama içindeki
grid'ler doğrudan bir `Fieldset` altında DEĞİL (düz `Stack`/`div` altında) — bu dosyada aynı çökme riski yok,
dokunulmadı.

## Test

- **Otomatik (yeni)**: `leave-type-dialog.test.tsx` içine `LeaveTypeDialog (senseik-038 fieldset/grid width
  regression)` describe bloğu eklendi. jsdom gerçek layout hesaplamadığından (bkz. proje test kuralları,
  "Playwright/Cypress" gerektiren durumlar) test genişliği piksel olarak ölçmüyor; bunun yerine `document.body`
  üzerinden (Mantine `Modal` içeriği bir portal'a render ediyor, RTL `container`'ı bunu kapsamıyor) tüm `.grid`
  elemanlarının `w-full` class'ını taşıdığını doğrulayan bir CSS-regresyon koruması. Saf bir davranış testi değil,
  bilinçli bir istisna — açıklayıcı yorumla işaretlendi.
- **Manuel** (gerekli — jsdom gerçek CSS Grid genişlik çökmesini yeniden üretemez): Ayarlar → İzin kuralları →
  aşağıdaki 3+ farklı türde "Düzenle" tıklanarak doğrulanmalı: Yıllık izin (PerYear, en çok alanlı — kıdem
  kademeleri dahil), Mazeret izni (PerRequest), Hastalık izni (Unlimited). Beklenen: form tam genişlikte açılır,
  tüm alanlar okunabilir/tıklanabilir, tek karakter genişliğinde şerit YOK.
- `pnpm exec tsc --noEmit`: temiz (2 ilgisiz pre-existing hata `protected-route.test.tsx`/`home.test.tsx`'te —
  bu kartın hiç dokunmadığı dosyalar, `User`/`MeFeatures` tipine bu koddan bağımsız olarak sonradan eklenen
  zorunlu alanlardan kaynaklanıyor).
- `pnpm exec eslint src`: 0 hata (1 ilgisiz pre-existing uyarı, `employee-list.tsx` — aynı uyarı daha önce
  senseik-008 handoff'unda da not edilmiş, bu kartın kapsamı dışı).
- `pnpm exec vitest run src/components/leave src/pages/leave`: 21/21 yeşil (5 dosya, senseik-039 ile ortak).

## Kritik Dosyalar

- `web/src/components/leave/leave-type-dialog.tsx` (düzeltme)
- `web/src/components/leave/leave-type-dialog.test.tsx` (regresyon testi)
- `web/src/components/ui/form-modal.tsx` (referans — bu kartta değiştirilmedi, sadece incelendi)

---
*Kaynak: 1. Tur Test Bulgu Dokümanı § B-01. Agent board: `docs/agent-board/cards/senseik-038-leave-type-edit-modal-css-bug.json`.*

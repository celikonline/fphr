# senseik-021 — Form/Modal Açılışları Sola Dayalı — UYGULAMA NOTU

**Durum: GATE 1 ve GATE 2 onaylandı, uygulandı, branch `a/form-modal-alignment` (commit `4780705`).**

## Ne değişti

Mantine `Modal` projede tutarsız kullanılıyordu: form modalları `centered` prop'unu kullanmadığı
için Mantine'in varsayılan davranışıyla açılıyordu (dikeyde üstte, ama **yatayda ortalı** —
`centered` yalnızca dikey konumu kontrol ediyor). Onay/basit modallar (`confirm-dialog.tsx`,
`components/ui/confirm-modal.tsx`) zaten `centered` kullanıyordu ve dokunulmadı.

**Yeni paylaşılan bileşen:** `web/src/components/ui/form-modal.tsx` — `FormModal`, Mantine
`Modal`'ı aynı API ile sarmalıyor, tek farkı `styles={{ inner: { justifyContent: "flex-start" } }}`.
Mantine 9'da `Modal`'ın "inner" flex sarmalayıcısı yatay konumu `justify-content` ile kontrol
ediyor (`ModalContent.mjs` → `ctx.getStyles("inner", ...)`); `centered` prop'u sadece
`align-items`'i değiştiriyor. Bu, en az invaziv çözümdü: mevcut `Modal` API'si (`title`, `size`,
`fullScreen`, `onClose`, ...) hiçbir kullanım noktasında bozulmadı, sadece import + tag adı
değişti (`Modal` → `FormModal`).

**Neden `Drawer`'a geçilmedi:** Projede zaten 4 `Drawer` kullanımı var ama bunlar kenara-dokunan
kayan panel deseni (leave-request-detail.tsx, team-timesheet.tsx, review-cycles.tsx,
candidates.tsx) — farklı bir UX (kayma animasyonu, tam yükseklik kenar paneli). Kullanıcı isteği
"sola yaslı modal", "kayan yan panel" değil; `Drawer`'a geçiş her 4 dosyada state/prop şeklini
(footer, scroll, overlay davranışı) yeniden düşünmeyi gerektirirdi — orantısız.

**Değiştirilen dosyalar (`Modal` → `FormModal`):**
- `web/src/components/leave/leave-request-dialog.tsx`
- `web/src/components/employees/create-employee-dialog.tsx`
- `web/src/components/leave/employee-rule-set-dialog.tsx` (kartta `settings/employee-rule-set-dialog.tsx` olarak anılmıştı; gerçek yol `components/leave/employee-rule-set-dialog.tsx`, `ChangeEmployeeRuleSetDialog`)
- `web/src/components/leave/leave-type-dialog.tsx`

**Dokunulmayanlar (bilinçli):** `confirm-dialog.tsx`, `components/ui/confirm-modal.tsx` — hâlâ
`centered`. Projede ~50 başka `Modal` kullanımı daha var (settings sayfaları, recruitment,
performance, vb.) — bu kart sadece görevde adı geçen 4 form modalını kapsıyor; `FormModal`
paylaşılan bileşen olduğu için geri kalanı ileride tek satırlık bir import değişikliğiyle
migrate edilebilir (YAGNI: şimdilik yapılmadı, ayrı bir "geniş rollout" kartı önerilir — aynı
desen senseik-008'in i18n-enum-leaks handoff'unda önerildiği gibi).

## Testler

- `web/src/components/ui/form-modal.test.tsx` (yeni) — `FormModal`'ın "inner" sarmalayıcıyı
  `justifyContent: "flex-start"` ile render ettiğini ve `Modal`'ın normal davranışını (title,
  children, `role="dialog"`) koruduğunu doğrular.
- `web/src/components/leave/leave-type-dialog.test.tsx` — yeni `describe` bloğu, dialogun
  `role="dialog"` elementinin ebeveyninde (`inner` div — Mantine'in `FocusTrap`'ı kendi DOM
  düğümü eklemediği için doğrudan ebeveyn) `justifyContent: "flex-start"` olduğunu doğruluyor.
- `web/src/components/leave/employee-rule-set-dialog.test.tsx` (yeni) — aynı pozisyon testi +
  temel render testi (`ChangeEmployeeRuleSetDialog` için ilk test dosyası).

**Not — DOM ayrıntısı:** Mantine 9'da `.mantine-Modal-inner` gibi statik bir class adı yok (eski
Mantine sürümlerinin aksine); "inner" parçası CSS-module hash'li class alıyor ama `styles` prop'u
üzerinden verilen inline `style` her zaman doğrudan o `div`'e uygulanıyor. Testler bu yüzden class
seçici yerine `getByRole("dialog").parentElement` üzerinden inline stili okuyor.

## Doğrulama

Host'ta pnpm yok (bu makinede node/pnpm kurulu değil) — Docker `node:24-alpine` + `corepack
enable` ile:
- `pnpm exec tsc --noEmit` — temiz.
- `pnpm exec eslint <değişen dosyalar>` — temiz.
- `pnpm exec vitest run src/components/ui/form-modal.test.tsx src/components/leave/employee-rule-set-dialog.test.tsx src/components/leave/leave-type-dialog.test.tsx` — 8/8 yeşil (leave-type-dialog.test.tsx toplam 4 test, önceki senseik-008 regresyon testleri dahil, hepsi hâlâ yeşil).

Tam `pnpm exec vitest run` (repo geneli) bu kart kapsamında ayrıca çalıştırılmadı — commit,
husky'nin lint-staged (ESLint) hook'undan geçti; `dotnet format` hook'u bu commit'te staged `.cs`
dosyası olmadığı için tetiklenmedi.

## Sonraki adım / bilinen kapsam dışı

- ~50 diğer `Modal` kullanımı (settings, recruitment, performance sayfaları) hâlâ merkezde açılıyor
  — ayrı bir kart önerilir, `FormModal` zaten hazır olduğu için düşük efor.
- `docs/agent-board/cards/senseik-021-form-alignment.json`'daki `worktree` alanı
  `.claude/worktrees/form-alignment` diyor; bu oturum aslında `.claude/worktrees/agent-ace06a8ad4cd819d0`
  worktree'sinde, `a/form-modal-alignment` dalında çalıştı (kart dosyasına dokunulmadı, orkestratör
  talimatına göre branch adı eşleşiyor).

---
*Kaynak: Kullanıcı isteği (2026-09-18). Agent board: `docs/agent-board/cards/senseik-021-form-alignment.json`.*

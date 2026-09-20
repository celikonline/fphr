# senseik-033 — UI Tooltip Component — DOĞRULAMA + KÜÇÜK REFAKTOR

**Durum: Uygulandı (GATE 1/GATE 2 onaylandı). `a/ui-tooltip-verify` dalında commit edildi (`8ebc64e`); push yapılmadı.**

## Bulgular

- `web/src/components/ui/` altında ayrı bir tooltip sarmalayıcı **yoktu** (`approval-card.tsx`, `confirm-modal.tsx`, `data-table.tsx`, `date-picker.tsx`, `file-dropzone.tsx`, `filter-bar.tsx`, `time-picker.tsx`, `index.ts`).
- Mantine `Tooltip` kullanımı **zaten tutarlıydı**: 35 dosyada doğrudan `<Tooltip label={t(...)}>` deseni, `ActionIcon`'a `aria-label={t(...)}` ile aynı metin eşleniyor. 29 dosya `ActionIcon` kullanıyor; bunlardan sadece 2'sinde hiç `Tooltip` yoktu:
  - `web/src/pages/users-roles.tsx` — bir kebab (⋯) menü tetikleyicisi, zaten `aria-label` var, tooltipsiz olması makul (self-explanatory "diğer eylemler" ikonu).
  - `web/src/components/leave/leave-type-dialog.tsx` — bir satır-silme (Trash2) ikon butonu, ne tooltip ne `aria-label` var; gerçek bir a11y/UX boşluğu ama kapsam dışı bırakıldı (bu kartın odağı ayrı bileşen çıkarımıydı, tekil eksik etiket değil — istenirse ayrı küçük bir düzeltme olarak açılabilir).
- Asıl bulgu: **Tooltip+ActionIcon boilerplate'i dosyalar arası kopyalanmıştı.** `web/src/components/employees/payments-section.tsx` zaten kendi yerel `RowIconButton` yardımcı bileşenini icat etmişti (`Tooltip label={label}><ActionIcon variant="subtle" color="gray" aria-label={label} ...`); aynı desen `locations.tsx` (4 kopya), `payments.tsx` (1 kopya) ve `absences-panel.tsx` (2 kopya) içinde satır satır tekrarlanmıştı — bazılarında `color="red"` varyasyonuyla.

## Karar

"Zaten tutarlı, dokunma" eşiği tam karşılanmadı: tooltip *davranışı* tutarlıydı ama *kodu* dört dosyada kopya-yapıştırdı. YAGNI kapsamında karmaşık bir API değil, mevcut yerel `RowIconButton` desenini merkezi hale getirdim.

## Yapılan Değişiklik

- **Yeni:** `web/src/components/ui/row-icon-button.tsx` — `RowIconButton({ label, icon, onClick, color? })`. Sabitler: `type="button"`, `variant="subtle"`, `aria-label={label}`, `Tooltip openDelay={300}` (önceki hiçbir kopyada `openDelay` yoktu — hızlı mouse geçişlerinde tooltip'in flaşlamasını önlemek için eklendi). `color` varsayılanı `"gray"`, `"red"` gibi override'lara izin veriyor (silme ikonları için).
- `web/src/components/ui/index.ts` — barrel export eklendi.
- Dört dosya migrate edildi (kanıt/örnek olarak, tamamı değil):
  - `web/src/components/employees/payments-section.tsx` — yerel `RowIconButton` tanımı silindi, paylaşılan bileşen import edildi; `ActionIcon`/`Tooltip` importları artık kullanılmadığı için kaldırıldı.
  - `web/src/pages/locations.tsx` — tablo satırı + kart görünümü, 4 kopya → `RowIconButton` çağrıları.
  - `web/src/pages/payments.tsx` — masraf kalemi silme ikonu; sayfada başka bir `Tooltip` (rozet üzerinde limit uyarısı) olduğu için `Tooltip` import'u korundu, sadece `ActionIcon` kaldırıldı.
  - `web/src/components/leave/absences-panel.tsx` — "geri al" ikonu, tablo hücresi + kart görünümü (2 kopya).
- Dokunulmayan yerler: `Menu.Target` içine gömülü `Tooltip+ActionIcon` kombinasyonları (ör. `employee-card.tsx`, `structure/levels-panel.tsx`, `users-roles.tsx`) — bunlar `onClick` değil menü açma tetikleyicisi, `RowIconButton`'ın basit API'siyle uyuşmuyor, kapsam dışı bırakıldı.

## Doğrulama

- Host'ta `pnpm`/`node` PATH'te değildi (proje belleği "host pnpm çalışıyor" diyordu ama bu oturumda/worktree'de yoktu); `C:\Program Files\nodejs\node.exe` + corepack JS girişi mevcuttu, ondan `corepack pnpm` çalıştı. Docker fallback ile de doğrulandı (`node:24-alpine`, `corepack enable`, isimli volume'lar `web/node_modules` vb. üzerine).
- Docker: `pnpm install` (18.9s, kilit dosyası tedarik zinciri politikalarını geçti) → `pnpm exec tsc --noEmit` **temiz** → `pnpm exec eslint src` **temiz**.
- Host: `corepack pnpm install` başarılı → commit sırasında husky `pnpm exec lint-staged` → `pnpm --dir web exec eslint --fix` **temiz**, ek değişiklik yok.
- Commit: `8ebc64e` — "refactor: extract shared RowIconButton for icon-only action buttons" (`a/ui-tooltip-verify` dalında, push yapılmadı).

## Notlar

- `leave-type-dialog.tsx`'teki tooltipsiz/etiketsiz silme ikonu ayrı bir küçük iyileştirme adayı — bu kartın kapsamı dışında bırakıldı, istenirse ayrı bir kart/PR olarak açılabilir.
- `RowIconButton` bilinçli olarak minimal tutuldu (`label`, `icon`, `onClick`, opsiyonel `color`) — `size`, `disabled` gibi ek prop'lar şu an hiçbir çağıran tarafından ihtiyaç duyulmuyor (YAGNI); ihtiyaç ortaya çıktıkça eklenebilir.

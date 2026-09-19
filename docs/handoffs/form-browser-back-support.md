# senseik-022 — Menüden Açılan Formlarda Tarayıcı Geri Tuşu Desteği — UYGULAMA NOTU

**Durum: GATE 1 ve GATE 2 onaylandı, uygulandı, branch `a/form-browser-back-support`
(commit `91de562`, `a/form-modal-alignment`'ın üstünde).**

## Ne değişti

Form/dialog'lar `useState` ile açılıyordu, URL değişmiyordu; tarayıcı geri tuşuna basınca
diyalog kapanmıyor, kullanıcı doğrudan sayfadan çıkıyordu (SPA'da bir önceki route'a gidiyordu).

**Yeni paylaşılan hook:** `web/src/hooks/use-dialog-history-sync.ts` — `useDialogHistorySync(name)`,
bir diyaloğun açık/kapalı durumunu `?dialog=<name>` URL parametresine bağlıyor:
- `open()` → `setSearchParams` ile parametreyi **push** eder (yeni history girdisi).
- `close()` → parametreyi **replace** ile kaldırır (forward yığınında çöp bırakmaz).
- `isOpen` doğrudan URL'den okunuyor (`searchParams.get("dialog") === name`) — bu yüzden geri
  tuşu (push edilen girdiyi pop eder) diyaloğu **ücretsiz** kapatıyor; ekstra bir `popstate`
  dinleyicisi yazmaya gerek yok, React Router'ın `useSearchParams`'ı zaten `popstate`'i dinliyor.

Gerçek route değişimleri (`employees/:id/:tab?` gibi) bu parametreye hiç dokunmuyor, davranışları
değişmedi.

## Uygulandığı 2 form

1. **`web/src/pages/leave/my-leave.tsx`** — `LeaveRequestDialog` ("+ Talep", satır düzenle,
   reddedilmiş talebi yeniden gönder — 3 giriş noktası, hepsi aynı diyaloğu farklı `mode`'larla
   açıyor). `dialog` state'i (mode + hangi kayıt) hâlâ yerelde tutuluyor — URL sadece "açık mı"
   sinyalini taşıyor, hangi moda olduğunu değil (edit/resubmit modları bir satıra tıklanarak
   açılıyor, URL'den deep-link ile yeniden kurulabilir bir state değil). Modal'ın render koşulu
   `{dialog && dialogUrlOpen && (...)}` — `dialogUrlOpen` `false` olur olmaz (geri tuşu) modal
   render'dan düşüyor, `dialog` state'i ise render sırasında (efekt içinde değil —
   `react-hooks/set-state-in-effect` kuralına takılmamak için `employee-list.tsx`'teki
   `prevFilter` deseniyle aynı "türetilmiş state'i render'da senkronize et" yaklaşımı kullanıldı)
   `null`'a çekiliyor.
   - **Mevcut `?new=1` hub-bağlantısı davranışı korundu** (roadmap #42, `/app/requests`'ten
     `?new=1` ile gelince diyalog ilk render'da açık başlıyor): mount effect artık `new` parametresini
     silip aynı anda (`replace`, tek atomik `setSearchParams` çağrısı) `dialog=leave-request`
     işaretini yazıyor — böylece bu giriş yolu da diğerleriyle aynı geri-tuşu davranışına sahip.
2. **`web/src/pages/employees/employee-list.tsx`** — `CreateEmployeeDialog` ("Yeni çalışan ekle").
   Daha basit: tek mod, ekstra local state yok, `createOpen`/`openCreate`/`closeCreate` doğrudan
   hook'tan.

## Neden paylaşılan hook (YAGNI notu)

Kart hem izin talebi hem çalışan oluşturma formunu istedi — yani aynı "aç → push, kapat → replace,
isOpen URL'den oku" mantığı en az 2 dosyada birebir tekrar edecekti (~15-20 satır, kopyala-yapıştır
kayması riski yüksek: push/replace karışırsa geri tuşu ya iki kez basmayı gerektirir ya da hiç
çalışmaz). Bu yüzden merkezi bir hook'a çıkarıldı; ek karmaşıklık yok, `open`/`close`/`isOpen`
üçlüsünden fazlası eklenmedi (ör. hangi moda açıldığını URL'de taşımak gibi bir genelleme
YAPILMADI çünkü hiçbir çağıran buna ihtiyaç duymuyor).

## Testler

`web/src/hooks/use-dialog-history-sync.test.tsx` (5 test, `MemoryRouter` + `renderHook`):
1. URL'de `dialog` parametresi yokken `isOpen === false`.
2. `open()` → `?dialog=<name>` yazıyor, `isOpen === true`.
3. `close()` → parametreyi kaldırıyor, `isOpen === false`.
4. **Geri tuşu simülasyonu**: `open()` sonrası `navigate(-1)` (gerçek tarayıcı geri tuşunun
   React Router karşılığı — `window.history.back()` `MemoryRouter`'ın kendi bellek-içi history'sini
   etkilemediği için kullanılmadı) → `isOpen === false`.
5. Aynı sayfada iki farklı isimli diyalog (`leave-request`, `create-employee`) birbirine
   karışmıyor — gerçek kullanım senaryosunu (my-leave.tsx + employee-list.tsx aynı anda mount
   olsa bile) doğruluyor.

Sayfa seviyesinde (`my-leave.tsx`, `employee-list.tsx`) ayrı bir entegrasyon testi **yazılmadı**:
bu iki dosya zaten OrgUnitSelect/EmployeePicker/DataTable gibi ağır bağımlılıklar taşıyor ve
codebase'de hiçbir sayfa için page-level render testi yok (component/hook seviyesinde test
konvansiyonu var). Hook testleri gerçek entegrasyon noktalarının (`"leave-request"`,
`"create-employee"`) davranışını zaten izole şekilde kanıtlıyor; sayfa entegrasyonu manuel
test + `merge_gate`'teki adımlarla doğrulanmalı.

## Doğrulama

Host'ta pnpm yok — Docker `node:24-alpine` + `corepack enable` ile:
- `pnpm exec tsc --noEmit` — temiz.
- `pnpm exec eslint src/hooks/use-dialog-history-sync.ts src/hooks/use-dialog-history-sync.test.tsx src/hooks/index.ts src/pages/leave/my-leave.tsx src/pages/employees/employee-list.tsx` —
  temiz, tek **ilgisiz pre-existing uyarı** (`employee-list.tsx:168`, `items` useMemo dep listesi —
  bu kartın dokunmadığı satır, senseik-008 handoff'unda da aynı dosya için not düşülmüş).
- `pnpm exec vitest run` (repo geneli) — **392/393 yeşil**. Tek kırmızı: `i18n.locales.test.ts`
  → `de/attendance.json` `teamTimesheet.week/month` çevrilmemiş placeholder — **ilgisiz
  pre-existing hata**, senseik-008 handoff'unda da aynı şekilde kayıtlı, bu kartın dokunmadığı dosya.

## Manuel test (merge_gate)

`pnpm --filter web typecheck + eslint` yukarıda geçti. Manuel doğrulama (tarayıcıda):
1. `/app/leave/me` → "+ Talep" → form açılır, URL `?dialog=leave-request` olur → geri tuşu →
   form kapanır, sayfa `/app/leave/me`'de kalır (dışarı çıkmaz).
2. `/app/employees` → "Yeni çalışan ekle" → form açılır, URL `?dialog=create-employee` olur →
   geri tuşu → form kapanır, liste sayfasında kalır.
3. `/app/requests` → "+ Talep" linkiyle `/app/leave/me?new=1`'e gidiş → form açık başlar, URL
   `?dialog=leave-request`'e döner (replace) → geri tuşu → `/app/requests`'e döner (bu akışta
   "formsuz my-leave" diye ayrı bir history girdisi hiç olmadı, bu beklenen davranış).
4. `employees/:id/:tab?` gibi gerçek route değişimleri (sekme değiştirme, detay sayfasına gitme)
   — geri tuşu davranışı bu karttan önceki gibi çalışmaya devam ediyor (dokunulmadı).

---
*Kaynak: Kullanıcı isteği (2026-09-18), React Router v8 (`react-router@^8.3.1`, projede "v7" olarak
anılıyor). Agent board: `docs/agent-board/cards/senseik-022-form-back-button.json`.*

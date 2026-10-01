# SenseİK Web Ana Sayfa Görsel Dönüşümü Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Giriş sonrası `/app` sayfasını SenseİK kimliğiyle kullanıcıya bugünkü önceliği gösteren yeni düzene geçirmek ve ayrı Docker önizlemesinde sunmak.

**Architecture:** Var olan widget sorguları ve yetki kapıları korunur. Üstte izin ve gerçek bekleyen onay verisine göre seçilen tek odak eylemi, altta iş akışı ile şirket bağlamı için iki dengeli bölüm yer alır. Yeni düzen Mantine ve tek sayfa CSS module ile kurulur; Docker önizlemesi mevcut API ağına bağlanan ayrı bir web konteyneridir.

**Tech Stack:** React 19, TypeScript, TanStack Query, Mantine 9, CSS modules, react-i18next, Vitest, Vite, Docker/nginx.

**Spec:** `docs/superpowers/specs/2026-10-01-web-tam-gorsel-donusum-design.md` (özellikle “Ana sayfa” ve 2026-10-01 ilk teslim kararı).

## Global Constraints

- `web/src/lib/mantine-theme.ts` paleti, Onest/Bricolage fontları, açık/koyu/sistem teması ve kullanıcı paleti korunur.
- Mevcut home widget veri kaynakları, sorgu anahtarları, modül/izin kapıları, linkler ve hata yalıtımı korunur.
- Yeni metin `web/public/locales/{tr,en,de}/home.json` dosyalarında birlikte yazılır.
- 375/768/1440 px'te gövde yatay taşmaz; klavye odağı ve azaltılmış hareket tercihi gözetilir.
- Mevcut `senseik-web` (:13000) ve `senseik-api` (:18080) konteynerleri yeniden oluşturulmaz; önizleme :13001'de ayrı konteynerdir.

## Review Focus

1. Bekleyen onay yetkisi olmayan kullanıcıda onay sorgusu çalışmaz ve onay eylemi görünmez (Task 1).
2. Onay sorgusu hata verirse diğer widget'lar görünmeye devam eder ve odak uygun yedek eyleme döner (Task 1).
3. Yetkili kullanıcının sıfır bekleyen onayı varsa sahte iş sayısı gösterilmez (Task 1).
4. Erişim ayarı/modül kapalıysa ilgili widget yeni düzende de gizlenir (Task 2).
5. Uzun Alman dili metni ve 375 px genişlikte gövde yatay taşmaz (Task 2).

---

### Task 1: Gerçek veriye bağlı tek odak eylemi

**Files:**
- Create: `web/src/pages/home-focus.tsx`
- Create: `web/src/pages/home-focus.test.tsx`
- Modify: `web/src/pages/home.tsx`
- Modify: `web/public/locales/tr/home.json`
- Modify: `web/public/locales/en/home.json`
- Modify: `web/public/locales/de/home.json`

**Interfaces:**
- Produces: `HomeFocus({ canViewInbox, canRequestLeave, canViewDirectory }: { canViewInbox: boolean; canRequestLeave: boolean; canViewDirectory: boolean })`.
- Consumes: `listInbox`, Query key `["home", "pending-approvals"]`, `getApiErrorStatus`; rota hedefleri `/app/inbox`, `/app/leave`, `/app/employees`, `/app/account`.

- [ ] **Step 1: Odak seçimini testle.** Yetki yokken onay sorgusu çalışmaması; onay sayısı pozitifken onay CTA'sı; sıfır veya sorgu hatasında izin, dizin ve en son hesap eylemine düşme davranışı için `home-focus.test.tsx` yaz ve başarısızlığı gör.
- [ ] **Step 2: Bileşeni uygula.** `useQuery({ queryKey: ["home", "pending-approvals"], queryFn: listInbox, enabled: canViewInbox, retry: (count, error) => ![401, 403, 404].includes(getApiErrorStatus(error) ?? 0) && count < 1 })` mevcut widget ile aynı cache anahtarını paylaşır. Öncelik: başarılı ve `data.length > 0` ise onay; sonra `canRequestLeave`, sonra `canViewDirectory`, son olarak herkes için hesap. Hata/sıfır sonucu sayı üretmez. Başlık, açıklama ve CTA çevirilerden gelir; `Link` + Mantine `Button` kullanılır.
- [ ] **Step 3: `HomePage` başına `HomeFocus` ekle.** `canRequestLeave = usePermission("leave.my.requests") && leaveEnabled`, `canViewDirectory` mevcut değer; izin ve modül kapıları korunur. `home-focus.test.tsx` ve `home.test.tsx` çalıştır; ayrı commit yap.

### Task 2: Widget'ları yeni bilgi düzenine yerleştir

**Files:**
- Modify: `web/src/pages/home.tsx`
- Create: `web/src/pages/home.module.css`
- Test: `web/src/pages/home.test.tsx`

**Interfaces:**
- Consumes: Task 1'in `HomeFocus` bileşeni ve var olan 10 widget bileşeni.
- Produces: İş akışı (onay/talepler/duyurular/izin bilgisi) ile şirket bağlamı (kişisel kart/takvim/dağılım) bölümleri.

- [ ] **Step 1: Mevcut korumaları çalıştır.** `cd web && pnpm exec vitest run src/pages/home.test.tsx`; beklenen sonuç yeşil. Gerekli rol/modül gizleme regresyonunu aynı dosyaya ekle.
- [ ] **Step 2: 3 eşit kolon yerine 2:1 içerik hiyerarşisi kur.** `HomeFocus` üstte tam genişlik; iş akışı bölümünde `PendingApprovalsWidget`, `TeamLeaveRequestsWidget`, `AnnouncementsWidget`, `LeaveInfoWidget`; bağlam bölümünde `OrgInfoWidget`, `EventsWidget`, `UpcomingLeavesWidget`, `BirthdaysWidget`, `HolidaysWidget`, `DepartmentDistributionWidget`. Var olan koşulları aynen taşı. Her bölüm boş kalırsa başlığı/kapsayıcısı da görünmez; ikisi de boşsa mevcut `noWidgets` açıklaması odak kartının altında görünür.
- [ ] **Step 3: Stili token'lardan kur.** `home.module.css` iki sütunlu grid, `min-width:0`, kendi içinde kayan taşkın öğeler; tablet/telefonda tek sütun. Widget kartlarının border/gölgesini mevcut Mantine değişkenleriyle sakinleştir; odak alanı tek belirgin yüzey olsun. Yeni durum animasyonu ekleme.
- [ ] **Step 4: Doğrula.** `home.test.tsx`, TypeScript, ESLint ve Vite build; açık/koyu tema ve 375/768/1440 px'te ekran görüntüsü. Ayrı commit yap.

### Task 3: Docker önizlemesi ve kullanıcıya gösterim

**Files:**
- Use unchanged: `web/Dockerfile`, `web/nginx.conf`, `docker-compose.yml`
- Record preview instructions: `docs/implementation/web-gorsel-donusum-onizleme.md`

**Interfaces:**
- Consumes: mevcut `senseik_default` Docker ağı ve `senseik-api` servis adı.
- Produces: yalnız `127.0.0.1:13001` üstünden erişilen `senseik-web-preview` konteyneri.

- [ ] **Step 1: Docker ağı ve portu doğrula.** `docker network inspect senseik_default` ve port 13001 dinleyicisini kontrol et; çakışma varsa boş yerel port seçip belgeye yaz.
- [ ] **Step 2: Worktree kaynağından imaj üret.** Repo kökünden `docker build -f web/Dockerfile --build-arg VITE_API_BASE_URL=/api --build-arg VITE_SHOW_DEMO_CREDENTIALS=true -t senseik-web-gorsel-preview .` çalıştır. Derleme mevcut `senseik-web` imajını değiştirmez.
- [ ] **Step 3: Ayrı konteyneri başlat.** `docker run -d --name senseik-web-preview --network senseik_default -p 127.0.0.1:13001:8080 senseik-web-gorsel-preview`; nginx `/api` isteklerini mevcut `api` ağına yollar. `http://127.0.0.1:13001/` ve `/api/...` sağlık yanıtını kontrol et.
- [ ] **Step 4: Tarayıcıda göster.** Mevcut demo hesabıyla `/app`'i aç; açık/koyu tema ve 375/1440 px ekran görüntüsünü incele, hataları düzelt. Kullanıcıya önizleme adresini ve neyi görmesi gerektiğini bildir. Önizleme konteyneri inceleme boyunca ayakta kalır.
- [ ] **Step 5: Belgele ve commit et.** `web-gorsel-donusum-onizleme.md` yalnız yeniden üretilebilir build/run/durdurma komutlarını ve portu içerir; sır veya demo parolası içermez.

## Aşama kapısı

- [ ] `cd web && pnpm exec tsc --noEmit`
- [ ] `cd web && pnpm exec eslint src`
- [ ] `cd web && pnpm exec vitest run src/pages/home-focus.test.tsx src/pages/home.test.tsx`
- [ ] `cd web && pnpm exec vite build` (Docker build bunu ayrıca yapar)
- [ ] `node .claude/takim/betikler/test-kapisi.mjs` mevcutsa çalıştır
- [ ] Docker önizlemesini kullanıcıya göster; sonraki ekran ailesine geçişi mevcut üst spec sırasıyla sürdür.

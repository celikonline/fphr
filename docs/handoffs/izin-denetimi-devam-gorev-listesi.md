# Görev listesi: İzin denetimi bulgularının kalanı (2026-09-20 devri)

**Bu dosyayı sırayla, atlamadan uygula. Bir adım anlaşılmazsa DUR ve kullanıcıya sor. Tahmin etme.**
Kullanıcı Türkçe konuşur; kullanıcıya Türkçe yaz.

## 0. Genel kurallar (hepsini oku)

1. Repo: `C:\Users\ALG0013\Documents\GitHub\senseik`. Dal: **`dev`**. `main` dalına ASLA commit atma.
2. `git push --force`, `git reset --hard`, `git checkout -- .`, `git clean`, `git branch -D`, `git worktree remove --force` **YASAK** (kullanıcı açıkça istemedikçe).
3. `git commit --no-verify` **YASAK**. Commit hook'u (husky) başarısız olursa hatayı oku ve düzelt.
4. Commit mesajı formatı: `<tip>: <Türkçe açıklama>` (tip: feat, fix, refactor, docs, test, chore). Satır başına en çok 100 karakter (commitlint).
5. Tek seferde tek iş yap. Her işten sonra: testi çalıştır → commit et → sonraki işe geç.
6. Bir araç "Fact-Forcing Gate" diye reddederse: hangi dosyayı kimin kullandığını, neyi değiştirdiğini ve kullanıcının talimatını bir cümleyle yaz, sonra AYNI işlemi tekrar dene.
7. Windows + Git Bash kullanılıyor. Komutların başına şunu koy (her Bash çağrısında yeniden gerekir, PATH kalıcı değildir):
   ```bash
   export PATH="/tmp/shim:/c/Program Files/dotnet:/c/Program Files/nodejs:$PATH"
   ```
   `/tmp/shim/pnpm` yoksa oluştur (husky için gerekli):
   ```bash
   mkdir -p /tmp/shim && printf '#!/bin/bash\nexec "/c/Program Files/nodejs/corepack.cmd" pnpm "$@"\n' > /tmp/shim/pnpm && chmod +x /tmp/shim/pnpm
   ```
8. `.NET` testlerini **tüm çözümde çalıştırma**. Yalnızca ilgili proje: `dotnet test tests/Modules/<Proje> --filter "FullyQualifiedName~<Ad>"`.
9. Web kontrolleri `web/` klasöründe: `corepack.cmd pnpm exec tsc --noEmit`, `corepack.cmd pnpm exec eslint src`, `corepack.cmd pnpm exec vitest run <dosya>`. `web/node_modules` yoksa önce `corepack.cmd pnpm install --frozen-lockfile` (uzun sürer, sabret).
10. Demo giriş bilgileri: `owner@demo.local`, `ik@demo.local`, `bordro@demo.local` — şifre hepsinde `Demo123!`.
11. Adresler: web `http://localhost:13000`, API `http://localhost:18080`, Mailpit `http://localhost:18025`.

## 1. Durumu öğren (hiçbir şey değiştirme)

```bash
cd /c/Users/ALG0013/Documents/GitHub/senseik
git status -sb
git log --oneline -15
git worktree list
for b in $(git for-each-ref --format='%(refname:short)' refs/heads | grep worktree-agent); do echo "$b benzersiz=$(git rev-list --count dev..$b)"; done
```
Beklenen: `dev`, `origin/dev`'in **çok commit önünde** (push edilmemiş). `worktree-agent-abc47ccc5e1ce34da` dalının `benzersiz` sayısı 0'dan büyük olabilir (henüz birleşmemiş iş). Diğer `worktree-agent-*` dalları için sayı **0** olmalı (zaten `dev`'de).

Çalışma dizini temiz değilse (`git status` değişiklik gösteriyorsa) **DUR**, kullanıcıya sor.

## 2. İş A — Bitmemiş ajan işini bitir ve birleştir (A5 vekil + A6 cinsiyet uygunluğu)

Dal: `worktree-agent-abc47ccc5e1ce34da`, klasör: `.claude/worktrees/agent-abc47ccc5e1ce34da`.

1. Durumu gör:
   ```bash
   git -C .claude/worktrees/agent-abc47ccc5e1ce34da status --short
   git log --format='%h %s' dev..worktree-agent-abc47ccc5e1ce34da
   ```
   Commit'li iş: cinsiyet uygunluğu (Babalık=Erkek, Doğum/Süt=Kadın) ve vekil sunucu doğrulaması. Commit'lenmemiş: ~15 web dosyası (vekil alanı, uygunluk filtresi, tür formu).
2. O worktree klasöründe web kontrolleri çalıştır (`tsc`, `eslint src`, `vitest run src/components/leave src/pages/leave`). Hata varsa **yalnızca kendi değişikliklerinden kaynaklananı** düzelt.
3. Backend testleri: `dotnet test tests/Modules/Sense.Modules.Leave.Tests` (Docker açık olmalı; ~5-8 dk).
4. Hepsi yeşilse worktree'de commit et (`feat(web): izin talebinde vekil alanı ve uygunluk filtresi`), sonra `dev`'e birleştir:
   ```bash
   cd /c/Users/ALG0013/Documents/GitHub/senseik
   git merge --no-ff worktree-agent-abc47ccc5e1ce34da -m "merge: izin türü uygunluğu ve vekil (A5, A6)"
   git diff --name-only --diff-filter=U
   ```
   Son komut boş çıkmalı. **Çakışma varsa** (dosya adı yazdırırsa): `git merge --abort` yap, hangi dosyalar olduğunu kullanıcıya yaz, DUR.
5. **Belge zorunlu** (`RequiresDocument`) işi yapılmadıysa: kullanıcıya "belge zorunlu ayarı hâlâ etkisiz, yapayım mı?" diye sor. Sormadan başlama (Documents modülü entegrasyonu büyük iştir).

## 3. İş B — Testleri doğrula (yalnızca oku, düzeltmeden önce sonucu kaydet)

Sırayla çalıştır, her birinin sonucunu (geçti/kaldı, kaç test) not et:
```bash
dotnet test tests/Modules/Sense.Modules.Leave.Tests
dotnet test tests/Modules/Sense.Modules.Workflow.Tests
dotnet test tests/Modules/Sense.Modules.Organization.Tests
dotnet test tests/Modules/Sense.Modules.Attendance.Tests
dotnet test tests/Modules/Sense.Modules.Employee.Tests
dotnet test tests/Sense.Tests.Architecture
```
Web: `cd web && corepack.cmd pnpm exec tsc --noEmit && corepack.cmd pnpm exec eslint src && corepack.cmd pnpm exec vitest run`.

**Bilinen eski hatalar** (bizim işimizle ilgisiz, dev'de zaten kırık):
- `OnionArchitectureTests.PermissionCatalogs_DoNotExposeUntranslatedEnglishDisplayNames` — üç Payroll izin adı (courtorder, declarations, union) İngilizce görünüyor. Düzeltme: `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx` ve `.en.resx` içine bu izin adlarının Türkçe/İngilizce karşılığını ekle (mevcut diğer `permission.*` girdilerinin formatına bak).
- `web/src/i18n.locales.test.ts` — `web/public/locales/de/attendance.json` içinde çevrilmemiş yer tutucu işaretleri. Düzeltme: testin hata mesajının gösterdiği anahtarları Almanca çevir.
- `login.test.tsx`, `monthly-timesheet.test.tsx` yük altında zaman aşımı verir; **tek başına** çalıştırınca geçerse sorun yok (`vitest run src/pages/auth/login.test.tsx`).

Yeni bir hata bulursan (yukarıdaki listede olmayan): hangi testin, hangi commit'ten sonra kırıldığını `git log` ile bul, kullanıcıya bildir, DUR.

İki eski hatayı düzeltirsen her biri için ayrı commit at (`fix: ...`).

## 4. İş C — Uygulamayı yeniden derle, migration uygula, canlı doğrula

1. İmajları derle ve başlat (birkaç dakika sürer):
   ```bash
   cd /c/Users/ALG0013/Documents/GitHub/senseik
   docker compose build api web worker
   docker compose up -d api web worker
   ```
2. **Migration'ları uygula** (compose kendiliğinden migrate ETMEZ). Yeni migration'lar: Organization `HolidayCalendarEntryHalfDay`, Leave (uygunluk/vekil). Temiz bir dışa aktarımdan çalıştır (çalışma dizini değil!):
   ```bash
   rm -rf /tmp/senseik-export && mkdir /tmp/senseik-export && git archive HEAD | tar -x -C /tmp/senseik-export
   MSYS_NO_PATHCONV=1 docker run --rm -v "$(cd /tmp/senseik-export && pwd -W)":/src -v senseik-nuget:/root/.nuget/packages --network senseik_default -e "ConnectionStrings__Database=Host=postgres;Port=5432;Database=senseik;Username=sense;Password=sense" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet run --project src/Sense.Migrator -- migrate
   ```
   Hata verirse çıktıyı kullanıcıya göster, DUR.
3. API'nin sağlıklı olduğunu doğrula: `curl -s http://localhost:18080/health` (200 dönmeli, birkaç deneme gerekebilir).
4. Tarayıcıda `http://localhost:13000` aç. **Sayfayı gereksiz yenileme (F5)** — sadece A7 testinde yenile. Aşağıdaki kontrol listesini yap, her satır için ✅/❌ yaz:

   | # | Kullanıcı | Ne yap | Beklenen |
   |---|---|---|---|
   | 1 | `ik@demo.local` | İzinlerim → + Talep → tür listesi | Kadın kullanıcıda "Babalık izni" **yok** |
   | 2 | `ik@demo.local` | İzinlerim → + Talep → Yıllık izin, başlangıç 10 gün önce | Hata: geçmişe en fazla 7 gün |
   | 3 | `ik@demo.local` | + Talep, 12 Ekim 2026 tek gün, açıklama yaz, gönder; sonra "Geri çek" | `owner@demo.local` onay kutusunda **kalmamalı** |
   | 4 | `owner@demo.local` | Onay kutusunda bir talepte Reddet, yorum boş | Reddet çalışmamalı, yorum zorunlu hatası |
   | 5 | `owner@demo.local` | Ayarlar → Çalışma takvimleri → yeni takvim → Pasifleştir | "Varsayılan yap" düğmesi görünmemeli |
   | 6 | `ik@demo.local` | Ayarlar → Tatil takvimleri → yeni takvim: ülke alanı | Aranabilir liste (combobox); "ZZ" seçilemez |
   | 7 | `ik@demo.local` | Tatil takvimi → tatil satırında düzenle/sil | Düğmeler var, silmeden önce onay sorar |
   | 8 | `ik@demo.local` | İzinlerim → 28 Ekim 2026 tek gün yıllık izin, formdaki "Hesaplanan gün" | 0,5 gün |
   | 9 | `owner@demo.local` | Çalışanlar → Ali Şahin → İzinler → Devamsızlık ekle, tarih = bir Pazar | Hata: iş günü değil |
   | 10 | herhangi | Giriş yap, F5 bas, 2 dk sonra başka sayfaya geç | Oturum düşmemeli |

   ❌ çıkan satır varsa: hangi adımda, ne gördüğünü yaz, kullanıcıya bildir. Kendin büyük düzeltme yapma; küçükse (yazım, çeviri) düzelt.

## 5. İş D — Test kalıntılarını temizle (yalnızca arayüzden, ik@demo.local ile)

- Ayarlar → Tatil takvimleri → **"QA Takvim"** (ülke ZZ): içindeki "QA Özel Tatil"i sil, sonra takvimi sil.
- Ayarlar → İzin kuralları → **"QA Test İzni (düzenlendi)"** türü pasif kalabilir (tür silme özelliği yok) — dokunma.
- Çalışanlar → Ali Şahin → Fazla mesailer: "QA fazla mesai" (15 Eyl) onay bekliyor kalabilir — silme özelliği yok, dokunma.
Bunları kullanıcıya "kalıntı" olarak listele.

## 6. İş E — Push (tüm testler ve kontroller bittikten sonra)

```bash
cd /c/Users/ALG0013/Documents/GitHub/senseik
git status -sb
git fetch origin
git rev-list --left-right --count origin/dev...dev
```
İlk sayı (origin/dev'de olup dev'de olmayan) **0** olmalı. 0 değilse: `git pull --no-rebase origin dev` dene; çakışma çıkarsa `git merge --abort`, kullanıcıya bildir, DUR.
Sonra: `git push origin dev` (force YOK).

`gh` (GitHub CLI) bu makinede giriş yapmamış; **PR'ı sen açma**. Kullanıcıya de: "dev → main PR'ını GitHub'dan aç, 'Rebase and merge' veya 'Squash' ile birleştir (Merge commit seçme; yoksa dev ile main arasında sürekli boş fark oluşur)".

## 7. İş F — Temizlik (push'tan SONRA, ancak `benzersiz=0` olan dallar için)

Bir ajan dalı silinebilir **yalnızca** şu komut `0` verirse: `git rev-list --count dev..<dal>`.
Silmeden önce kullanıcıya sor: "şu dalları ve worktree'leri silmemi ister misin: <liste>". Onay gelirse:
```bash
git worktree remove .claude/worktrees/agent-<id>   # --force KULLANMA; hata verirse kullanıcıya bildir
git branch -d worktree-agent-<id>                  # küçük -d: birleşmemişse reddeder (güvenlik)
```
`.claude/worktrees/qa-teslim` boş dizin, silinemiyor ("busy"). Dokunma.

## 8. Kullanıcı kararı bekleyen sorular (sormadan uygulama)

1. Sistem tatil takvimine ("Türkiye Resmi Tatilleri") tatil ekleme salt-okunur olsun mu? (Şu an açık; yanlış eklenen tatil silinemiyor.)
2. Şu ekranlardaki serbest metin **ülke** alanları da combobox olsun mu? `web/src/pages/locations.tsx`, `web/src/pages/settings/visa-processes.tsx`, `web/src/pages/company-profile.tsx`
3. Yalnızca açılır pencereler mi (kart senseik-063) yoksa sayfa içeriği de sola mı dayansın?
4. "Belge zorunlu" izin türü ayarı gerçekten uygulansın mı (iş A adım 5)?
5. `senseik-061` (ana sayfa etkinlik widget'ı + etkinlik düzenleme) ve `senseik-063` (78 düz Modal → FormModal) kartları başlatılsın mı? Kartlar: `docs/agent-board/cards/`.

## 9. Bitince kullanıcıya şunu raporla

- Hangi işler bitti (A–F), her biri için commit hash'i.
- Test sonuçları (geçen/kalan sayıları).
- Canlı doğrulama tablosu (§4) sonucu.
- Push edildi mi.
- Kalan kararlar (§8) ve yapamadıkların.

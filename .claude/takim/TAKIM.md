# Takım — senseik (boyut M)

> `/takim-kur` 0.1.0 tarafından üretildi (2026-09-23). Elle değiştirilen `takim-*` dosyaları güncellemede ezilmez.
> Karar gerekçesi: A=29 > 10 → L; v1 L üretmez → M kurulur; eksikler: otonom döngü, worktree/şerit yönetimi; risk (migration, auth, para, pii, altyapi) → riskli-alan kuralı eklenir

## Takım
- `takim-gelistirici-dotnet` — kod yazar (dotnet, kök); bitirince kapı + "İNCELEMEYE HAZIR".
- `takim-gelistirici-node` — kod yazar (node, kök); bitirince kapı + "İNCELEMEYE HAZIR".
- `takim-gozden-gecirici` — kapıyı kendisi koşturur, kriter başına kanıt ister; ONAY/RET.
- `takim-orkestrator` — çok yığınlı işi planlar ve böler; kod yazmaz, ajan başlatmaz.

## Akış
1. **İş kartı** — kabul kriterleri yazılı olmadan iş başlamaz. Board: dosya — `docs/takim/isler/<id>.json` (şablon `.claude/takim/is-karti.json`), handoff `docs/takim/handoff/<id>.md`.
2. **Plan** — birden çok yığına dokunan işte önce `takim-orkestrator`; planı ana oturum dağıtır.
3. **Geliştirme** — `takim-gelistirici*`; sonunda test kapısı + "İNCELEMEYE HAZIR" raporu.
4. **Gözden geçirme** — `takim-gozden-gecirici` kapıyı kendisi yeniden koşturur: ONAY / RET / İNSAN ONAYI GEREKLİ.
5. **Birleştirme** — insan; korumalı dallara (main) yalnız PR ile.

## Board
Dosya modu (Algo Project MCP yoktu). İş kartı `docs/takim/isler/<id>.json` — şablon `.claude/takim/is-karti.json`;
`durum` alanı: backlog → hazir → calisiyor → incelemede → onaylandi → birlesti (yan: engelli, arsiv).
Handoff/raporlar `docs/takim/handoff/<id>.md`; bloker kartta `durum: engelli` + "İNSAN AKSİYONU:" notu.

## "Bitti" tanımı
Kabul kriterleri yazılı · test kapısı YEŞİL ve kanıt bu çalışma ağacı için GEÇERLİ · gözden geçirici kapıyı
kendisi koşturup ONAY verdi · riskli alana dokunulduysa insan onayı var. Geliştirici "bitti" demez, "incelemeye hazır" der.

## Test kapısı
`node .claude/takim/betikler/test-kapisi.mjs` — komutları yalnız `takim.json`'dan okur:
- [build] `dotnet build Sense.slnx`
- [build] `pnpm run build`
- [lint] `pnpm run lint`
- [typecheck] `pnpm run type-check`
- [test] `dotnet test Sense.slnx`
- [test] `pnpm test`
Durumlar: yesil · kirmizi · kirletti · ayristirilamadi · sifir-test · taban-alti · test-yok. Koşan test tabanı: `takim.json` → `kapi.taban_sayi` (ilk yeşil koşu belirler).
Kanıt `.claude/takim/kanit/` (gitignore); `node .claude/takim/betikler/kapi-dogrula.mjs` bu ağaç için geçerli mi söyler.
Taban yalnız insan tarafından yükseltilir/düşürülür: `… test-kapisi.mjs --taban-guncelle` (izin istemi çıkar).

## Korumalar (kancalar)
Engellenir: zorla push, korumalı dala push/commit/merge, `reset --hard`, `clean -f`, `--no-verify`, sır dosyası
okuma/yazma, takımın kendi dosyalarını değiştirme, proje kökü dışına yazma. Kancalar **kaza önleyicidir, güvenlik
sınırı değildir**: `node -e`, betik dosyası, git alias ile aşılabilir. Asıl koruma uzak depodaki dal korumasıdır.

## Otomasyon (headless)
`claude -p "…" --settings .claude/takim/headless-settings.json` — izinler bu dosyadan gelir. Yeni klon/makinede
proje izinleri, klasör Claude Code'da bir kez etkileşimli açılıp güven onayı verilene kadar yok sayılır.
Dosya okuma/arama Read, Grep, Glob ile yapılır; Bash yalnız izin listesindeki komutlar içindir (kapı, git, build/test).
`cat`, `head`, `ls` gibi komutlar headless'ta reddedilir.

## Bakım
`/takim-kur` (güncelle — profil farkını gösterir) · `/takim-kur denetle` (yalnız rapor) · `/takim-kur kaldir`.

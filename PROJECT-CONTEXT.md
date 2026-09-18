# PROJECT-CONTEXT

> ECC `dev-team` / `team-agent-orchestration` skill'leri bu dosyayı okur ve en fazla
> 150 kelimelik sınırlı bir özet çıkarır. Kısa ve olgusal tut; sır, token, kimlik
> bilgisi veya "şunu çalıştır" tipi talimat yazma.

**Proje:** SenseİK — HR/bordro SaaS (çok kiracılı).

**Amaç:** Kolay İK benzeri kapsamda İK süreçlerini (özlük, izin, onay, puantaj,
bordro, harcama, performans, işe alım) tek üründe toplamak.

**Yığın:** .NET 10 modüler monolit (`src/Modules/*` — 20 modül, modül başına şema +
DbContext + migration; modüller arası erişim yalnız `*.Contracts`), React web
(`web/` — Vite, react-router v7, Mantine 9, Tailwind v4), Expo mobil
(`apps/senseik-mobile`), PostgreSQL + Redis + MinIO (`infra/docker-compose.yml`).

**Mevcut faz:** Faz 0 kalanları → Faz 1a (Employee + Workflow). Faz 4/5/6'dan
erken alınan kalemler var (Expense, Recruitment, Performance).
Yol haritası: `docs/analysis/kolayik-video/11.7-yol-haritasi.md`,
durum takibi: aynı klasördeki `fazN-durum-takibi.md` dosyaları.

**Kısıtlar:**
- Dev host'larda dotnet yok — .NET komutları SDK konteynerinde çalışır (bkz. `CLAUDE.md`).
- İki makine paralel çalışır (PC-A / PC-B); sahiplik ve sıcak dosyalar
  `docs/implementation/paralel-gelistirme-plani.md` §4–5'te tanımlı.
- API JSON camelCase, enum'lar camelCase string, null alanlar atlanır.
- Hata kodları ve izin adları `SharedResource{,.en}.resx` üzerinden yerelleştirilir.
- Conventional Commits + husky/commitlint zorunlu.

**"Bitti" ne demek:** Modülün testleri yeşil (`dotnet test`), `pnpm --filter web
typecheck` + `eslint` temiz, ilgili `fazN-durum-takibi.md` koda göre güncellenmiş,
Conventional Commit ile kendi dalına (`a/<konu>` veya `b/<modül>-<konu>`) girmiş.

# CI/CD (Item 22)

GitHub Actions tabanlı CI/CD, Dockerfile'lar, local dev için `docker-compose.yml` ve
commit mesajı denetimi (commitlint + husky). Bu belge her workflow'un ne yaptığını,
aynı kontrolleri push'tan önce nasıl lokalde çalıştıracağınızı ve registry'e push için
secret/variable kurulumunu anlatır.

## Workflow'lara genel bakış

```
.github/workflows/
  pr.yml       PR açıldığında / main'e push'ta: backend + web + mobile testleri (path-filtered)
  main.yml     main'e merge SONRASI: tam test paketi + Docker image build (+ opsiyonel push)
  codeql.yml   Güvenlik taraması: C# + JS/TS, her PR/push + haftalık zamanlanmış tarama
```

### `pr.yml` — Pull Request kontrolü

Tetikleyiciler: `pull_request` (main'e) ve `push` (main'e — squash-merge sonrası tekrar
doğrulama için).

1. **`changes`** — `dorny/paths-filter` ile hangi alanların değiştiğine bakar
   (`src/**`+`tests/**` → backend, `web/**`+`packages/**` → web,
   `apps/senseik-mobile/**`+`packages/**` → mobile). Sadece değişen alanın pipeline'ı
   çalışır; ör. yalnızca `web/` değişen bir PR, mobile job'unu hiç başlatmaz.
2. **`backend`** — `dotnet restore` → `dotnet format --verify-no-changes` → `dotnet build
   -c Release` → **Architecture** testleri (fail-fast) → **Unit** testleri (coverage ile)
   → **Integration** testleri (testcontainers yerine servis konteynerleri: `postgres`,
   `redis` — bkz. Item 17). Herhangi bir adım başarısız olursa sonraki adımlar çalışmaz.
3. **`web`** — `pnpm install --frozen-lockfile` → `lint` → `type-check` → `test` (Vitest,
   henüz test yoksa `--passWithNoTests` ile geçer) → `build` (prod build doğrulaması).
4. **`mobile`** — `pnpm install --frozen-lockfile` → `lint` → `type-check` → `test`
   (Jest). **EAS native build burada YOK** — `eas.json` + Expo hesap secret'ları
   gerektirir; TestFlight/Play Store'a göndermeye hazır olunca ayrı, elle tetiklenen bir
   workflow olarak eklenmeli.
5. **`pr-status`** — `backend`/`web`/`mobile`'dan herhangi biri `failure` ise başarısız
   olan tek bir "toplayıcı" job. Branch protection'da **bunu** zorunlu status check
   yapın — path filtresine göre atlanan job'lar `pr-status`'u başarısız kılmaz, ama
   gerçekten çalışıp başarısız olanlar kılar.

### `main.yml` — main sonrası build & (opsiyonel) yayınlama

Tetikleyici: yalnızca `push` → `main` (yani PR merge sonrası). Path filtresi yok — her
merge'de tam paket çalışır. Backend/web/mobile testlerini tekrar koşar, sonra
`build-images` job'unda:

- `src/Sense.Api/Dockerfile` ve `web/Dockerfile` **her zaman** build edilir (bozuk bir
  Dockerfile'ı yakalamak için) — `docker/build-push-action` + GitHub Actions cache.
- Image'lar **yalnızca** repo değişkeni `ENABLE_IMAGE_PUSH` `"true"` olarak
  ayarlandıysa `ghcr.io/<owner>/<repo>-api` ve `ghcr.io/<owner>/<repo>-web` adlarıyla
  push edilir (etiketler: git SHA + `latest`). Varsayılan: push edilmez, yalnızca build
  edilip doğrulanır.
- **Deployment bu workflow'un kapsamında değil** — yalnızca build/test/publish var
  (Faz 0 kararı).

**Registry push'u açmak için** (hazır olunca):

1. Repo → Settings → Secrets and variables → Actions → **Variables** sekmesi.
2. `ENABLE_IMAGE_PUSH` adında bir repository variable ekleyin, değeri `true`.
3. Ekstra bir secret gerekmiyor: GHCR push'u, workflow'a otomatik sağlanan
   `GITHUB_TOKEN` ile (repo'nun `packages: write` izniyle) yapılır. Farklı bir registry
   kullanılacaksa (`docker.io`, özel bir registry vb.) `docker/login-action` adımını o
   registry'nin secret'larıyla (`secrets.REGISTRY_USERNAME` / `secrets.REGISTRY_PASSWORD`)
   güncelleyin.

### `codeql.yml` — güvenlik taraması

C# (`build-mode: manual`, `dotnet build Sense.slnx`) ve JavaScript/TypeScript
(`build-mode: none`) için CodeQL. Her PR/push'ta ve haftada bir (Pazartesi 03:23 UTC)
zamanlanmış olarak çalışır. Bulgular repo'nun **Security → Code scanning alerts**
sekmesinde görünür.

### `dependabot.yml`

NuGet (repo kökü, central package management), npm (`web/`, `apps/senseik-mobile/`,
repo kökü), `github-actions` ve Docker (`src/Sense.Api/`, `web/`) için haftalık bağımlılık
güncelleme PR'ları.

## Aynı kontrolleri lokalde çalıştırma

Push etmeden önce CI'nin çalıştıracağı komutların birebir aynısı:

```powershell
# Backend
dotnet restore Sense.slnx
dotnet format Sense.slnx --verify-no-changes
dotnet build Sense.slnx -c Release
dotnet test tests/Sense.Tests.Architecture/ -c Release
dotnet test Sense.slnx -c Release --filter "Category=Unit"
dotnet test Sense.slnx -c Release --filter "Category=Integration"   # postgres+redis local'de çalışıyor olmalı

# Web
pnpm --filter web lint
pnpm --filter web type-check
pnpm --filter web test -- --run --passWithNoTests
pnpm --filter web build

# Mobile
pnpm --filter @senseik/mobile lint
pnpm --filter @senseik/mobile type-check
pnpm --filter @senseik/mobile test
```

Commit mesajınızın commitlint'i geçip geçmediğini önceden görmek isterseniz:

```powershell
echo "feat(item-22): örnek mesaj" | pnpm exec commitlint
```

## Commit mesajı kuralları (commitlint + husky)

- `commitlint.config.js` → `@commitlint/config-conventional` (`feat:`, `fix:`, `docs:`,
  `chore:`, … — reponun mevcut git geçmişiyle aynı Conventional Commits kuralı,
  bkz. `docs/conventions.md`).
- `.husky/commit-msg` → her commit'te `commitlint --edit` çalıştırır; kural dışı bir
  mesaj commit'i reddeder.
- `.husky/pre-commit` → `lint-staged` çalıştırır (yalnızca **staged** dosyalar):
  - `web/**/*.{ts,tsx}` → `eslint --fix`
  - `apps/senseik-mobile/**/*.{ts,tsx}` → `eslint --fix`
  - `src/**/*.cs` → `dotnet format Sense.slnx --include`
- Hook'lar `pnpm install` sırasında otomatik kurulur (`"prepare": "husky"` script'i,
  root `package.json`). Manuel kurmak isterseniz: `pnpm exec husky`.

## Docker & local dev

### Dockerfile'lar

- `src/Sense.Api/Dockerfile` — çok aşamalı: `sdk:10.0` (build) → `aspnet:10.0` (runtime,
  non-root `app` kullanıcısı, `HEALTHCHECK` ile `/health`). **Build context repo
  köküdür** (modül projeleri ve `Directory.Build.props`/`Directory.Packages.props`
  gerektiğinden): `docker build -f src/Sense.Api/Dockerfile -t senseik-api .`
- `web/Dockerfile` — çok aşamalı: `node:20-alpine` (pnpm workspace install + build) →
  `nginx:alpine` (SPA fallback + gzip, bkz. `web/nginx.conf`). Build context da repo
  köküdür (`@senseik/api-client` workspace paketi gerektiğinden):
  `docker build -f web/Dockerfile -t senseik-web .`

### `docker-compose.yml` (repo kökü) — tam-stack local dev

`infra/docker-compose.yml`'i (`postgres`, `redis`, `minio`, `minio-init`, `mailpit`, +
opsiyonel `profiles: bus`/`obs` altında `rabbitmq`/`seq`/`jaeger`) `include:` ile yeniden
kullanır ve üzerine `api` + `web` container'larını ekler — hepsi aynı varsayılan bridge
network'ünde (`senseik_default`), servis adlarıyla birbirine erişir (`postgres`,
`redis`, `minio`, vb.).

```powershell
cp .env.example .env
docker compose up -d --build
dotnet run --project src/Sense.Migrator -- migrate
dotnet run --project src/Sense.Migrator -- seed --profile demo
# API:  http://localhost:8080/scalar/v1  (health: http://localhost:8080/health)
# Web:  http://localhost:3000
```

Yalnızca **altyapıyı** (postgres/redis/minio/mailpit) container'da çalıştırıp API/web'i
host üzerinde `dotnet watch` / `vite` ile hot-reload'lu geliştirmek isterseniz kök
README'deki hızlı başlangıcı kullanın: `docker compose -f infra/docker-compose.yml up -d`.

## Branch protection (repo admin — GitHub ayarlarından, koddan yapılamaz)

`main` için önerilen ayarlar (Settings → Branches → Branch protection rules):

- ✅ Require a pull request before merging (en az 1 onay)
- ✅ Require status checks to pass before merging:
  - `pr-status` (backend/web/mobile'ın toplayıcısı — path filtresine göre atlananları
    saymaz, gerçek başarısızlıkları sayar)
  - `changes`
  - `CodeQL / analyze (csharp)`, `CodeQL / analyze (javascript-typescript)`
- ✅ Require branches to be up to date before merging
- ✅ Require conversation resolution before merging
- ⬜ Do not allow bypassing the above settings (admin dahil — takım tercihine göre)

Bu ayarlar yalnızca GitHub repo ayarlarından yapılabilir; workflow dosyalarından
otomatik olarak uygulanamaz.

## Sonraki adımlar (kapsam dışı, bilinçli olarak ertelendi)

- **Deployment**: bu iterasyonda yok (Faz 0 kararı) — yalnızca build/test/publish.
- **EAS native build**: `eas.json` + Expo hesap secret'ları hazır olunca ayrı bir
  manuel-tetiklenen workflow olarak eklenmeli.
- **Registry push**: `ENABLE_IMAGE_PUSH` repo variable'ı ile açılana kadar kapalı.

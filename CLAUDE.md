# SenseİK — agent notes

HR/payroll SaaS. .NET 10 modular monolith (`src/Modules/*`, one schema + DbContext + migrations per module, modules talk only via `*.Contracts`), React web app (`web/`, Vite + react-router v7 + Mantine 9 + Tailwind v4 for layout; no shadcn/Radix — see `docs/implementation/mantine-gecis.md`), Expo mobile app (`apps/senseik-mobile`).

## Toolchain (use the SDK container for dotnet; node/pnpm work on the host)

Note: this host does have a native `dotnet` (`~/.dotnet`, 10.0.x) and the takım test gate (`test-kapisi.mjs`) runs it directly with the host locale (`LANG=en_US`), while the SDK container has no `LANG`. Tests that assert localized text must pin `CultureInfo.CurrentUICulture` themselves.

- .NET: `docker run --rm -v "$PWD":/src -v senseik-nuget:/root/.nuget/packages --add-host=host.docker.internal:host-gateway -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet <args>`. Do not pass `-p:UseAppHost=false` (xUnit v3 needs an app host). `scripts/dev/dotnet` wraps exactly this (plus the build-slot semaphore) — prefer it over typing the command by hand.
- Tests: no per-test Testcontainers container — all `.NET` tests share one Postgres server. Run `scripts/test-postgres.sh` first (starts/reuses the `senseik-test-postgres` `postgres:17-alpine` container on host port 55432, or reuses/restarts it if present; leaves the unrelated `lexparse_postgres` on 5432 alone) and export its `SENSE_TEST_POSTGRES` output (`eval "$(scripts/test-postgres.sh)"`). `TestFixture` (`tests/Sense.Tests.Shared/Fixtures`) opens one `sense_test_<guid>` database per test instance on that server and drops it in `DisposeAsync`; there is no Testcontainers fallback — an unset/blank `SENSE_TEST_POSTGRES` throws instead of starting a container. `scripts/dev/dotnet` forwards `SENSE_TEST_POSTGRES` into the SDK container, rewriting `Host=localhost`/`Host=127.0.0.1` to `Host=host.docker.internal` so the container reaches the host-published port (`TESTCONTAINERS_HOST_OVERRIDE`/the docker.sock mount are gone from the shim — nothing starts containers from inside it anymore). CI (`.github/workflows/main.yml`, `pr.yml`) instead points `SENSE_TEST_POSTGRES` at the job's existing `postgres` service container.
- Web: host `pnpm` works in `web/` — `pnpm exec tsc --noEmit`, `pnpm exec eslint src` (ESLint runs on the host now: `typescript` is aliased to `@typescript/typescript6` for tooling while `tsc` uses TypeScript 7 via `@typescript/native`), `pnpm exec vite build`. In Docker instead: `node:24-alpine`, `corepack enable` first; mount named volumes over `node_modules`, `web/node_modules`, `packages/api-client/node_modules`, `apps/senseik-mobile/node_modules` so host macOS builds don't mix in.
- Commits run husky hooks (lint-staged: ESLint on staged web/mobile files, `dotnet format Sense.slnx --include` on staged `.cs` files). The host has no dotnet, so commit with a PATH that provides a `dotnet` shim running the SDK container with the repo mounted at the same absolute path (lint-staged passes absolute paths) — `scripts/dev/dotnet` already is that shim: `PATH="$PWD/scripts/dev:$PATH" git commit ...`. It only runs `dotnet format` during commits, so `SENSE_TEST_POSTGRES` doesn't need to be set for that. Equivalent one-off form: `exec docker run --rm -v "$PWD":"$PWD" -v senseik-nuget:/root/.nuget/packages -w "$PWD" mcr.microsoft.com/dotnet/sdk:10.0 dotnet "$@"`.
- Migrations: generate real EF migrations per module (`dotnet-ef` inside the SDK container, design-time factories exist in each module's Infrastructure).

## Conventions that bite

- API JSON: camelCase properties, `JsonStringEnumConverter(CamelCase)` (C# enums serialize as `"active"`), nulls omitted (`WhenWritingNull`) — web code must treat missing as null.
- Register module handlers from the **Application** assembly in `AddModuleHandlers` (Contracts-assembly registration alone silently registers nothing; pass Domain/Contracts too so their domain/integration event types resolve in the outbox).
- Integration events: raise them with `IIntegrationEventOutbox.Enqueue(...)` inside a command handler — the row lands in the module's outbox in the command transaction, and `OutboxProcessor` publishes it to `IEventBus`.
- Error codes and permission names are localized in `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx`.
- New permission keys reach existing tenants via `SystemRolePermissionSynchronizer` at API startup; add keys to `SystemRoleDefinitions` include lists.

## Tracking and parallel work

- Roadmap: `docs/analysis/kolayik-video/11.7-yol-haritasi.md`. Status docs: `faz0-durum-takibi.md`, `faz1a-durum-takibi.md` in the same folder — update them whenever an item's status changes, verified against code.
- Single machine: all modules, web and mobile are developed on this machine (the PC-A/PC-B split in `docs/implementation/paralel-gelistirme-plani.md` is retired since 2026-09-24). Parallel sessions still share the working tree, so touch that plan's "hot files" (§5) append-only; branches `a/<topic>` (team cards: `takim/<topic>`); `git fetch && git rebase origin/main` daily.

<!-- takim-kur:basla -->
## Ajan takımı (/takim-kur)
Takım, akış ve "bitti" tanımı: `.claude/takim/TAKIM.md`. Kurallar `.claude/rules/takim-*.md` ile yüklenir.
<!-- takim-kur:bitis -->

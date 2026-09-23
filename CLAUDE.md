# SenseİK — agent notes

HR/payroll SaaS. .NET 10 modular monolith (`src/Modules/*`, one schema + DbContext + migrations per module, modules talk only via `*.Contracts`), React web app (`web/`, Vite + react-router v7 + Mantine 9 + Tailwind v4 for layout; no shadcn/Radix — see `docs/implementation/mantine-gecis.md`), Expo mobile app (`apps/senseik-mobile`).

## Toolchain (no dotnet on the dev hosts; node/pnpm work on the host)

- .NET: `docker run --rm -v "$PWD":/src -v senseik-nuget:/root/.nuget/packages -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal --add-host=host.docker.internal:host-gateway -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet <args>`. Do not pass `-p:UseAppHost=false` (xUnit v3 needs an app host). Testcontainers-based tests work this way.
- Web: host `pnpm` works in `web/` — `pnpm exec tsc --noEmit`, `pnpm exec eslint src` (ESLint runs on the host now: `typescript` is aliased to `@typescript/typescript6` for tooling while `tsc` uses TypeScript 7 via `@typescript/native`), `pnpm exec vite build`. In Docker instead: `node:24-alpine`, `corepack enable` first; mount named volumes over `node_modules`, `web/node_modules`, `packages/api-client/node_modules`, `apps/senseik-mobile/node_modules` so host macOS builds don't mix in.
- Commits run husky hooks (lint-staged: ESLint on staged web/mobile files, `dotnet format Sense.slnx --include` on staged `.cs` files). The host has no dotnet, so commit with a PATH that provides a `dotnet` shim running the SDK container with the repo mounted at the same absolute path (lint-staged passes absolute paths): `exec docker run --rm -v "$PWD":"$PWD" -v senseik-nuget:/root/.nuget/packages -w "$PWD" mcr.microsoft.com/dotnet/sdk:10.0 dotnet "$@"`.
- Migrations: generate real EF migrations per module (`dotnet-ef` inside the SDK container, design-time factories exist in each module's Infrastructure).

## Conventions that bite

- API JSON: camelCase properties, `JsonStringEnumConverter(CamelCase)` (C# enums serialize as `"active"`), nulls omitted (`WhenWritingNull`) — web code must treat missing as null.
- Register module handlers from the **Application** assembly in `AddModuleHandlers` (Contracts-assembly registration alone silently registers nothing; pass Domain/Contracts too so their domain/integration event types resolve in the outbox).
- Integration events: raise them with `IIntegrationEventOutbox.Enqueue(...)` inside a command handler — the row lands in the module's outbox in the command transaction, and `OutboxProcessor` publishes it to `IEventBus`.
- Error codes and permission names are localized in `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx`.
- New permission keys reach existing tenants via `SystemRolePermissionSynchronizer` at API startup; add keys to `SystemRoleDefinitions` include lists.

## Tracking and parallel work

- Roadmap: `docs/analysis/kolayik-video/11.7-yol-haritasi.md`. Status docs: `faz0-durum-takibi.md`, `faz1a-durum-takibi.md` in the same folder — update them whenever an item's status changes, verified against code.
- Two machines work in parallel per `docs/implementation/paralel-gelistirme-plani.md`: PC-A owns Identity/Organization/Employee/Documents/DataTransfer, `src/Shared/*`, web shell/settings/employee pages; PC-B owns Workflow/Notifications/Leave. Touch "hot files" (§5) append-only; branches `a/<topic>` / `b/<module>-<topic>`; `git fetch && git rebase origin/main` daily.

<!-- takim-kur:basla -->
## Ajan takımı (/takim-kur)
Takım, akış ve "bitti" tanımı: `.claude/takim/TAKIM.md`. Kurallar `.claude/rules/takim-*.md` ile yüklenir.
<!-- takim-kur:bitis -->

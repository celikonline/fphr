# SenseİK — agent notes

HR/payroll SaaS. .NET 10 modular monolith (`src/Modules/*`, one schema + DbContext + migrations per module, modules talk only via `*.Contracts`), React web app (`web/`, Vite + react-router v7 + Tailwind v4), Expo mobile app (`apps/senseik-mobile`).

## Toolchain (no dotnet/node on the dev hosts)

- .NET: `docker run --rm -v "$PWD":/src -v senseik-nuget:/root/.nuget/packages -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal --add-host=host.docker.internal:host-gateway -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet <args>`. Do not pass `-p:UseAppHost=false` (xUnit v3 needs an app host). Testcontainers-based tests work this way.
- Web: `node:24-alpine`, `corepack enable` first; mount named volumes over `node_modules`, `web/node_modules`, `packages/api-client/node_modules`, `apps/senseik-mobile/node_modules` so host macOS builds don't mix in. Checks: `pnpm exec tsc --noEmit`, `pnpm exec vite build`.
- Migrations: generate real EF migrations per module (`dotnet-ef` inside the SDK container, design-time factories exist in each module's Infrastructure).

## Conventions that bite

- API JSON: camelCase properties, `JsonStringEnumConverter(CamelCase)` (C# enums serialize as `"active"`), nulls omitted (`WhenWritingNull`) — web code must treat missing as null.
- Register module handlers from the **Application** assembly in `AddModuleHandlers` (Contracts-assembly registration silently registers nothing).
- Error codes and permission names are localized in `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx`.
- New permission keys reach existing tenants via `SystemRolePermissionSynchronizer` at API startup; add keys to `SystemRoleDefinitions` include lists.

## Tracking and parallel work

- Roadmap: `docs/analysis/kolayik-video/11.7-yol-haritasi.md`. Status docs: `faz0-durum-takibi.md`, `faz1a-durum-takibi.md` in the same folder — update them whenever an item's status changes, verified against code.
- Two machines work in parallel per `docs/implementation/paralel-gelistirme-plani.md`: PC-A owns Identity/Organization/Employee/Documents/DataTransfer, `src/Shared/*`, web shell/settings/employee pages; PC-B owns Workflow/Notifications/Leave. Touch "hot files" (§5) append-only; branches `a/<topic>` / `b/<module>-<topic>`; `git fetch && git rebase origin/main` daily.

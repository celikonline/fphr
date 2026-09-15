# SenseİK

[![PR](https://github.com/algosense-yazilim/senseik/actions/workflows/pr.yml/badge.svg)](https://github.com/algosense-yazilim/senseik/actions/workflows/pr.yml)
[![Main](https://github.com/algosense-yazilim/senseik/actions/workflows/main.yml/badge.svg)](https://github.com/algosense-yazilim/senseik/actions/workflows/main.yml)
[![CodeQL](https://github.com/algosense-yazilim/senseik/actions/workflows/codeql.yml/badge.svg)](https://github.com/algosense-yazilim/senseik/actions/workflows/codeql.yml)

Algosense'in bulut tabanlı İnsan Kaynakları platformu: özlük, izin & fazla mesai, PDKS/vardiya/puantaj, bordro, zimmet, harcama & avans, eğitim, dosya & KVKK, duyuru & akış, performans, ücret değerlendirme, işe alım (ATS), İK analitiği & raporlama, AI asistan (chatbot + MCP), faturalama.

**Stack:** .NET 10 (modüler monolit, Onion Architecture, EF Core, PostgreSQL) · React 18 + Vite + TypeScript (PWA) · Expo (mobil) · Redis · MinIO/S3 · Hangfire · SignalR · RabbitMQ (Faz 4+).

## Hızlı başlangıç

```powershell
# Ön koşullar: .NET 10 SDK, Node 22+, pnpm, Docker Desktop (WSL2)
cp .env.example .env
docker compose -f infra/docker-compose.yml up -d
dotnet tool restore
dotnet run --project src/Sense.Migrator -- migrate
dotnet run --project src/Sense.Migrator -- seed --profile demo
dotnet watch --project src/Sense.Api          # https://localhost:5001/scalar/v1
dotnet run --project src/Sense.Worker
pnpm install && pnpm --filter web dev         # http://localhost:3000
pnpm --filter @senseik/mobile start           # Expo (mobil, apps/senseik-mobile/)
```

Demo kullanıcılar (`seed --profile demo`): `owner@demo.local / Demo123!`, `ik@demo.local`, `bordro@demo.local`, `yonetici@demo.local`, `calisan@demo.local`, `denetci@demo.local`.

## Yapı

```
src/Sense.Api          Host (Minimal API, SignalR, OpenAPI+Scalar, /mcp)
src/Sense.Worker       Hangfire + outbox işleyici
src/Sense.Migrator     migrate | seed | reset
src/Shared/            Kernel · Contracts · Infrastructure · Web
src/Modules/<M>/       Domain · Application · Infrastructure · Contracts · Api
tests/                 Shared · Architecture · Contract · Integration · TenantIsolation · Modules/*
web/                   React + Vite PWA
apps/senseik-mobile/   Expo (React Native)
packages/              Paylaşılan pnpm paketleri (ör. @senseik/api-client)
infra/                 docker-compose, k6, bicep
docs/adr               Mimari kararlar
```

Ayrıntılar: `docs/architecture.md`, `docs/conventions.md`, `docs/ci-cd.md`, `docs/observability.md`, `docs/adr/`. Yeni modül iskeleti için `./build/new-module.ps1 -Name <Modül>`.

## Bilinen sınırlama (Faz 0)

`dotnet run --project src/Sense.Migrator -- migrate` şu an yalnızca `identity` şemasını
migrate eder (`src/Sense.Migrator/Program.cs`'de yalnızca `IdentityDbContext` kayıtlı).
Documents modülünün henüz üretilmiş bir EF Core migration'ı da yok. Yeni bir modülün
şemasını migrator'a dahil etmek için modülün `AddModuleDbContext<...>` çağrısını
`Program.cs`'e eklemeniz gerekir — `./build/new-module.ps1` bunu "Next steps" çıktısında
hatırlatır.

# ADR 0002 — Onion Architecture

**Durum:** Kabul edildi — 2026-09-14

## Karar
Her modül dört halkadan oluşur; bağımlılık yalnızca içe doğrudur:

| Halka | Proje | Kural |
|---|---|---|
| Domain Model + Domain Services | `Sense.Modules.X.Domain` | Yalnız `Sense.Shared.Kernel`; hiçbir NuGet |
| Application | `Sense.Modules.X.Application` | Domain + Contracts; **portları** (arayüzleri) tanımlar; Infrastructure'a referans yok |
| Infrastructure | `Sense.Modules.X.Infrastructure` | Application portlarını implement eder (EF, Hangfire, dış servisler) |
| Presentation | `Sense.Modules.X.Api` | Endpoint'ler + `XModule : IModule` kompozisyon kökü (Infrastructure'ı yalnız DI kaydı için görür) |

`Sense.Tests.Architecture` bu kuralları her PR'da doğrular.

## Gerekçe
İş kuralları (bordro hesabı, izin bakiyesi, onay motoru) altyapıdan bağımsız, saf birim testlerle doğrulanabilir olmalı. Portlar Application'da tanımlandığı için sağlayıcılar (S3 ↔ Azure Blob, Anthropic ↔ başka LLM, SMTP ↔ Resend) kod değişmeden değiştirilebilir.

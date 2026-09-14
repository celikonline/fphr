# ADR 0004 — Kendi hafif Command/Query dispatcher'ı

**Durum:** Kabul edildi — 2026-09-14

## Karar
MediatR (v13 ticari lisans) yerine `Sense.Shared.Contracts` içinde `ICommand<T>`, `IQuery<T>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `IPipelineBehaviour<,>` ve `IDispatcher`; implementasyon `Sense.Shared.Infrastructure`. Pipeline sırası: Logging → Tracing → Validation (FluentValidation) → Authorization (permission + veri kapsamı) → UnitOfWork (yalnız command) → Caching (yalnız `ICachedQuery`) → Handler. Handler kaydı Scrutor ile assembly taraması.

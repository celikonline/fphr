# SenseİK Mimari Özeti

Ayrıntılı plan ve modül tasarımları ürün planında; kararlar `docs/adr/` altında. Bu belge kodu okuyanlar için kısa haritadır.

## Katmanlar (Onion — ADR 0002)

```
Sense.Api (host)  →  Modules.X.Api (controller + IModule)  →  Modules.X.Application (use case, port)  →  Modules.X.Domain (aggregate, kural)
                                            ↘ Modules.X.Infrastructure (EF, adaptörler; Application portlarını implement eder)
Shared: Kernel (domain çekirdek) · Contracts (portlar, PagedQuery, ITenantContext, Permission) · Infrastructure (ModuleDbContext, dispatcher, outbox, cache, e-posta) · Web (controller tabanı, ProblemDetails, izin policy'si, middleware, binder)
```

Kurallar `Sense.Tests.Architecture` ile zorlanır: Domain'de NuGet yok; Application, Infrastructure/Api'yi görmez; modüller arası yalnız `*.Contracts`.

## İstek akışı

1. `RequestContextMiddleware`: JWT claim'lerinden `ICurrentUser` + kiracı (`tid`) bağlamı.
2. Controller (`ApiControllerBase`) → `IDispatcher` → pipeline: Logging → Tracing → Validation → Authorization (`[RequiresPermission]`, veri kapsamı) → UnitOfWork (yalnız command) → Caching (yalnız `ICachedQuery`) → handler.
3. Handler `Result`/`Result<T>` döner; hata **kodu** taşır (ör. `user.email_taken`). Sunum katmanı `IErrorLocalizer` ile kültüre göre mesaj üretir (`Resources/SharedResource*.resx`).
4. `AuditTenantInterceptor`: `CreatedAt/CreatedUserId/ModifiedDate/ModifiedUserId`, `TenantId`, soft delete, domain event → outbox (aynı transaction).
5. `Sense.Worker` outbox'ı işler: domain event handler'ları (modül içi) ve integration event'ler (`IEventBus`).

## Yerelleştirme

- Varsayılan `tr-TR`, ikincil `en-US` (`Localization` ayarı, `Accept-Language`).
- Kullanıcıya dönen her metin kaynak anahtarıdır: hata kodları, ProblemDetails başlıkları, sistem rolü adları (`roles.{code}.name`), izin adları, e-posta şablonları.
- Kodda literal metin/sayı yok: sabitler `*Defaults`/`*Constants` sınıflarında, ayarlar `IOptions<…>` ile appsettings'te.

## Grid standardı

`?page&pageSize&sort=-createdAt,lastName&q=…&status=active&createdAt.gte=2026-01-01&export=xlsx` → `PagedQuery` (model binder) → `GridFieldMap` beyaz listesi ile `ApplyFilters/ApplySort/ToPagedResultAsync` → `PagedResult<T>`.

## Arka plan işleri ve gerçek zamanlı bildirim (Item 16)

Hangfire (PostgreSQL storage) zamanlanmış/kuyruklu işler için; SignalR (`/hubs/notifications`, Redis backplane ile çoklu sunucu ölçekleme) gerçek zamanlı bildirim için. `BackgroundJobExecutor` her işte kiracı bağlamını (`ITenantContext`) yeniden kurar ve kendi correlation id'sini üretir. Ayrıntı: `docs/implementation/item-16-background-jobs-and-signalr.md`.

## Dosya depolama (Item 15, Documents modülü)

`Sense.Shared.Contracts.Storage.IFileStorage` portu, MinIO/S3 uyumlu adaptörle (presigned upload/download URL) implement edilir. Documents modülü akışı: `InitiateUploadCommand` (presigned URL üretir, `Document` agregasını `Pending` durumunda oluşturur) → istemci dosyayı doğrudan storage'a yükler → `ConfirmUploadCommand` (ETag doğrulanır, durum güncellenir) → `DeleteDocumentCommand`. Kiracı izolasyonu bucket/key önekiyle sağlanır; dosya **içeriği** hiçbir zaman loglanmaz (bkz. `docs/observability.md`).

## Gözlemlenebilirlik (Item 23)

Serilog (Console/Seq) + OpenTelemetry (ASP.NET Core/HttpClient/Npgsql + özel `Sense.Dispatcher`/`Sense.Documents`/`Sense.BackgroundJobs` ActivitySource'ları → Jaeger) + Sentry (500+ hatalar) + sağlık kontrolleri (`/health`, `/health/live`, `/health/ready`: postgres/redis/minio/hangfire). Her log/iz `TenantId`/`UserId`/`CorrelationId` taşır. Ayrıntı: `docs/observability.md`.

## Yerel ortam

İki seçenek:

- **Docker** (önerilen, bkz. kök `README.md` ve `docs/ci-cd.md`): `infra/docker-compose.yml` → postgres/redis/minio/mailpit (+ `obs` profili: jaeger/seq); kök `docker-compose.yml` isteğe bağlı olarak `api`/`web`'i de container'da çalıştırır.
- **Docker'sız**: PostgreSQL 18 Windows servisi (`postgres/postgres`, DB `senseik`, `senseik_test`), Redis Windows derlemesi (`build/local-services.ps1 start`), dosya depolama yerel klasör, e-posta için Mailpit yerine `Email:Provider=None` veya yerel SMTP.

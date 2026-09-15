# Gözlemlenebilirlik (Item 23)

Serilog (yapılandırılmış log) → Console/Seq, OpenTelemetry (dağıtık iz) → Jaeger, Sentry (hata izleme) ve
sağlık kontrolleri. Her log/iz satırı — mümkün olduğunda — `TenantId`, `UserId`/`UserEmail` ve `CorrelationId`
taşır; kiracılar arası veri sızıntısını önlemek için hiçbir yerde belge/kayıt **içeriği** loglanmaz, yalnızca
kimlikler ve metadata.

## Mimari özet

```
İstek → CorrelationIdMiddleware (en erken) → UseExceptionHandler → UseSerilogRequestLogging
      → UseAuthentication → RequestContextMiddleware (ICurrentUser + kiracı bağlamı) → UseAuthorization → Controller
                                                                                                              ↓
                                                                                          IDispatcher pipeline (Logging → Tracing → …)

Serilog:  Enrich.FromLogContext + TenantEnricher + CorrelationIdEnricher + UserEnricher → Console (+ Seq, appsettings ile)
OpenTelemetry: AspNetCore + HttpClient + Npgsql + özel ActivitySource'lar (Sense.Dispatcher, Sense.Documents, Sense.BackgroundJobs) → OTLP/gRPC → Jaeger
Sentry: GlobalExceptionHandler 500+ hataları SentrySdk.CaptureException ile açıkça bildirir (DSN yoksa no-op)
Health: /health (tam rapor), /health/live (süreç ayakta mı), /health/ready (postgres/redis/minio/hangfire hazır mı)
```

## Correlation ID

- `CorrelationIdMiddleware` (`src/Sense.Api/Middleware/CorrelationIdMiddleware.cs`) pipeline'ın en başında çalışır:
  `X-Correlation-Id` isteği okur, yoksa yeni bir GUID üretir, `HttpContext.Items` ve `ICorrelationIdContextSetter`
  (AsyncLocal, `Sense.Shared.Infrastructure.Observability.CorrelationIdContext`) içine yazar, yanıt başlığına ekler.
- `RequestContextMiddleware` artık kendi correlation id'sini üretmez; `HttpContext.Items` üzerinden bu değeri okur
  (yoksa `TraceIdentifier`'a düşer — ör. middleware eklenmeden çalışan testler için).
- Arka plan işlerinde (Hangfire) `BackgroundJobExecutor` her iş için kendi correlation id'sini (`job-{guid}`) üretir:
  işi tetikleyen isteğin correlation id'si iş kuyruğuna taşınmadığından (Item 16 job payload'ı bunu içermiyor),
  her job bağımsız ama kendi içinde uçtan uca izlenebilir bir kimlik alır — validasyon kriterindeki "standalone
  trace with job context" seçeneği.
- İstemci tarafı: bir isteği izlemek için `X-Correlation-Id` başlığını siz belirleyin (aksi halde sunucu üretir);
  yanıt başlığında her zaman geri döner.

## Serilog enricher'ları

`src/Shared/Sense.Shared.Infrastructure/Observability/`:

- **TenantEnricher**: `TenantId` — `TenantContext`'in statik AsyncLocal'inden okur (DI kapsamına ihtiyaç duymaz,
  bu yüzden hem HTTP isteğinde hem arka plan işlerinde çalışır).
- **CorrelationIdEnricher**: `CorrelationId` — yukarıdaki `CorrelationIdContext`'ten okur.
- **UserEnricher**: `UserId`, `UserEmail` — `IHttpContextAccessor` üzerinden mevcut isteğin JWT claim'lerinden
  (`sub`, `email`) okur; arka plan işlerinde HttpContext olmadığından sessizce hiçbir şey eklemez.

Kayıt (`src/Sense.Api/Program.cs`):

```csharp
builder.Host.UseSerilog((context, services, cfg) => cfg
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With<TenantEnricher>()
    .Enrich.With<CorrelationIdEnricher>()
    .Enrich.With(new UserEnricher(services.GetRequiredService<IHttpContextAccessor>()))
    .Enrich.WithProperty(...));
```

`TenantEnricher`/`CorrelationIdEnricher` parametresiz kurulur çünkü Serilog'un `Enrich.With<T>()` çağrısı DI
kullanmaz (yalnızca `Activator.CreateInstance`); bu yüzden ikisi de DI yerine statik AsyncLocal state'e dayanır
(`TenantContext`/`CorrelationIdContext` ile aynı desen — bkz. `TenantContext.CurrentTenantIdOrNull`).
`UserEnricher` ise mevcut isteğin `IHttpContextAccessor`'ını (tekil/singleton, root provider'dan güvenle
çözümlenebilir) kurucusunda alır.

## Yapılandırılmış log kuralları

- **Trace/Debug**: geliştirme sırasında ayrıntı; production'da kapalı (`appsettings.Development.json`'da `Debug`).
- **Information**: iş olayları — "Document {DocumentId} uploaded by {UserId} for tenant {TenantId}" gibi; **asla**
  string interpolation (`$"..."`) kullanılmaz, her zaman adlandırılmış yer tutucular (`{DocumentId}`) — Serilog'un
  yapılandırılmış (structured) sorgulanabilir alanlar üretmesi için.
- **Warning**: kurtarılabilir durumlar (ETag uyuşmazlığı, 4xx, >1s isteler — bkz. `UseSerilogRequestLogging`'in
  özel `GetLevel` yapılandırması).
- **Error**: yakalanan istisnalar, 5xx.
- **Fatal**: uygulama başlatılamıyor (`Program.cs`'teki dış `catch`).
- **PII**: parola, token, tam kart numarası gibi veriler asla loglanmaz; belge/kayıt işlemlerinde yalnızca
  kimlik (`DocumentId`, `TenantId`, `BucketName`, `Key`) loglanır/etiketlenir — asla dosya içeriği.

## OpenTelemetry ve Jaeger

`src/Sense.Api/Observability/OpenTelemetryExtensions.cs` (`AddSenseObservability`):

- **Servis adı**: `Observability:ServiceName` (varsayılan `SenseIK.Api`); kaynak öznitelikleri: `service.name`,
  `service.version` (derleme sürümü), `deployment.environment` (`ASPNETCORE_ENVIRONMENT`).
- **Tracing enstrümantasyonu**: ASP.NET Core (gelen istekler; `/health*` filtrelenir — polling gürültü yapmasın),
  HttpClient (giden çağrılar), Npgsql (`Npgsql.OpenTelemetry` paketi — EF Core sorguları Npgsql'in kendi
  ActivitySource'u üzerinden görünür; ayrı bir `OpenTelemetry.Instrumentation.EntityFrameworkCore` paketine
  gerek yoktur), artı özel kaynaklar: `Sense.Dispatcher` (her command/query, `TracingBehaviour`),
  `Sense.Documents` (dosya yükleme/indirme, `MinioAdapter`), `Sense.BackgroundJobs` (Hangfire job'ları,
  `BackgroundJobExecutor`).
- **Metrics**: ASP.NET Core + HttpClient + .NET runtime (GC, thread pool) sayaçları.
- **Exporter**: OTLP/gRPC (`Observability:OtlpEndpoint`, varsayılan `http://localhost:4317`) — Jaeger'in kendi
  (deprecated) native exporter paketi yerine, Jaeger'in yerleşik OTLP alıcısına gönderilir.
- **İzin kontrolleri**: `AuthorizationBehaviour`, her izin kararını mevcut isteğin span'ine olay (`ActivityEvent`,
  `sense.permission_check`) olarak ekler — ayrı bir span açmaz.

Jaeger'i local'de çalıştırma (`infra/docker-compose.yml`, `obs` profili — Seq ile birlikte):

```powershell
docker compose -f infra/docker-compose.yml --profile obs up -d jaeger seq
```

Jaeger UI: http://localhost:16686 → servis adı `SenseIK.Api` → API'ye birkaç istek atın → "Find Traces".
EF Core sorguları, dispatcher span'inin altında çocuk span olarak görünür (Npgsql enstrümantasyonu sayesinde).

## Sentry

`src/Sense.Api/Observability/SentryExtensions.cs` (`AddSenseSentry`):

- **DSN**: `SENTRY_DSN` ortam değişkeni (öncelikli) veya `Observability:SentryDsn` appsettings anahtarı. **İkisi
  de boşsa Sentry hiç başlatılmaz** — local geliştirmede varsayılan budur, dev hatalarının prod Sentry projesine
  sızmasını önler. Etkinleştirmek için: `.env`'de `SENTRY_DSN=https://...@sentry.io/...` (bkz. `.env.example`).
- **Örnekleme**: hatalar her zaman %100 yakalanır; performans izleme (trace) örneklemesi
  `Observability:SentryTracesSampleRate` (varsayılan 0.1) ile ayarlanır.
- **Zenginleştirme**: `SetBeforeSend` her olaya `tenant_id` ve `correlation_id` etiketi ekler (kullanıcı/belge
  içeriği asla eklenmez — `SendDefaultPii = false`).
- **Yakalama noktası**: `GlobalExceptionHandler` (Sense.Shared.Web), 500+ durumlarda `SentrySdk.CaptureException`
  çağırır. Bunun ayrı bir açık çağrı olarak eklenmesinin nedeni: `UseExceptionHandler` istisnayı tamamen yutar
  (pipeline'a yeniden fırlatmaz), bu yüzden Sentry'nin kendi ASP.NET Core middleware'i bu istisnayı hiç göremez.
- **Test**: bir controller'da kasıtlı `throw new InvalidOperationException("test")` ekleyip isteği çağırın;
  Sentry projenizde olayın `tenant_id`/`correlation_id` etiketleriyle göründüğünü doğrulayın.

## Sağlık kontrolleri

`src/Sense.Api/HealthChecks/HealthCheckExtensions.cs`:

| Uç nokta | Amaç | Kontroller |
|---|---|---|
| `/health` | Tam rapor (JSON: durum, süre, her kontrolün detayı) | hepsi |
| `/health/live` | Liveness — sürecin istek karşılayabildiğini doğrular, dış bağımlılık kontrolü yapmaz | yok |
| `/health/ready` | Readiness — dış bağımlılıklar hazır mı | `ready` etiketli olanlar (aşağıdaki 4'ü) |

Kontroller (hepsi `ready` etiketli):

- **postgres**: `AspNetCore.HealthChecks.NpgSql` — `ConnectionStrings:Database`.
- **redis**: `AspNetCore.HealthChecks.Redis` — `ConnectionStrings:Redis`.
- **minio**: özel `MinioHealthCheck` — MinIO SDK'sinin S3 API'sinden (kimlik doğrulama gerektirir, sürüme
  bağımlıdır) bağımsız olarak, MinIO'nun kendi native `/minio/health/live` uç noktasını `HttpClient` ile sorar.
- **hangfire**: `AspNetCore.HealthChecks.Hangfire` — en az bir sunucunun (worker) ayakta olduğunu doğrular.

Yanıt örneği (`/health`):

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.4,
  "entries": [
    { "name": "postgres", "status": "Healthy", "description": null, "durationMs": 3.1, "tags": ["ready"] }
  ]
}
```

## Sorun giderme

- **Jaeger'de iz görünmüyor**: `Observability:EnableTracing=true` mu? `docker compose --profile obs` ile Jaeger
  ayakta mı (`docker ps`)? OTLP portu (4317) erişilebilir mi? `Observability:OtlpEndpoint` doğru mu (container
  dışından çalışıyorsanız `localhost:4317`, API de container içindeyse servis adı `jaeger:4317`)?
- **Sentry hata almıyor**: `SENTRY_DSN` gerçekten ayarlı mı (`echo $env:SENTRY_DSN`)? Hata 500 (Unhandled) seviyesinde
  mi (yalnızca 500+ `SentrySdk.CaptureException` çağrılır, 4xx için değil)? `SentryOptions.Debug` (Development'ta
  otomatik açık) konsol loglarında SDK'nın DSN'i kabul edip etmediğini gösterir.
- **Loglar TenantId/CorrelationId içermiyor**: enricher'lar yalnızca bağlam kurulduysa (kimliği doğrulanmış istek,
  ya da job içi) property ekler; anonim/health-check isteklerinde bu alanlar beklenen şekilde yoktur.
- **`/health` 503 dönüyor ama uygulama çalışıyor**: hangi kontrolün `Unhealthy` olduğunu görmek için `/health`
  yanıtındaki `entries` dizisine bakın (`description` alanı hatayı özetler).

## Geçmiş not

Item 23 sırasında `Sense.Shared.Infrastructure`'ın floating NuGet sürüm kaymasından (Minio/Hangfire.PostgreSql/
Npgsql API değişiklikleri) kaynaklanan üç ayrı, gözlemlenebilirlikle ilgisiz build hatası tespit edilmişti; bunlar
Item 23'ün kapsamı dışında bırakılmıştı. Sonraki bir düzeltmeyle (bkz. `git log --grep "NuGet API drift"`) giderildi
— `dotnet build Sense.slnx` ve `dotnet test tests/Sense.Tests.Architecture/` şu an temiz geçiyor.

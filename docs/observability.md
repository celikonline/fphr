# Gözlemlenebilirlik (Item 23)

Serilog (yapılandırılmış log) → Console/Seq/**Loki**, OpenTelemetry (dağıtık iz) → **Jaeger**, OpenTelemetry metrikleri →
**Prometheus**, hepsi **Grafana**'da tek yerden (hazır dashboard'larla), Sentry (hata izleme) ve sağlık kontrolleri. Her log/iz satırı — mümkün olduğunda — `TenantId`, `UserId`/`UserEmail` ve `CorrelationId`
taşır; kiracılar arası veri sızıntısını önlemek için hiçbir yerde belge/kayıt **içeriği** loglanmaz, yalnızca
kimlikler ve metadata.

## Mimari özet

```
İstek → CorrelationIdMiddleware (en erken) → ClientContextMiddleware → SecurityHeadersMiddleware → (UseHsts, prod)
      → UseExceptionHandler → UseSerilogRequestLogging
      → UseAuthentication → RequestContextMiddleware (ICurrentUser + kiracı bağlamı) → UseAuthorization → Controller
                                                                                                              ↓
                                                                                          IDispatcher pipeline (Logging → Tracing → …)

Serilog:  Enrich.FromLogContext + TenantEnricher + CorrelationIdEnricher + UserEnricher → Console (+ Seq, appsettings ile)
          + Observability:Logs:Enabled ise OTLP/HTTP → Loki (/otlp/v1/logs)
OpenTelemetry izleri: AspNetCore + HttpClient + Npgsql + SignalR + özel ActivitySource'lar (Sense.Dispatcher, Sense.Documents, Sense.BackgroundJobs) → OTLP/gRPC → Jaeger
OpenTelemetry metrikleri: AspNetCore/Kestrel + HttpClient + System.Runtime + Npgsql + AuthN/AuthZ + Sense meter → /metrics ← Prometheus scrape
Grafana: Prometheus + Loki + Jaeger datasource'ları, 4 hazır dashboard; log ↔ iz arasında trace_id ile tek tık geçiş
Sentry: GlobalExceptionHandler 500+ hataları SentrySdk.CaptureException ile açıkça bildirir (DSN yoksa no-op)
Health: /health (tam rapor), /health/live (süreç ayakta mı), /health/ready (postgres/redis/minio/hangfire hazır mı)
```

## Local observability stack (Prometheus · Loki · Grafana · Jaeger)

```bash
docker compose -f infra/docker-compose.yml --profile obs up -d     # jaeger, prometheus, loki, grafana (+ seq)
dotnet run --project src/Sense.Api                                  # Development: Logs:Enabled=true
# veya tam container stack: docker compose --profile obs up -d --build
```

| Araç | Adres | Not |
|---|---|---|
| Grafana | http://localhost:13001 | `admin` / `admin` (`GRAFANA_ADMIN_*`); ana sayfa = API Genel Bakış |
| Prometheus | http://localhost:19090 | Status → Targets, Alerts |
| Jaeger | http://localhost:26686 | servis `SenseIK.Api` |
| Loki | http://localhost:13100 | yalnızca API; Grafana → Explore → Loki |
| API metrikleri | `GET /metrics` | Prometheus metin formatı |

Yapılandırma dosyaları `infra/observability/` altındadır:

```
prometheus/prometheus.yml          scrape: api:8080 (container) + host.docker.internal:5001 (dotnet run, https)
prometheus/rules/senseik-api.yml   alarmlar: SenseApiDown, HighErrorRate (>%5 5xx), HighLatencyP95 (>1 sn),
                                   SenseOutboxDeadLetters, SenseBackgroundJobFailures
loki/loki.yml                      tek süreç, dosya sistemi, 7 gün saklama, structured metadata açık
grafana/provisioning/datasources   Prometheus (uid prometheus), Loki (uid loki), Jaeger (uid jaeger)
grafana/dashboards/*.json          dashboard'lar (UI'da düzenleyip JSON'u buraya geri kaydedin)
```

> Jaeger `2.20.0`'a sabitlenmiştir: 2.21.0 eski `/api/services|traces` HTTP API'sini kaldırdı ve Grafana'nın Jaeger
> datasource'u bu API'yi kullanıyor. Yükseltmeden önce Grafana → Connections → Jaeger → *Save & test* ile doğrulayın.

### Grafana dashboard'ları ("SenseİK" klasörü)

| Dashboard | İçerik |
|---|---|
| **API Genel Bakış** | RED: istek/sn, 5xx/4xx oranı, p50–p99 gecikme, en yoğun/en yavaş rotalar, hata ve exception kırılımı, rota özet tablosu, giden HTTP çağrıları, kimlik doğrulama sonuçları |
| **Uygulama (Command / Query / Job / Outbox)** | Modül bazında command/query hacmi, başarısızlık/exception oranı, en yavaş ve en çok başarısız istekler, Hangfire iş çalıştırma/süre, outbox processed/retried/dead-letter, SignalR bağlantıları |
| **.NET Runtime ve Veritabanı** | CPU, bellek, GC heap/nesiller/duraklama, tahsis hızı, thread pool, exception tipleri, Npgsql havuzu ve sorgu süresi, Kestrel bağlantıları |
| **Loglar** | Seviye/correlation id/tenant/user/istemci/serbest metin filtreleri; seviye bazlı hacim, en sık hata şablonları, kiracı ve istemci kırılımı, hata ve yavaş/5xx istek akışları |

### Metrikler (Prometheus)

`AddSenseObservability` metrikleri OTLP yerine `OpenTelemetry.Exporter.Prometheus.AspNetCore` ile `/metrics`
(`Observability:MetricsPath`) üzerinden sunar (pull modeli; Jaeger metrik kabul etmez). Paket upstream'de hâlâ yalnızca
beta yayımlanıyor (`Directory.Packages.props`: `1.*-beta.*`). `/metrics` ve `/health*` istekleri iz ve istek logu
gürültüsünden hariç tutulur. **Üretimde** `/metrics` yalnızca iç ağdan (ingress/NetworkPolicy) erişilebilir olmalıdır.

Özel uygulama metrikleri (`Sense.Shared.Infrastructure/Observability/SenseMeters.cs`, meter `Sense`):

| Prometheus adı | Tip | Etiketler | Kaynak |
|---|---|---|---|
| `sense_dispatcher_request_duration_seconds` | histogram | `sense_request`, `sense_request_kind` (command/query), `sense_module`, `sense_outcome` (success/failure/exception) | `TracingBehaviour` |
| `sense_background_job_duration_seconds` | histogram | `sense_job_type`, `sense_outcome` | `BackgroundJobExecutor` |
| `sense_outbox_messages_total` | counter | `sense_outbox_context`, `sense_outbox_result` (processed/retried/dead_lettered) | `OutboxProcessor` |

Kural: metrik etiketleri düşük kardinaliteli olmalı — **kiracı, kullanıcı, kayıt kimliği asla metrik etiketi olmaz**
(bunlar loglarda ve izlerde aranır).

### Loglar (Loki)

`LogExportExtensions.WriteToSenseLogExport`, `Observability:Logs:Enabled=true` ise (Development'ta açık, container
stack'te `Observability__Logs__Enabled`) Serilog'a `Serilog.Sinks.OpenTelemetry` ekler ve OTLP/HTTP ile
`Observability:Logs:Endpoint`'e (varsayılan `http://localhost:13100/otlp/v1/logs`) gönderir.

- Index etiketleri (resource attribute): `service_name`, `deployment_environment`.
- Structured metadata: tüm Serilog property'leri (`CorrelationId`, `TenantId`, `UserId`, `ClientApp`, `RequestPath`,
  `StatusCode`, `Elapsed`…), `detected_level`, `message_template_text`, `exception_*`, `trace_id`, `span_id`.
- Örnek LogQL: `{service_name="SenseIK.Api"} | CorrelationId="..."`,
  `{service_name="SenseIK.Api"} | detected_level="error" | TenantId="..."`,
  `{service_name="SenseIK.Api"} | StatusCode >= 500 or Elapsed > 1000`.
- Log satırındaki **TraceID** bağlantısı izi Jaeger'de açar; Jaeger span'inden de *Logs for this span* ile Loki'ye dönülür.
- Sink `OTEL_EXPORTER_OTLP_*` ortam değişkenlerini bilinçli olarak yok sayar (`ignoreEnvironment: true`); hedef
  yalnızca `Observability:Logs:Endpoint`'tir.

## HTTP başlıkları

Sabitler: `SenseHeaderNames` (`Sense.Shared.Contracts/Security/ClaimNames.cs`); web (`packages/api-client`) ve mobil
(`apps/senseik-mobile/src/lib/apiClient.ts`) istemcileri aynı adları kullanır.

| Başlık | Yön | Davranış |
|---|---|---|
| `X-Correlation-Id` | istek + yanıt | İstemci gönderirse korunur; yoksa `X-Request-Id`, o da yoksa yeni GUID. AsyncLocal bağlama, loglara, span'e (`sense.correlation_id`) ve **giden HttpClient çağrılarına** (`CorrelationIdPropagationHandler`) taşınır. Web/mobil istemci her istekte üretir. |
| `X-Request-Id` | istek + yanıt | Tek isteğin kimliği; proxy/ingress'ten gelirse korunur, yoksa `HttpContext.TraceIdentifier`. |
| `X-Trace-Id` | yanıt | W3C trace id — destek ekibi izi Jaeger'de doğrudan bulur. (`traceparent`/`tracestate` .NET tarafından otomatik işlenir.) |
| `X-Client-App` / `X-Client-Version` / `X-Client-Platform` | istek | Loglara (`ClientApp`, `ClientVersion`, `ClientPlatform`) ve span'e (`sense.client.*`) eklenir. Yalnızca gözlemlenebilirlik içindir, yetkilendirmede kullanılmaz. |
| `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy` | yanıt | `SecurityHeadersMiddleware`; uygulama açıkça koyduysa üzerine yazmaz. CSP eklenmez (Scalar/Hangfire inline script kullanır). |
| `Strict-Transport-Security` | yanıt | Development dışı ortamlarda `UseHsts()`. |
| `Server` | yanıt | Kaldırıldı (`AddServerHeader = false`). |

- İstemciden gelen kimlik/istemci başlıkları yalnızca kısa (id ≤128, istemci bilgisi ≤64 karakter) ve
  `[A-Za-z0-9-_.:/]` karakterlerinden oluşuyorsa kabul edilir (`HeaderValues`); aksi halde yok sayılır — log injection
  ve başlık bölmeye karşı.
- Yanıt başlıkları `OnStarting`'de yeniden yazılır; `UseExceptionHandler` 500 yanıtında başlıkları temizlese de kimlikler
  ve güvenlik başlıkları korunur.
- CORS `Access-Control-Expose-Headers`: `SenseHeaderNames.ExposedToClients` (correlation/request/trace id, rate limit,
  `Retry-After`, `Content-Disposition`, `ETag`, `api-supported-versions`) — tarayıcı JS'i bunları ancak böyle okuyabilir.
- API bir reverse proxy arkasındaysa istemci IP'si/şeması için `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` ayarlayın
  (ASP.NET Core yerleşik forwarded headers desteği).

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
- **Örnekleme**: `ParentBased(TraceIdRatio(Observability:TracingSampleRatio))` — varsayılan 1.0; üretimde düşürün
  (ör. 0.1). Üst servisten gelen örnekleme kararına her zaman uyulur.
- **Exception kaydı**: ASP.NET Core ve HttpClient span'lerinde `RecordException = true`.
- **Metrics**: yukarıdaki "Metrikler (Prometheus)" bölümüne bakın (OTLP ile değil, `/metrics` üzerinden).
- **Exporter**: OTLP/gRPC (`Observability:OtlpEndpoint`, varsayılan `http://localhost:14317`) — Jaeger'in kendi
  (deprecated) native exporter paketi yerine, Jaeger'in yerleşik OTLP alıcısına gönderilir.
- **Jaeger'de arama**: *Tags* alanına `sense.correlation_id=<id>` veya `sense.client.app=mobile` yazın; ya da yanıt
  başlığındaki `X-Trace-Id` ile doğrudan izi açın.
- **İzin kontrolleri**: `AuthorizationBehaviour`, her izin kararını mevcut isteğin span'ine olay (`ActivityEvent`,
  `sense.permission_check`) olarak ekler — ayrı bir span açmaz.

Jaeger'i local'de çalıştırma (`infra/docker-compose.yml`, `obs` profili — Seq ile birlikte):

```powershell
docker compose -f infra/docker-compose.yml --profile obs up -d
```

Jaeger UI: http://localhost:26686 → servis adı `SenseIK.Api` → API'ye birkaç istek atın → "Find Traces".
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

- **Grafana'da metrik yok**: Prometheus → Status → Targets'ta `senseik-api` hedeflerinden biri `UP` mı? API host'ta
  çalışıyorsa `https://host.docker.internal:5001/metrics`, container'daysa `api:8080` scrape edilir; kullanılmayan hedefin
  `DOWN` görünmesi normaldir. `Observability:EnableMetrics=true` mı?
- **Grafana'da log yok**: `Observability:Logs:Enabled=true` mı (Production/`appsettings.json`'da varsayılan kapalı)?
  Loki ayakta mı (`curl localhost:13100/ready`)? API container'daysa endpoint `http://loki:3100/otlp/v1/logs` olmalı.
- **Grafana Jaeger datasource 404**: Jaeger imajı 2.21+ olmuş olabilir (bkz. yukarıdaki not).

- **Jaeger'de iz görünmüyor**: `Observability:EnableTracing=true` mu? `docker compose --profile obs` ile Jaeger
  ayakta mı (`docker ps`)? OTLP portu (14317) erişilebilir mi? `Observability:OtlpEndpoint` doğru mu (container
  dışından çalışıyorsanız `localhost:14317`, API de container içindeyse servis adı `jaeger:4317`)?
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

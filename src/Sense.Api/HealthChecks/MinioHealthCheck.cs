using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sense.Shared.Infrastructure.Storage;

namespace Sense.Api.HealthChecks;

/// <summary>
/// MinIO/S3 bağlantısını doğrular (Item 15 dosya depolama). MinIO SDK'sının S3 API'sine (kimlik doğrulama,
/// bucket listeleme) bağımlı olmadan, sunucunun kendi native liveness uç noktasını (/minio/health/live) sorar
/// — MinIO resmi dokümantasyonunun önerdiği yöntem; SDK sürümünden bağımsız ve daha hafiftir.
/// </summary>
public sealed class MinioHealthCheck(IHttpClientFactory httpClientFactory, IOptions<MinioOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var minio = options.Value;
        var scheme = minio.UseHttps ? "https" : "http";
        var uri = new Uri($"{scheme}://{minio.Endpoint}/minio/health/live");

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(MinioHealthCheck));
            using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("MinIO erişilebilir.")
                : HealthCheckResult.Unhealthy($"MinIO sağlık uç noktası {(int)response.StatusCode} döndürdü.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MinIO erişilemez durumda.", ex);
        }
    }
}

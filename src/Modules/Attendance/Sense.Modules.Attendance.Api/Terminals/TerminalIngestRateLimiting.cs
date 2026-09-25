using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Sense.Modules.Attendance.Api.Terminals;

/// <summary>
/// Okutma ucu hız sınırı (yapılandırma <c>RateLimiting:TerminalIngest</c>; ADR-0003 sabit pencere kalıbı). Pencere başına
/// <see cref="PermitLimit"/> istek terminal başına, <see cref="IpPermitLimit"/> istek IP başına (NAT arkasında birden çok terminal olabilir).
/// </summary>
public sealed class TerminalIngestRateLimitOptions
{
    public const string Section = "RateLimiting:TerminalIngest";

    public int PermitLimit { get; set; } = 60;

    public int IpPermitLimit { get; set; } = 600;

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// <c>UseRateLimiter()</c> (kimlik doğrulamadan ÖNCE) ile uygulanan <see cref="TerminalKeyDefaults.RateLimitPolicy"/> politikası: her istek hem
/// IP'sinin hem de terminal anahtarının sabit penceresinden izin almalıdır; biri doluysa 429 (Shared.Web'in OnRejected'ı ProblemDetails +
/// Retry-After yazar). Terminal bölümü anahtar önekine değil TAM anahtarın SHA-256 özetine göre ayrılır: önek gizli olmadığından önekle
/// bölümlemek, öneki bilen birinin gerçek terminalin kotasını sahte anahtarlarla tüketmesine (hedefli DoS) izin verirdi; özet ise yalnız
/// gerçek anahtarı bilen için aynı bölüme düşer. Anahtar kendisi hiçbir yerde tutulmaz.
/// </summary>
public static class TerminalIngestRateLimiting
{
    private const string MissingKeyPartition = "none";
    private const string UnknownIp = "unknown";

    public static IServiceCollection AddTerminalIngestRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(TerminalIngestRateLimitOptions.Section).Get<TerminalIngestRateLimitOptions>()
            ?? new TerminalIngestRateLimitOptions();
        var window = TimeSpan.FromSeconds(options.WindowSeconds);
        var perKey = CreateFixedWindow(options.PermitLimit, window);
        var perIp = CreateFixedWindow(options.IpPermitLimit, window);

        services.Configure<RateLimiterOptions>(limiter => limiter.AddPolicy(TerminalKeyDefaults.RateLimitPolicy, httpContext =>
        {
            var keyPartition = KeyPartition(httpContext.Request.Headers[TerminalKeyDefaults.HeaderName].ToString());
            var ipPartition = httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownIp;
            return RateLimitPartition.Get((keyPartition, ipPartition), key => new CombinedLimiter(perIp, key.ipPartition, perKey, key.keyPartition));
        }));
        return services;
    }

    private static string KeyPartition(string presentedKey) =>
        string.IsNullOrEmpty(presentedKey) ? MissingKeyPartition : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey)));

    private static PartitionedRateLimiter<string> CreateFixedWindow(int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(partition => RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));

    /// <summary>
    /// İki paylaşılan bölümlenmiş sınırlayıcının (IP, anahtar) önce IP sonra anahtar sırasıyla izin aldığı ince görünüm. Kendisi durum tutmaz ve
    /// iç sınırlayıcıları elden çıkarmaz (paylaşılırlar); boşta kaldığında ara yazılım onu atar, iç pencereler kendi boşta temizliğini yapar.
    /// </summary>
    private sealed class CombinedLimiter(PartitionedRateLimiter<string> perIp, string ip, PartitionedRateLimiter<string> perKey, string key) : RateLimiter
    {
        private long _lastUsed = Stopwatch.GetTimestamp();

        public override TimeSpan? IdleDuration => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastUsed));

        public override RateLimiterStatistics? GetStatistics() => perKey.GetStatistics(key);

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Interlocked.Exchange(ref _lastUsed, Stopwatch.GetTimestamp());
            var ipLease = perIp.AttemptAcquire(ip, permitCount);
            if (!ipLease.IsAcquired)
            {
                return ipLease;
            }

            var keyLease = perKey.AttemptAcquire(key, permitCount);
            if (!keyLease.IsAcquired)
            {
                ipLease.Dispose();
                return keyLease;
            }

            return new CombinedLease(ipLease, keyLease);
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AttemptAcquireCore(permitCount));
    }

    private sealed class CombinedLease(RateLimitLease first, RateLimitLease second) : RateLimitLease
    {
        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => first.MetadataNames.Concat(second.MetadataNames).Distinct(StringComparer.Ordinal);

        public override bool TryGetMetadata(string metadataName, out object? metadata) =>
            first.TryGetMetadata(metadataName, out metadata) || second.TryGetMetadata(metadataName, out metadata);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                second.Dispose();
                first.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Sense.Modules.Attendance.Api.Terminals;

/// <summary>
/// Okutma ucu sınırları (yapılandırma <see cref="Section"/>). <see cref="PermitLimit"/>: doğrulanmış terminal başına pencere başına izin
/// (okutma isteği <c>ceil(kalem/50)</c> izin, nabız 1 izin harcar). <see cref="FailedAuthPermitLimit"/>: IP başına pencere başına BAŞARISIZ
/// anahtar denemesi; geçerli anahtarlı istekler IP kotasından hiç pay düşmez. <see cref="MaxTrackedIps"/>: izlenen IP sayısı üst sınırı.
/// </summary>
public sealed class TerminalIngestRateLimitOptions
{
    public const string Section = "RateLimiting:TerminalIngest";

    public int PermitLimit { get; set; } = 60;

    public int FailedAuthPermitLimit { get; set; } = 30;

    public int WindowSeconds { get; set; } = 60;

    public int MaxTrackedIps { get; set; } = 10_000;

    public TimeSpan Window => TimeSpan.FromSeconds(Math.Max(1, WindowSeconds));
}

/// <summary>
/// Terminal başına istek bütçesi (kimlik doğrulamadan SONRA; bölüm anahtarı doğrulanmış terminal kimliği - doğrulanmamış girdiden türemez,
/// bölüm sayısı terminal sayısıyla sınırlı). Okutma isteğinin maliyeti kalem sayısıyla ölçeklenir.
/// </summary>
public sealed class TerminalRequestThrottle(IOptions<TerminalIngestRateLimitOptions> options) : IDisposable
{
    public const int ItemsPerPermit = 50;

    private readonly int _permitLimit = Math.Max(1, options.Value.PermitLimit);

    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.Create<Guid, Guid>(terminalId =>
        RateLimitPartition.GetFixedWindowLimiter(terminalId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, options.Value.PermitLimit),
            Window = options.Value.Window,
            QueueLimit = 0,
        }));

    /// <summary>Bir okutma isteğinin izin maliyeti: <c>ceil(kalem/50)</c>, en az 1.</summary>
    public static int CostOf(int itemCount) => Math.Max(1, (itemCount + ItemsPerPermit - 1) / ItemsPerPermit);

    /// <summary>İzin alınamazsa false ve (biliniyorsa) yeniden deneme süresi.</summary>
    public bool TryAcquire(Guid terminalId, int permits, out TimeSpan? retryAfter)
    {
        using var lease = _limiter.AttemptAcquire(terminalId, Math.Clamp(permits, 1, _permitLimit));
        retryAfter = !lease.IsAcquired && lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : null;
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

/// <summary>
/// IP başına BAŞARISIZ anahtar denemesi kovası (sabit pencere). Yalnız başarısız doğrulamalar yazılır; kova dolunca sonraki başarısız denemeler
/// 429 alır, geçerli anahtarlı istekler (aynı proxy IP'sinden gelse de) etkilenmez. İzlenen IP sayısı <see cref="TerminalIngestRateLimitOptions.MaxTrackedIps"/>
/// ile sınırlıdır: dolunca süresi geçmiş kovalar, o da yetmezse en eski pencere atılır.
/// </summary>
public sealed class TerminalAuthFailureTracker(IOptions<TerminalIngestRateLimitOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly int _limit = Math.Max(1, options.Value.FailedAuthPermitLimit);
    private readonly int _maxTracked = Math.Max(1, options.Value.MaxTrackedIps);
    private readonly TimeSpan _window = options.Value.Window;

    public int TrackedCount
    {
        get
        {
            lock (_gate)
            {
                return _buckets.Count;
            }
        }
    }

    /// <summary>Başarısız denemeyi yazar; kova sınırı aşıldıysa yeniden deneme süresini döner (429), aşılmadıysa null (401).</summary>
    public TimeSpan? RegisterFailure(string ip)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            if (!_buckets.TryGetValue(ip, out var bucket) || now - bucket.WindowStart >= _window)
            {
                MakeRoom(now);
                bucket = new Bucket(now);
                _buckets[ip] = bucket;
            }

            bucket.Failures++;
            return bucket.Failures > _limit ? bucket.WindowStart + _window - now : null;
        }
    }

    private void MakeRoom(DateTime now)
    {
        if (_buckets.Count < _maxTracked)
        {
            return;
        }

        foreach (var expired in _buckets.Where(b => now - b.Value.WindowStart >= _window).Select(b => b.Key).ToList())
        {
            _buckets.Remove(expired);
        }

        while (_buckets.Count >= _maxTracked)
        {
            _buckets.Remove(_buckets.MinBy(b => b.Value.WindowStart).Key);
        }
    }

    private sealed class Bucket(DateTime windowStart)
    {
        public DateTime WindowStart { get; } = windowStart;

        public int Failures { get; set; }
    }
}

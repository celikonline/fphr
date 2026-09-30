using Microsoft.Extensions.Options;

namespace Sense.Modules.Attendance.Api.TerminalCredentials;

/// <summary>Kimlik ekleme/etkinleştirme uçlarının kullanıcı başına sınırları (yapılandırma <see cref="Section"/>).</summary>
public sealed class TerminalCredentialThrottleOptions
{
    public const string Section = "RateLimiting:TerminalCredentials";

    /// <summary>Kullanıcı başına pencere başına istek (ekleme + etkinleştirme).</summary>
    public int PermitLimit { get; set; } = 30;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>Kullanıcı başına pencere başına "PIN başkasında aktif" (genel <c>rejected</c>) yanıtı; aşılınca PIN istekleri 429.</summary>
    public int PinRejectBudget { get; set; } = 5;

    public int PinRejectWindowMinutes { get; set; } = 5;

    /// <summary>İzlenen kullanıcı sayısı üst sınırı (dolunca süresi geçenler, gerekirse en eski pencere atılır).</summary>
    public int MaxTrackedUsers { get; set; } = 10_000;
}

/// <summary>
/// PIN varlık ipucu (oracle) hafifletmesi (Z5.4, T2 tur 3): kimlik doğrulamadan SONRA, doğrulanmış kullanıcı kimliğiyle (Program.cs'teki
/// <c>UseRateLimiter</c> kimlik doğrulamadan önce çalıştığı için ara yazılım politikası kullanıcıyı göremez - T1 TerminalRequestThrottle kalıbı).
/// (1) Kullanıcı başına sabit pencere istek sınırı. (2) Kullanıcı başına "PIN başkasında aktif" ret bütçesi: bütçe dolunca pencere bitene
/// kadar PIN ekleme ve etkinleştirme istekleri işlenmeden 429 (genel ileti). Bellek içi, süreç başına (çok örnekli dağıtımda örnek başına).
/// </summary>
public sealed class TerminalCredentialThrottle(IOptions<TerminalCredentialThrottleOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Window> _requests = [];
    private readonly Dictionary<Guid, Window> _pinRejects = [];
    private readonly int _permitLimit = Math.Max(1, options.Value.PermitLimit);
    private readonly TimeSpan _requestWindow = TimeSpan.FromSeconds(Math.Max(1, options.Value.WindowSeconds));
    private readonly int _pinRejectBudget = Math.Max(1, options.Value.PinRejectBudget);
    private readonly TimeSpan _pinRejectWindow = TimeSpan.FromMinutes(Math.Max(1, options.Value.PinRejectWindowMinutes));
    private readonly int _maxTracked = Math.Max(1, options.Value.MaxTrackedUsers);

    /// <summary>İstek izni; sınır aşıldıysa false ve yeniden deneme süresi.</summary>
    public bool TryAcquireRequest(Guid userId, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var window = Current(_requests, userId, _requestWindow);
            window.Count++;
            retryAfter = window.Start + _requestWindow - Now();
            return window.Count <= _permitLimit;
        }
    }

    /// <summary>Kullanıcının PIN ret bütçesi bu pencerede doldu mu (doluysa PIN istekleri işlenmez).</summary>
    public bool IsPinRejectBudgetExhausted(Guid userId, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            retryAfter = TimeSpan.Zero;
            if (!_pinRejects.TryGetValue(userId, out var window) || Now() - window.Start >= _pinRejectWindow)
            {
                return false;
            }

            retryAfter = window.Start + _pinRejectWindow - Now();
            return window.Count >= _pinRejectBudget;
        }
    }

    /// <summary>"PIN başkasında aktif" yanıtını kullanıcının bütçesine yazar.</summary>
    public void RegisterPinReject(Guid userId)
    {
        lock (_gate)
        {
            Current(_pinRejects, userId, _pinRejectWindow).Count++;
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private Window Current(Dictionary<Guid, Window> windows, Guid userId, TimeSpan length)
    {
        var now = Now();
        if (windows.TryGetValue(userId, out var window) && now - window.Start < length)
        {
            return window;
        }

        if (windows.Count >= _maxTracked)
        {
            Prune(windows, now, length);
        }

        window = new Window(now);
        windows[userId] = window;
        return window;
    }

    /// <summary>Yalnız sınır dolunca çalışır: süresi geçenleri, yetmezse en eski pencereyi atar.</summary>
    private void Prune(Dictionary<Guid, Window> windows, DateTime now, TimeSpan length)
    {
        foreach (var expired in windows.Where(w => now - w.Value.Start >= length).Select(w => w.Key).ToList())
        {
            windows.Remove(expired);
        }

        if (windows.Count >= _maxTracked)
        {
            windows.Remove(windows.MinBy(w => w.Value.Start).Key);
        }
    }

    private sealed class Window(DateTime start)
    {
        public DateTime Start { get; } = start;

        public int Count { get; set; }
    }
}

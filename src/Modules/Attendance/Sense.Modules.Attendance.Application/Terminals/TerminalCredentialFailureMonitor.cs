using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Bir PIN kaleminin kaba kuvvet kapısından geçişi.</summary>
public enum PinGateOutcome
{
    /// <summary>Engel yok, kimlik doğru: okutma işlenir.</summary>
    Allowed,

    /// <summary>Engel yok, kimlik yanlış: bütçeye yazıldı (ya da aynı özet pencerede zaten sayılmıştı).</summary>
    Failed,

    /// <summary>Engel açık: doğru/yanlış ayrımı yapılmaz, hiçbir şey yazılmaz.</summary>
    Blocked,
}

/// <summary>
/// PIN kaba kuvvet koruması (Z5.1). Engel yokken terminal başına kayan pencerede (W) FARKLI yanlış PIN özetleri sayılır (aynı değerin tekrarı -
/// ör. çevrimdışı toplu gönderimin yeniden denemesi - tek sayılır). Sayı bütçeye (B) ulaşınca engel açılır ve girişten TAM W sonra biter: engel
/// süresince HİÇBİR PIN kalemi (doğru ya da yanlış) bütçeye yazılmaz, sayaç engele girişte sıfırlanır. Böylece engelin bitişi ne meşru trafikle
/// uzar ne de adayların doğru olup olmadığını (nabızdaki <c>pinEntryBlocked</c> üzerinden) sızdırır; deneme hızı en çok B/W. "Engelli mi + kayıt"
/// tek kilit altında atomiktir (eşzamanlı isteklerde bütçe taşmaz). Bellek içidir (süreç başına; şema değişikliği yok); kayıt sayısı terminal
/// başına en çok B. Engele geçişte terminal koduyla uyarı loglanır (kimlik değeri ASLA). <see cref="LastSuspiciousAtUtc"/> "şüpheli deneme" işareti.
/// </summary>
public sealed partial class TerminalCredentialFailureMonitor(
    IOptions<TerminalIngestOptions> options, TimeProvider clock, ILogger<TerminalCredentialFailureMonitor> logger)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, State> _terminals = [];
    private readonly int _budget = Math.Max(1, options.Value.PinFailureBudget);
    private readonly TimeSpan _window = TimeSpan.FromMinutes(Math.Max(1, options.Value.PinFailureWindowMinutes));

    public bool IsBlocked(Guid terminalId)
    {
        lock (_gate)
        {
            return _terminals.TryGetValue(terminalId, out var state) && state.BlockedUntilUtc > Now();
        }
    }

    /// <summary>
    /// Tek kilit altında: engel açıksa <see cref="PinGateOutcome.Blocked"/> (hiçbir şey yazılmaz); değilse doğru kimlik
    /// <see cref="PinGateOutcome.Allowed"/>, yanlış kimlik bütçeye yazılır (<see cref="PinGateOutcome.Failed"/>) - bütçe bu kayıtla dolarsa engel
    /// açılır (bu kalem yine <see cref="PinGateOutcome.Failed"/>).
    /// </summary>
    public PinGateOutcome Evaluate(TerminalIdentity terminal, string credentialDigest, bool isFailure)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentException.ThrowIfNullOrEmpty(credentialDigest);
        var now = Now();
        lock (_gate)
        {
            _terminals.TryGetValue(terminal.TerminalId, out var state);
            if (state?.BlockedUntilUtc > now)
            {
                return PinGateOutcome.Blocked;
            }

            if (!isFailure)
            {
                return PinGateOutcome.Allowed;
            }

            if (state is null)
            {
                state = new State();
                _terminals[terminal.TerminalId] = state;
            }

            Prune(state, now);
            state.Failures.TryAdd(credentialDigest, now);
            if (state.Failures.Count >= _budget)
            {
                state.Failures.Clear();
                state.BlockedUntilUtc = now + _window;
                state.LastSuspiciousAtUtc = now;
                LogPinBudgetExceeded(logger, terminal.Code, _budget, (int)_window.TotalMinutes);
            }

            return PinGateOutcome.Failed;
        }
    }

    public DateTime? LastSuspiciousAtUtc(Guid terminalId)
    {
        lock (_gate)
        {
            return _terminals.TryGetValue(terminalId, out var state) ? state.LastSuspiciousAtUtc : null;
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    /// <summary>Pencereden çıkmış kayıtları atar (en çok bütçe kadar kayıt var; maliyet sabit üst sınırlı).</summary>
    private void Prune(State state, DateTime now)
    {
        foreach (var expired in state.Failures.Where(f => now - f.Value >= _window).Select(f => f.Key).ToList())
        {
            state.Failures.Remove(expired);
        }
    }

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning,
        Message = "Terminal {TerminalCode}: {Budget} distinct failed PINs within {WindowMinutes} min - PIN entry on this terminal is blocked for {WindowMinutes} min (possible brute force).")]
    private static partial void LogPinBudgetExceeded(ILogger logger, string terminalCode, int budget, int windowMinutes);

    private sealed class State
    {
        /// <summary>Engel yokken: yanlış PIN özeti → pencereye ilk girdiği an (en çok bütçe kadar kayıt).</summary>
        public Dictionary<string, DateTime> Failures { get; } = new(StringComparer.Ordinal);

        public DateTime? BlockedUntilUtc { get; set; }

        public DateTime? LastSuspiciousAtUtc { get; set; }
    }
}

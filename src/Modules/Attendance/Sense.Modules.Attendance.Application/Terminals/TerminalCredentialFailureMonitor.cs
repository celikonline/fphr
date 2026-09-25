using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// PIN kaba kuvvet koruması (Z5.1): terminal başına kayan pencerede FARKLI yanlış PIN özetlerini sayar (aynı yanlış değerin tekrarı - ör. çevrimdışı
/// toplu gönderimin yeniden denemesi - pencere içinde tek sayılır ve süresini uzatmaz). Yalnız gerçekten yanlış (eşleşmeyen) PIN'ler yazılır; doğru
/// PIN'ler engel sırasında da bütçeye yazılmaz, böylece meşru trafik engeli kendiliğinden uzatamaz. Bütçe (<see cref="TerminalIngestOptions.PinFailureBudget"/>)
/// dolunca o terminalin PIN kalemleri çözülmeden reddedilir; engele geçişte terminal koduyla uyarı loglanır (kimlik değeri ASLA). Bellek içidir
/// (süreç başına; şema değişikliği yok): yalnız kimliği doğrulanmış terminaller izlendiği için kayıt sayısı terminal sayısıyla sınırlıdır ve
/// terminal başına en çok bütçe kadar zaman damgası tutulur. <see cref="LastSuspiciousAtUtc"/> yönetim listesinde ve nabızda "şüpheli deneme" işaretidir.
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
            return _terminals.TryGetValue(terminalId, out var state) && CountInWindow(state, Now()) >= _budget;
        }
    }

    /// <summary>
    /// Yanlış bir PIN'i (özetiyle) kaydeder; bu özet pencerede zaten sayıldıysa hiçbir şey yapmaz. Bütçe bu kayıtla doluyorsa (engele geçiş) uyarı loglar.
    /// </summary>
    public void RecordFailure(TerminalIdentity terminal, string credentialDigest)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentException.ThrowIfNullOrEmpty(credentialDigest);
        var now = Now();
        bool becameBlocked;
        lock (_gate)
        {
            if (!_terminals.TryGetValue(terminal.TerminalId, out var state))
            {
                state = new State();
                _terminals[terminal.TerminalId] = state;
            }

            Prune(state, now);
            if (state.Failures.ContainsKey(credentialDigest))
            {
                return;
            }

            var wasBlocked = state.Failures.Count >= _budget;
            state.Failures[credentialDigest] = now;
            while (state.Failures.Count > _budget)
            {
                state.Failures.Remove(state.Failures.MinBy(f => f.Value).Key);
            }

            var blocked = CountInWindow(state, now) >= _budget;
            if (blocked)
            {
                state.LastSuspiciousAtUtc = now;
            }

            becameBlocked = blocked && !wasBlocked;
        }

        if (becameBlocked)
        {
            LogPinBudgetExceeded(logger, terminal.Code, _budget, (int)_window.TotalMinutes);
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

    private int CountInWindow(State state, DateTime now) => state.Failures.Values.Count(t => now - t < _window);

    /// <summary>Pencereden çıkmış kayıtları atar (en çok bütçe kadar kayıt var; maliyet sabit üst sınırlı).</summary>
    private void Prune(State state, DateTime now)
    {
        foreach (var expired in state.Failures.Where(f => now - f.Value >= _window).Select(f => f.Key).ToList())
        {
            state.Failures.Remove(expired);
        }
    }

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning,
        Message = "Terminal {TerminalCode}: {Budget} failed PIN items within {WindowMinutes} min - PIN entry on this terminal is rejected until the window clears (possible brute force).")]
    private static partial void LogPinBudgetExceeded(ILogger logger, string terminalCode, int budget, int windowMinutes);

    private sealed class State
    {
        /// <summary>Yanlış PIN özeti → pencereye ilk girdiği an (en çok bütçe kadar kayıt).</summary>
        public Dictionary<string, DateTime> Failures { get; } = new(StringComparer.Ordinal);

        public DateTime? LastSuspiciousAtUtc { get; set; }
    }
}

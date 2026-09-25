using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// PIN kaba kuvvet koruması (Z5.1): terminal başına başarısız PIN kalemlerini kayan pencerede sayar. Bütçe (<see cref="TerminalIngestOptions.PinFailureBudget"/>)
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

    /// <summary>Bir başarısız PIN kalemi kaydeder; bütçe bu kayıtla doluyorsa (engele geçiş) uyarı loglar.</summary>
    public void RecordFailure(TerminalIdentity terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var now = Now();
        bool becameBlocked;
        lock (_gate)
        {
            if (!_terminals.TryGetValue(terminal.TerminalId, out var state))
            {
                state = new State();
                _terminals[terminal.TerminalId] = state;
            }

            var wasBlocked = CountInWindow(state, now) >= _budget;
            state.Failures.Enqueue(now);
            while (state.Failures.Count > _budget)
            {
                state.Failures.Dequeue();
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

    private int CountInWindow(State state, DateTime now) => state.Failures.Count(t => now - t < _window);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning,
        Message = "Terminal {TerminalCode}: {Budget} failed PIN items within {WindowMinutes} min - PIN entry on this terminal is rejected until the window clears (possible brute force).")]
    private static partial void LogPinBudgetExceeded(ILogger logger, string terminalCode, int budget, int windowMinutes);

    private sealed class State
    {
        public Queue<DateTime> Failures { get; } = new();

        public DateTime? LastSuspiciousAtUtc { get; set; }
    }
}

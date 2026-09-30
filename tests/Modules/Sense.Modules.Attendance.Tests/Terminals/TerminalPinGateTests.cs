using Microsoft.Extensions.Time.Testing;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>PIN kapısı (<see cref="TerminalCredentialFailureMonitor.Evaluate"/>): "engelli mi + kayıt" atomik; engel tam W sürer, sırasında hiçbir şey yazılmaz.</summary>
public sealed class TerminalPinGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
    private readonly TerminalIdentity _terminal = new(Guid.NewGuid(), Guid.NewGuid(), TerminalDirectionMode.Both, null, "T-01");

    [Fact]
    public void Concurrent_wrong_pins_at_the_budget_boundary_do_not_overflow()
    {
        const int Budget = 5;
        const int Attempts = 200;
        var monitor = TerminalTestData.Monitor(new FakeTimeProvider(Start), budget: Budget);

        var outcomes = new System.Collections.Concurrent.ConcurrentBag<PinGateOutcome>();
        Parallel.For(0, Attempts, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
            outcomes.Add(monitor.Evaluate(_terminal, "wrong-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), isFailure: true)));

        outcomes.Count(o => o == PinGateOutcome.Failed).ShouldBe(Budget);
        outcomes.Count(o => o == PinGateOutcome.Blocked).ShouldBe(Attempts - Budget);
        monitor.IsBlocked(_terminal.TerminalId).ShouldBeTrue();
    }

    [Fact]
    public void While_blocked_every_candidate_gets_the_same_outcome_and_nothing_is_recorded()
    {
        var clock = new FakeTimeProvider(Start);
        var monitor = TerminalTestData.Monitor(clock, budget: 2);
        monitor.Evaluate(_terminal, "a", isFailure: true).ShouldBe(PinGateOutcome.Failed);
        monitor.Evaluate(_terminal, "b", isFailure: true).ShouldBe(PinGateOutcome.Failed);

        clock.Advance(TimeSpan.FromMinutes(4));
        monitor.Evaluate(_terminal, "c", isFailure: true).ShouldBe(PinGateOutcome.Blocked);
        monitor.Evaluate(_terminal, "correct", isFailure: false).ShouldBe(PinGateOutcome.Blocked);
        clock.Advance(TimeSpan.FromMinutes(1));

        // Engel girişten (t=0) tam 5 dk sonra biter; engel sırasındaki "c" sayılmadı, sayaç sıfırdan başlar.
        monitor.IsBlocked(_terminal.TerminalId).ShouldBeFalse();
        monitor.Evaluate(_terminal, "correct", isFailure: false).ShouldBe(PinGateOutcome.Allowed);
        monitor.Evaluate(_terminal, "c", isFailure: true).ShouldBe(PinGateOutcome.Failed);
        monitor.IsBlocked(_terminal.TerminalId).ShouldBeFalse();
        monitor.LastSuspiciousAtUtc(_terminal.TerminalId).ShouldBe(Start.UtcDateTime);
    }
}

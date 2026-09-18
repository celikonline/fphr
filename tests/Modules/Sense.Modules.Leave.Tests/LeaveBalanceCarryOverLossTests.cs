using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// Kolay İK "Devir kayıpları" column of the leave balance report (11.3.12 #7) - derived from the ledger alone by
/// <see cref="LeaveBalanceEngine.CarryOverLoss"/>.
/// </summary>
public sealed class LeaveBalanceCarryOverLossTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TypeId = Guid.NewGuid();

    // Periods: 2024 (closed), 2025 (closed), 2026 (current).
    private static readonly LeavePeriod Current = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    [Fact]
    public void LimitedCarryOver_LossIsClosingMinusCarried_AcrossEveryClosedPeriod()
    {
        var entries = new List<LeaveLedgerEntry>
        {
            // 2024: accrued 14, used 4 → closing 10; rule carries max 5 → loss 5.
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2024, 1, 1)),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, TypeId, 4m, new DateOnly(2024, 6, 3), LeaveLedgerSource.Request, null),
            LeaveLedgerEntry.CarryOver(TenantId, EmployeeId, TypeId, 5m, new DateOnly(2025, 1, 1)),
            // 2025: carried 5 + accrued 14, used 12 → closing 7; carries 5 → loss 2.
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2025, 1, 1)),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, TypeId, 12m, new DateOnly(2025, 8, 11), LeaveLedgerSource.Request, null),
            LeaveLedgerEntry.CarryOver(TenantId, EmployeeId, TypeId, 5m, new DateOnly(2026, 1, 1)),
            // 2026 rows must not influence the figure.
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2026, 1, 1)),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, TypeId, 3m, new DateOnly(2026, 2, 2), LeaveLedgerSource.Request, null),
        };

        LeaveBalanceEngine.CarryOverLoss(Current, entries).ShouldBe(7m);
    }

    [Fact]
    public void UnlimitedCarryOver_HasNoLoss()
    {
        var entries = new List<LeaveLedgerEntry>
        {
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2024, 1, 1)),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, TypeId, 4m, new DateOnly(2024, 6, 3), LeaveLedgerSource.Request, null),
            LeaveLedgerEntry.CarryOver(TenantId, EmployeeId, TypeId, 10m, new DateOnly(2025, 1, 1)),
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2025, 1, 1)),
            LeaveLedgerEntry.CarryOver(TenantId, EmployeeId, TypeId, 24m, new DateOnly(2026, 1, 1)),
        };

        LeaveBalanceEngine.CarryOverLoss(Current, entries).ShouldBe(0m);
    }

    [Fact]
    public void ResetRule_LosesTheWholeClosingBalance()
    {
        var entries = new List<LeaveLedgerEntry>
        {
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2025, 1, 1)),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, TypeId, 9m, new DateOnly(2025, 3, 3), LeaveLedgerSource.Request, null),
            // Reset mode writes no CarryOver row (CarryOverAmount = 0 → engine skips it).
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2026, 1, 1)),
        };

        LeaveBalanceEngine.CarryOverLoss(Current, entries).ShouldBe(5m);
    }

    [Fact]
    public void NegativeClosingForgivenByReset_IsNotReportedAsLoss()
    {
        var entries = new List<LeaveLedgerEntry>
        {
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2025, 1, 1)),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, TypeId, 16m, new DateOnly(2025, 3, 3), LeaveLedgerSource.Request, null),
        };

        LeaveBalanceEngine.CarryOverLoss(Current, entries).ShouldBe(0m);
    }

    [Fact]
    public void FirstPeriod_HasNothingBefore_SoNoLoss()
    {
        var entries = new List<LeaveLedgerEntry>
        {
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, TypeId, 14m, new DateOnly(2026, 1, 1)),
        };

        LeaveBalanceEngine.CarryOverLoss(Current, entries).ShouldBe(0m);
        LeaveBalanceEngine.CarryOverLoss(Current, []).ShouldBe(0m);
    }
}

using NSubstitute;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Contracts.Events;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Events;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// 11.3.07-bordro.md "Değişti mekanizması" (P3): a cross-module event on an employee with a payslip in an Open period
/// produces a <see cref="PayslipDirtyFlag"/>. Repositories/unit-of-work are NSubstitute fakes; <see cref="PayslipDirtyFlagService"/>
/// itself runs for real (it is the orchestration under test, not a dependency to fake out).
/// </summary>
public sealed class PayslipDirtyFlagHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static (IPayrollPeriodRepository Periods, IPayslipDirtyFlagRepository DirtyFlags, IPayrollUnitOfWork UnitOfWork, PayslipDirtyFlagService Flagging, PayrollPeriod Period) Build()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([period]));

        var dirtyFlags = Substitute.For<IPayslipDirtyFlagRepository>();
        dirtyFlags.FindActiveAsync(period.Id, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDirtyFlag?>(null));

        var unitOfWork = Substitute.For<IPayrollUnitOfWork>();
        var flagging = new PayslipDirtyFlagService(periods, dirtyFlags, TimeProvider.System);
        return (periods, dirtyFlags, unitOfWork, flagging, period);
    }

    [Fact]
    public async Task SalaryChanged_OnAnOpenPeriodCoveringTheEffectiveDate_CreatesADirtyFlag()
    {
        var (_, dirtyFlags, unitOfWork, flagging, period) = Build();
        var handler = new SalaryChangedDirtyFlagHandler(flagging, unitOfWork);

        await handler.Handle(new SalaryChanged(TenantId, EmployeeId, Guid.NewGuid(), new DateOnly(2026, 3, 1)), CancellationToken.None);

        dirtyFlags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f =>
            f.PayrollPeriodId == period.Id && f.EmployeeId == EmployeeId && f.Reasons.Contains(PayslipDirtyFlagReasons.SalaryChanged)));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveCancelled_CreatesADirtyFlagWithTheLeaveChangedReason()
    {
        var (_, dirtyFlags, unitOfWork, flagging, period) = Build();
        var handler = new LeaveCancelledDirtyFlagHandler(flagging, unitOfWork);

        await handler.Handle(
            new LeaveCancelled(TenantId, Guid.NewGuid(), EmployeeId, Guid.NewGuid(), new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 12), 2m, WasApproved: true),
            CancellationToken.None);

        dirtyFlags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.PayrollPeriodId == period.Id && f.Reasons.Contains(PayslipDirtyFlagReasons.LeaveChanged)));
    }

    [Fact]
    public async Task EmployeeProfileChanged_BankSection_CreatesADirtyFlag()
    {
        var (_, dirtyFlags, unitOfWork, flagging, period) = Build();
        var handler = new EmployeeProfileChangedDirtyFlagHandler(flagging, unitOfWork, TimeProvider.System);

        await handler.Handle(new EmployeeProfileChanged(TenantId, EmployeeId, EmployeeProfileSections.Bank), CancellationToken.None);

        dirtyFlags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.PayrollPeriodId == period.Id && f.Reasons.Contains(PayslipDirtyFlagReasons.BankChanged)));
    }

    [Fact]
    public async Task EmployeeProfileChanged_NonBankSection_IsIgnored()
    {
        var (_, dirtyFlags, unitOfWork, flagging, _) = Build();
        var handler = new EmployeeProfileChangedDirtyFlagHandler(flagging, unitOfWork, TimeProvider.System);

        await handler.Handle(new EmployeeProfileChanged(TenantId, EmployeeId, EmployeeProfileSections.CustomFields), CancellationToken.None);

        dirtyFlags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondReason_OnAnAlreadyActiveFlag_MergesInsteadOfCreatingASecondRow()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([period]));

        var existingFlag = PayslipDirtyFlag.Create(TenantId, period.Id, EmployeeId, PayslipDirtyFlagReasons.SalaryChanged, Guid.NewGuid(), DateTime.UtcNow);
        var dirtyFlags = Substitute.For<IPayslipDirtyFlagRepository>();
        dirtyFlags.FindActiveAsync(period.Id, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDirtyFlag?>(existingFlag));

        var unitOfWork = Substitute.For<IPayrollUnitOfWork>();
        var flagging = new PayslipDirtyFlagService(periods, dirtyFlags, TimeProvider.System);
        var handler = new EmployeeProfileChangedDirtyFlagHandler(flagging, unitOfWork, TimeProvider.System);

        await handler.Handle(new EmployeeProfileChanged(TenantId, EmployeeId, EmployeeProfileSections.Bank), CancellationToken.None);

        dirtyFlags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());
        existingFlag.Reasons.ShouldBe([PayslipDirtyFlagReasons.SalaryChanged, PayslipDirtyFlagReasons.BankChanged]);
    }
}

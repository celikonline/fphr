using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// Period-status guard on the P3 recalculation commands (11.3.07-bordro.md "Approved+ dönemde bayrak kalır, yeniden
/// hesap yerine Supplementary önerilir"): both <see cref="RecalculateAllPayslipsCommand"/> and
/// <see cref="RecalculateDirtyPayslipsCommand"/> must reject a non-Open period rather than silently recalculating it.
/// PayrollPeriodCalculationService itself is not invoked in these tests (fakes are enough to prove the guard fires
/// before any calculation happens) - its own behavior is covered by PayrollPeriodCalculationServiceTests.
/// </summary>
public sealed class PayrollPeriodFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static PayrollPeriod LockedPeriod()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        period.Approve(null).IsSuccess.ShouldBeTrue();
        period.MarkPaid().IsSuccess.ShouldBeTrue();
        period.Lock().IsSuccess.ShouldBeTrue();
        return period;
    }

    [Fact]
    public async Task RecalculateAllPayslipsHandler_OnALockedPeriod_IsRejectedWithoutCallingTheCalculationService()
    {
        var period = LockedPeriod();
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var tenant = Substitute.For<Sense.Shared.Contracts.Context.ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var currentUser = Substitute.For<Sense.Shared.Contracts.Context.ICurrentUser>();
        var handler = new RecalculateAllPayslipsHandler(tenant, currentUser, periods, employeeDirectory, calculation: null!);

        var result = await handler.Handle(new RecalculateAllPayslipsCommand(period.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotOpen);
        await employeeDirectory.DidNotReceive().ListActiveEmployeeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateDirtyPayslipsHandler_OnALockedPeriod_IsRejected()
    {
        var period = LockedPeriod();
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));

        var handler = new RecalculateDirtyPayslipsHandler(periods, calculation: null!, Substitute.For<Sense.Shared.Contracts.Context.ICurrentUser>());

        var result = await handler.Handle(new RecalculateDirtyPayslipsCommand(period.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotOpen);
    }

    [Fact]
    public async Task OpenPayrollPeriodHandler_WhenAPeriodAlreadyExistsForThatMonth_Conflicts()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByYearMonthAsync(2026, 3, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(PayrollPeriod.Create(TenantId, 2026, 3).Value));
        var tenant = Substitute.For<Sense.Shared.Contracts.Context.ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var handler = new OpenPayrollPeriodHandler(tenant, periods, TimeProvider.System);
        var result = await handler.Handle(new OpenPayrollPeriodCommand(2026, 3), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodAlreadyOpen);
    }

    [Theory]
    [InlineData(2001)]
    [InlineData(2023)]
    [InlineData(2999)]
    public async Task OpenPayrollPeriodHandler_WhenTheYearIsOutsideTheSupportedWindow_IsRejectedAsValidation(int year)
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var tenant = Substitute.For<Sense.Shared.Contracts.Context.ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var handler = new OpenPayrollPeriodHandler(tenant, periods, TimeProvider.System);
        var result = await handler.Handle(new OpenPayrollPeriodCommand(year, 6), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodYearOutOfRange);
        result.Error.Type.ShouldBe(Sense.Shared.Kernel.Results.ErrorType.Validation);
        periods.DidNotReceive().Add(Arg.Any<PayrollPeriod>());
    }

    [Fact]
    public async Task RecalculateAllPayslipsHandler_WhenNoLegislationParameterExistsForThePeriod_ReturnsABusinessRuleError()
    {
        var period = PayrollPeriod.Create(TenantId, 2025, 12).Value;
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        employeeDirectory.ListPayrollCandidateEmployeeIdsAsync(TenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([Guid.NewGuid()]));
        var tenant = Substitute.For<Sense.Shared.Contracts.Context.ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        // No brackets for 2025 in the legislation store: the reader returns null for every key.
        var reader = Substitute.For<Sense.Shared.Contracts.Legislation.ILegislationParameterReader>();
        var calculation = new PayrollPeriodCalculationService(
            Substitute.For<IPayslipRepository>(), Substitute.For<IPayslipCalculationTraceRepository>(),
            Substitute.For<IEmployeePayrollProfileRepository>(), Substitute.For<IPayslipDirtyFlagRepository>(),
            Substitute.For<IEmployeeSalaryLookup>(), reader, tenant, TimeProvider.System,
            Substitute.For<Sense.Modules.Payroll.Application.Ports.IWorkedTimeReader>(), Substitute.For<Sense.Modules.Payroll.Application.Ports.IAbsenceDaysReader>(),
            Substitute.For<Sense.Modules.Payroll.Application.Ports.IExpensePayoutReader>(), periods,
            Substitute.For<IGarnishmentOrderRepository>(), Substitute.For<IEmployeeDisabilityLookup>(),
            Substitute.For<Sense.Modules.Payroll.Domain.IEmployeeUnionMembershipRepository>(),
            Substitute.For<Sense.Modules.Leave.Contracts.IOvertimePayrollReader>(), Substitute.For<IPayrollAdjustmentRepository>(),
            Substitute.For<Sense.Modules.Payroll.Domain.IEmployeePayItemAssignmentRepository>(), Substitute.For<Sense.Modules.Payroll.Domain.IPayItemDefinitionRepository>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PayrollPeriodCalculationService>.Instance);

        var handler = new RecalculateAllPayslipsHandler(tenant, Substitute.For<Sense.Shared.Contracts.Context.ICurrentUser>(), periods, employeeDirectory, calculation);
        var result = await handler.Handle(new RecalculateAllPayslipsCommand(period.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.LegislationParameterMissing);
        result.Error.Type.ShouldBe(Sense.Shared.Kernel.Results.ErrorType.Rule);
    }
}

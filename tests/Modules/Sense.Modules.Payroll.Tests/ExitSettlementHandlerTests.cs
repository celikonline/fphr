using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>"Çıkış hesabı": EmployeeTerminated freezes the kıdem/ihbar/izin amounts into an <see cref="ExitSettlement"/>,
/// once per exit, and never fails the outbox row when the estimate cannot be produced.</summary>
public sealed class ExitSettlementHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2024, 3, 1);
    private static readonly DateOnly TerminationDate = new(2025, 4, 5);

    private const string TiersJson = """[{"upperBoundDays":183,"weeks":2},{"upperBoundDays":548,"weeks":4},{"upperBoundDays":1095,"weeks":6},{"upperBoundDays":null,"weeks":8}]""";

    private static ILegislationParameterReader Legislation(bool present)
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        if (!present)
        {
            return reader;
        }

        reader.FindAsync(LegislationParameterKeys.SeveranceCeilingGross, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LegislationParameterValue?>(new LegislationParameterValue(
                LegislationParameterKeys.SeveranceCeilingGross, "TR", 100_000m, null, new DateOnly(2020, 1, 1), null, "test")));
        reader.FindAsync(LegislationParameterKeys.NoticeWeeksBySeniorityTiers, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LegislationParameterValue?>(new LegislationParameterValue(
                LegislationParameterKeys.NoticeWeeksBySeniorityTiers, "TR", null, TiersJson, new DateOnly(2020, 1, 1), null, "test")));
        return reader;
    }

    private static (EmployeeTerminatedSettlementHandler Handler, IExitSettlementRepository Settlements, IPayrollUnitOfWork UnitOfWork) Build(
        bool legislationPresent = true, ExitSettlement? existing = null)
    {
        var salary = Substitute.For<IEmployeeSalaryLookup>();
        salary.GetEmploymentDatesAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeEmploymentDates?>(new EmployeeEmploymentDates(HireDate, null)));
        salary.GetSalaryAsOfAsync(TenantId, EmployeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeCurrentSalary?>(new EmployeeCurrentSalary(60_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, HireDate)));
        var seniority = Substitute.For<IEmployeeSeniorityLookup>();
        seniority.GetCountedSeniorityDaysAsync(TenantId, EmployeeId, Arg.Any<EmployeeSeniorityPurpose>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(400));
        var leave = Substitute.For<ILeaveBalanceLookup>();
        leave.GetRemainingAnnualLeaveDaysAsOfAsync(TenantId, EmployeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<decimal?>(10m));

        var settlements = Substitute.For<IExitSettlementRepository>();
        settlements.FindByEmployeeAndDateAsync(EmployeeId, TerminationDate, Arg.Any<CancellationToken>()).Returns(Task.FromResult(existing));
        var unitOfWork = Substitute.For<IPayrollUnitOfWork>();
        var estimator = new SeveranceEstimator(salary, seniority, leave, Legislation(legislationPresent), TimeProvider.System,
            Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IPayslipRepository>(), Substitute.For<IEmployeeDisabilityLookup>(),
            Substitute.For<IEmployeePayrollProfileRepository>());
        // Payroll's own legislation keys are absent here, so the net stays uncalculated; the net has its own tests.
        var netService = new ExitSettlementNetService(Legislation(legislationPresent), Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IPayslipRepository>());
        var handler = new EmployeeTerminatedSettlementHandler(
            settlements, estimator, netService, unitOfWork, TimeProvider.System, NullLogger<EmployeeTerminatedSettlementHandler>.Instance);
        return (handler, settlements, unitOfWork);
    }

    private static EmployeeTerminated Terminated(TerminationType type, DateOnly? date) =>
        new(TenantId, EmployeeId, type, "REASON", null, date);

    [Fact]
    public async Task Handle_RecordsTheFrozenAmounts_ForAnEmployerNoticeExit()
    {
        var (handler, settlements, unitOfWork) = Build();

        await handler.Handle(Terminated(TerminationType.ValidCause_Art18, TerminationDate), CancellationToken.None);

        // daily 60,000/30 = 2,000; 400 days -> kıdem 60,000 * 400/365 = 65,753.42; ihbar 4 hafta (<548 gün) = 2,000*28 = 56,000;
        // izin 10 gün = 20,000.
        settlements.Received(1).Add(Arg.Is<ExitSettlement>(s =>
            s.EmployeeId == EmployeeId && s.TerminationDate == TerminationDate
            && s.CauseCategory == SeveranceCauseCategory.EmployerNoticeBased
            && s.SeveranceAmount == 65_753.42m && s.NoticeAmount == 56_000m && s.UnusedLeaveAmount == 20_000m
            && s.TotalAmount == 141_753.42m && s.SeniorityDays == 400 && s.Currency == "TRY"));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAMutualRescission_PaysOnlyTheUnusedLeave()
    {
        var (handler, settlements, _) = Build();

        await handler.Handle(Terminated(TerminationType.MutualRescission, TerminationDate), CancellationToken.None);

        settlements.Received(1).Add(Arg.Is<ExitSettlement>(s =>
            s.CauseCategory == SeveranceCauseCategory.EmployeeResignation
            && s.SeveranceAmount == 0m && s.NoticeAmount == 0m && s.UnusedLeaveAmount == 20_000m && s.TotalAmount == 20_000m));
    }

    [Fact]
    public async Task Handle_WhenTheExitWasAlreadyRecorded_DoesNothing()
    {
        var data = new ExitSettlementData(EmployeeId, TerminationDate, SeveranceCauseCategory.EmployerNoticeBased, "TRY", 400, 2_000m, 1m, 1m, 1m, 3m, "[]");
        var existing = ExitSettlement.Create(TenantId, data, DateTimeOffset.UtcNow);
        var (handler, settlements, unitOfWork) = Build(existing: existing);

        await handler.Handle(Terminated(TerminationType.ValidCause_Art18, TerminationDate), CancellationToken.None);

        settlements.DidNotReceive().Add(Arg.Any<ExitSettlement>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheEventHasNoTerminationDate_DoesNothing()
    {
        var (handler, settlements, unitOfWork) = Build();

        await handler.Handle(Terminated(TerminationType.ValidCause_Art18, null), CancellationToken.None);

        settlements.DidNotReceive().Add(Arg.Any<ExitSettlement>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNoLegislationCoversTheDate_RecordsNothing_AndDoesNotThrow()
    {
        var (handler, settlements, unitOfWork) = Build(legislationPresent: false);

        await handler.Handle(Terminated(TerminationType.ValidCause_Art18, TerminationDate), CancellationToken.None);

        settlements.DidNotReceive().Add(Arg.Any<ExitSettlement>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHandler_ReturnsTheNewestSettlement_OrNotFound()
    {
        var older = ExitSettlement.Create(TenantId, new ExitSettlementData(
            EmployeeId, new DateOnly(2024, 1, 1), SeveranceCauseCategory.EmployerNoticeBased, "TRY", 10, 1m, 1m, 1m, 1m, 3m, "[]"), DateTimeOffset.UtcNow);
        var newer = ExitSettlement.Create(TenantId, new ExitSettlementData(
            EmployeeId, TerminationDate, SeveranceCauseCategory.EmployeeJustCause, "TRY", 400, 2_000m, 5m, 0m, 2m, 7m, "[]"), DateTimeOffset.UtcNow);
        var settlements = Substitute.For<IExitSettlementRepository>();
        settlements.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExitSettlement>>([newer, older]));
        settlements.ListByEmployeeAsync(Guid.Empty, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExitSettlement>>([]));
        var handler = new GetExitSettlementHandler(settlements);

        var found = await handler.Handle(new GetExitSettlementQuery(EmployeeId), CancellationToken.None);
        var missing = await handler.Handle(new GetExitSettlementQuery(Guid.Empty), CancellationToken.None);

        found.Value.TerminationDate.ShouldBe(TerminationDate);
        found.Value.TotalAmount.ShouldBe(7m);
        missing.IsFailure.ShouldBeTrue();
        missing.Error.Code.ShouldBe(PayrollDomainErrors.ExitSettlementNotFound);
        missing.Error.Type.ShouldBe(ErrorType.NotFound);
    }
}

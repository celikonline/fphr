using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Dekont eşleştirme uçtan uca: an uploaded CSV confirms the matching payslip and leaves the rest reported, not written.</summary>
public sealed class MatchBankReceiptsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Baran = Guid.NewGuid();

    private static Payslip PayslipOf(Guid employeeId, decimal net)
    {
        var payslip = (Payslip)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Payslip));
        typeof(Payslip).GetProperty(nameof(Payslip.EmployeeId))!.SetValue(payslip, employeeId);
        typeof(Payslip).GetProperty(nameof(Payslip.NetAmount))!.SetValue(payslip, net);
        return payslip;
    }

    private static MatchBankReceiptsHandler Handler(
        PayrollPeriod period, IReadOnlyList<Payslip> payslips, IReadOnlyList<EmployeePayoutAccount> accounts, out IEmployeeDirectory directory)
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var payslipRepository = Substitute.For<IPayslipRepository>();
        payslipRepository.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(payslips));
        directory = Substitute.For<IEmployeeDirectory>();
        directory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([]));
        var lookup = Substitute.For<IEmployeePayoutAccountLookup>();
        lookup.GetPrimaryPayoutAccountsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(accounts));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return new MatchBankReceiptsHandler(periods, payslipRepository, directory, lookup, tenant, TimeProvider.System, NullLogger<MatchBankReceiptsHandler>.Instance);
    }

    private static PayrollPeriod ApprovedPeriod()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        period.Approve(null);
        return period;
    }

    [Fact]
    public async Task Handle_ConfirmsThePayslip_WhenTheReceiptMatches()
    {
        var period = ApprovedPeriod();
        var ayse = PayslipOf(Ayse, 38_250.50m);
        var handler = Handler(period, [ayse], [new EmployeePayoutAccount(Ayse, "TR330006100519786457841326", "Banka A")], out _);

        var result = await handler.Handle(new MatchBankReceiptsCommand(period.Id, "TR330006100519786457841326;38250.50;Onaylandı"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ConfirmedCount.ShouldBe(1);
        ayse.PaymentConfirmedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_ReportsAMismatch_ButDoesNotConfirmThePayslip()
    {
        var period = ApprovedPeriod();
        var ayse = PayslipOf(Ayse, 100m);
        var handler = Handler(period, [ayse], [new EmployeePayoutAccount(Ayse, "TR330006100519786457841326", "Banka A")], out _);

        var result = await handler.Handle(new MatchBankReceiptsCommand(period.Id, "TR330006100519786457841326;90.00"), CancellationToken.None);

        result.Value.AmountMismatches.ShouldHaveSingleItem().EmployeeId.ShouldBe(Ayse);
        ayse.PaymentConfirmedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ReportsAnUnmatchedEmployee_WhenTheFileHasNoRowForThem()
    {
        var period = ApprovedPeriod();
        var handler = Handler(
            period, [PayslipOf(Ayse, 100m), PayslipOf(Baran, 200m)],
            [new EmployeePayoutAccount(Ayse, "TR330006100519786457841326", null), new EmployeePayoutAccount(Baran, "TR330006100519786457841327", null)], out _);

        var result = await handler.Handle(new MatchBankReceiptsCommand(period.Id, "TR330006100519786457841326;100.00"), CancellationToken.None);

        result.Value.ConfirmedCount.ShouldBe(1);
        result.Value.Unmatched.ShouldHaveSingleItem().EmployeeId.ShouldBe(Baran);
    }

    [Fact]
    public async Task Handle_IsRefused_ForAPeriodThatIsStillOpen()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 8).Value;

        var result = await Handler(period, [PayslipOf(Ayse, 1m)], [], out _).Handle(new MatchBankReceiptsCommand(period.Id, "x;1"), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.BankFilePeriodNotApproved);
    }

    [Fact]
    public async Task Handle_IsRefused_WhenTheCsvHasNoReadableRow()
    {
        var period = ApprovedPeriod();
        var handler = Handler(period, [PayslipOf(Ayse, 100m)], [new EmployeePayoutAccount(Ayse, "TR330006100519786457841326", null)], out _);

        var result = await handler.Handle(new MatchBankReceiptsCommand(period.Id, "IBAN;Tutar;Durum"), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.ReceiptCsvEmpty);
    }
}

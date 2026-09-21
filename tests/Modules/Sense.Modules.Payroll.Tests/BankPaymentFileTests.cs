using System.Text;
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

/// <summary>The generic bank payment list: its layout, and the guards that keep a partial or mismatched file from leaving the server.</summary>
public sealed class BankPaymentFileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Baran = Guid.NewGuid();

    [Fact]
    public void Build_WritesTheHeaderAndOneLinePerPayment_WithDotDecimals()
    {
        var csv = BankPaymentFileBuilder.Build(
            [new BankPayment("Ayşe Yılmaz", "TR33 0006 1005 1978 6457 8413 26", 38_250.5m, "Örnek Bank"), new BankPayment("Baran Demir", "TR330006100519786457841326", 41_000m, null)],
            "Maas 2026-08");

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("SiraNo;AdSoyad;IBAN;Banka;Tutar;ParaBirimi;Aciklama");
        lines[1].ShouldBe("1;\"Ayşe Yılmaz\";TR330006100519786457841326;\"Örnek Bank\";38250.50;TRY;\"Maas 2026-08\"");
        lines[2].ShouldBe("2;\"Baran Demir\";TR330006100519786457841326;\"\";41000.00;TRY;\"Maas 2026-08\"");
    }

    [Fact]
    public void Build_NeutralisesTextThatASpreadsheetWouldRunAsAFormula_AndEscapesQuotes()
    {
        var csv = BankPaymentFileBuilder.Build([new BankPayment("=HYPERLINK(\"x\")", "TR330006100519786457841326", 1m, null)], "Maas");

        csv.ShouldContain("\"'=HYPERLINK(\"\"x\"\")\"");
    }

    [Fact]
    public void Encode_StartsWithAUtf8ByteOrderMark()
    {
        var bytes = BankPaymentFileBuilder.Encode("Ş");

        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes.Skip(3).ToArray()).ShouldBe("Ş");
    }

    [Fact]
    public void Total_IsTheRoundedSumOfThePayments()
    {
        BankPaymentFileBuilder.Total([new BankPayment("A", "TR1", 100.10m, null), new BankPayment("B", "TR2", 200.205m, null)]).ShouldBe(300.31m);
    }

    private static Payslip PayslipOf(Guid employeeId, Guid periodId, decimal net)
    {
        var payslip = (Payslip)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Payslip));
        typeof(Payslip).GetProperty(nameof(Payslip.EmployeeId))!.SetValue(payslip, employeeId);
        typeof(Payslip).GetProperty(nameof(Payslip.PayrollPeriodId))!.SetValue(payslip, periodId);
        typeof(Payslip).GetProperty(nameof(Payslip.NetAmount))!.SetValue(payslip, net);
        return payslip;
    }

    private static ExportBankPaymentFileHandler Handler(PayrollPeriod period, IReadOnlyList<Payslip> payslips, IReadOnlyList<EmployeePayoutAccount> accounts)
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var payslipRepository = Substitute.For<IPayslipRepository>();
        payslipRepository.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(payslips));
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([]));
        var lookup = Substitute.For<IEmployeePayoutAccountLookup>();
        lookup.GetPrimaryPayoutAccountsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(accounts));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var user = Substitute.For<ICurrentUser>();
        return new ExportBankPaymentFileHandler(periods, payslipRepository, directory, lookup, tenant, user, NullLogger<ExportBankPaymentFileHandler>.Instance);
    }

    private static PayrollPeriod ApprovedPeriod()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        period.Approve();
        return period;
    }

    [Fact]
    public async Task Handle_BuildsTheFile_ForAnApprovedPeriodWithEveryAccount()
    {
        var period = ApprovedPeriod();
        var handler = Handler(period, [PayslipOf(Ayse, period.Id, 38_250.5m), PayslipOf(Baran, period.Id, 41_000m)],
            [new EmployeePayoutAccount(Ayse, "TR330006100519786457841326", "Banka A"), new EmployeePayoutAccount(Baran, "TR330006100519786457841327", "Banka B")]);

        var result = await handler.Handle(new ExportBankPaymentFileQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("banka-odeme_2026-08.csv");
        var text = Encoding.UTF8.GetString(result.Value.Content.Skip(3).ToArray());
        text.ShouldContain("38250.50");
        text.ShouldContain("41000.00");
    }

    [Fact]
    public async Task Handle_IsRefused_ForAPeriodThatIsStillOpen()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 8).Value;

        var result = await Handler(period, [PayslipOf(Ayse, period.Id, 1m)], []).Handle(new ExportBankPaymentFileQuery(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.BankFilePeriodNotApproved);
    }

    [Fact]
    public async Task Handle_IsRefused_WhenAnyEmployeeHasNoPrimaryAccount()
    {
        var period = ApprovedPeriod();
        var handler = Handler(period, [PayslipOf(Ayse, period.Id, 10m), PayslipOf(Baran, period.Id, 20m)],
            [new EmployeePayoutAccount(Ayse, "TR330006100519786457841326", null)]);

        var result = await handler.Handle(new ExportBankPaymentFileQuery(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.BankFileMissingAccounts);
    }

    [Fact]
    public async Task Handle_IsRefused_WhenNoPayslipHasANetToPay()
    {
        var period = ApprovedPeriod();

        var result = await Handler(period, [PayslipOf(Ayse, period.Id, 0m)], []).Handle(new ExportBankPaymentFileQuery(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.BankFileNoPayments);
    }
}

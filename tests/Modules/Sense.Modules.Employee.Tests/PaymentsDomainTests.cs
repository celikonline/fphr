using System.Globalization;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>E3 rules on the Employee aggregate: IBAN acceptance, salary history uniqueness/current resolution, primary bank account invariants.</summary>
public sealed class PaymentsDomainTests
{
    internal const string IbanA = "TR33 0006 1005 1978 6457 8413 26";
    internal const string IbanB = "TR130006200000001234567890";
    internal const string IbanC = "TR200001000000009876543210";

    private static readonly DateOnly Today = new(2026, 9, 15);
    private static readonly ReversibleProtector Protector = new();

    private static EmployeeEntity NewEmployee() => EmployeeEntity.Create(Guid.NewGuid(), new EmployeeInfo("Ali", "Kaya")).Value;

    private static SalaryRecordData Salary(DateOnly from, decimal amount = 50_000m, string currency = "TRY") =>
        new(from, amount, currency, SalaryType.Gross, PayFrequency.Monthly, 1_000m, 2_500.50m, "Zam");

    private static BankAccountData Account(string? iban, BankAccountType type = BankAccountType.DemandDeposit) => new(iban, type, "Ziraat", "TRY");

    [Theory]
    [InlineData(IbanA, "TR330006100519786457841326")]
    [InlineData("tr13 0006 2000 0000 1234 5678 90", "TR130006200000001234567890")]
    [InlineData(" TR200001000000009876543210 ", "TR200001000000009876543210")]
    public void ParseIban_NormalizesSpacesAndCase(string raw, string normalized) =>
        BankAccount.ParseIban(raw).Value.Value.ShouldBe(normalized);

    [Theory]
    [InlineData(null, EmployeeDomainErrors.IbanRequired)]
    [InlineData("  ", EmployeeDomainErrors.IbanRequired)]
    [InlineData("TR33 0006 1005 1978 6457 8413 27", EmployeeDomainErrors.IbanInvalid)]
    [InlineData("TR33000610051978645784132", EmployeeDomainErrors.IbanInvalid)]
    [InlineData("TR3300061005197864578413261", EmployeeDomainErrors.IbanInvalid)]
    [InlineData("TR33 0006 1005 1978 6457 8413 2X", EmployeeDomainErrors.IbanInvalid)]
    [InlineData("not an iban", EmployeeDomainErrors.IbanInvalid)]
    [InlineData("GB82 WEST 1234 5698 7654 32", EmployeeDomainErrors.IbanCountryUnsupported)]
    public void ParseIban_RejectsMissingMalformedChecksumAndForeign(string? raw, string code) =>
        BankAccount.ParseIban(raw).Error.Code.ShouldBe(code);

    [Fact]
    public void SalaryHistory_OneRecordPerEffectiveDate_AndCurrentIsLatestStartedRecord()
    {
        var employee = NewEmployee();
        employee.CurrentSalaryRecord(Today).ShouldBeNull();

        var first = employee.AddSalaryRecord(Salary(new DateOnly(2025, 1, 1), 40_000m)).Value;
        var raise = employee.AddSalaryRecord(Salary(new DateOnly(2026, 1, 1), 50_000m)).Value;
        var future = employee.AddSalaryRecord(Salary(new DateOnly(2027, 1, 1), 60_000m)).Value;

        (first.Changed, first.EffectiveFrom).ShouldBe((true, new DateOnly(2025, 1, 1)));
        employee.CurrentSalaryRecord(Today).ShouldBe(raise.Record);
        employee.CurrentSalaryRecord(new DateOnly(2027, 1, 1)).ShouldBe(future.Record);
        employee.CurrentSalaryRecord(new DateOnly(2024, 12, 31)).ShouldBeNull();
        employee.SalaryHistory.Select(r => r.Amount).ShouldBe([60_000m, 50_000m, 40_000m]);

        ShouldFail(employee.AddSalaryRecord(Salary(new DateOnly(2026, 1, 1))), EmployeeDomainErrors.SalaryEffectiveDateDuplicate, ErrorType.Conflict);
        ShouldFail(employee.UpdateSalaryRecord(future.Record.Id, Salary(new DateOnly(2025, 1, 1))), EmployeeDomainErrors.SalaryEffectiveDateDuplicate, ErrorType.Conflict);
        ShouldFail(employee.UpdateSalaryRecord(Guid.NewGuid(), Salary(new DateOnly(2028, 1, 1))), EmployeeDomainErrors.SalaryRecordNotFound, ErrorType.NotFound);
    }

    [Fact]
    public void UpdateSalary_ReportsNoOp_AndEarliestAffectedDate_DeleteReportsRemovedRecord()
    {
        var employee = NewEmployee();
        var record = employee.AddSalaryRecord(Salary(new DateOnly(2026, 3, 1))).Value.Record;

        employee.UpdateSalaryRecord(record.Id, Salary(new DateOnly(2026, 3, 1)) with { Currency = " try " }).Value.Changed.ShouldBeFalse();

        var movedEarlier = employee.UpdateSalaryRecord(record.Id, Salary(new DateOnly(2026, 2, 1))).Value;
        (movedEarlier.Changed, movedEarlier.EffectiveFrom).ShouldBe((true, new DateOnly(2026, 2, 1)));
        var movedLater = employee.UpdateSalaryRecord(record.Id, Salary(new DateOnly(2026, 4, 1), 55_000m)).Value;
        (movedLater.Changed, movedLater.EffectiveFrom, record.Amount).ShouldBe((true, new DateOnly(2026, 2, 1), 55_000m));

        var deleted = employee.DeleteSalaryRecord(record.Id).Value;
        (deleted.Record.Id, deleted.EffectiveFrom).ShouldBe((record.Id, new DateOnly(2026, 4, 1)));
        employee.SalaryRecords.ShouldBeEmpty();
        ShouldFail(employee.DeleteSalaryRecord(record.Id), EmployeeDomainErrors.SalaryRecordNotFound, ErrorType.NotFound);
    }

    [Theory]
    [InlineData("0", "TRY", null, EmployeeDomainErrors.SalaryAmountInvalid)]
    [InlineData("-1", "TRY", null, EmployeeDomainErrors.SalaryAmountInvalid)]
    [InlineData("100.123", "TRY", null, EmployeeDomainErrors.SalaryAmountInvalid)]
    [InlineData("1000000000000", "TRY", null, EmployeeDomainErrors.SalaryAmountInvalid)]
    [InlineData("100", "", null, EmployeeDomainErrors.CurrencyRequired)]
    [InlineData("100", "TL", null, EmployeeDomainErrors.CurrencyInvalid)]
    [InlineData("100", "XYZ", null, EmployeeDomainErrors.CurrencyInvalid)]
    [InlineData("100", "TRY", "-5", EmployeeDomainErrors.SalaryAllowanceInvalid)]
    [InlineData("100", "TRY", "1.005", EmployeeDomainErrors.SalaryAllowanceInvalid)]
    public void SalaryValidation_RejectsInvalidAmountCurrencyAndAllowance(string amount, string currency, string? transport, string code)
    {
        var data = new SalaryRecordData(
            Today, decimal.Parse(amount, CultureInfo.InvariantCulture), currency, SalaryType.Net, PayFrequency.Hourly,
            transport is null ? null : decimal.Parse(transport, CultureInfo.InvariantCulture));
        ShouldFail(NewEmployee().AddSalaryRecord(data), code, ErrorType.Validation);
    }

    [Fact]
    public void SalaryCurrency_IsNormalizedToUppercase_AndZeroAllowanceIsAccepted()
    {
        var record = NewEmployee().AddSalaryRecord(new SalaryRecordData(Today, 12.5m, " eur ", SalaryType.Net, PayFrequency.Daily, 0m, 0m)).Value.Record;
        (record.Currency, record.TransportAllowance, record.Source).ShouldBe(("EUR", 0m, SalarySource.Manual));
    }

    [Fact]
    public void BankAccounts_FirstIsPrimary_ExactlyOnePrimary_AndDeletingPrimaryPromotesOldest()
    {
        var employee = NewEmployee();
        var a = employee.AddBankAccount(Account(IbanA), makePrimary: false, Protector).Value;
        a.IsPrimary.ShouldBeTrue();
        (a.ProtectedIban, a.MaskedIban).ShouldBe(("enc:TR330006100519786457841326", "TR** **** **** **** **** **13 26"));

        // Creation order is the version-7 id order, which has millisecond resolution.
        Thread.Sleep(2);
        var b = employee.AddBankAccount(Account(IbanB), makePrimary: false, Protector).Value;
        Thread.Sleep(2);
        var c = employee.AddBankAccount(Account(IbanC), makePrimary: true, Protector).Value;
        employee.BankAccounts.Count(x => x.IsPrimary).ShouldBe(1);
        c.IsPrimary.ShouldBeTrue();
        employee.OrderedBankAccounts.Select(x => x.Id).ShouldBe([c.Id, a.Id, b.Id]);

        employee.SetPrimaryBankAccount(b.Id).Value.ShouldBeTrue();
        employee.SetPrimaryBankAccount(b.Id).Value.ShouldBeFalse();
        (a.IsPrimary, b.IsPrimary, c.IsPrimary).ShouldBe((false, true, false));

        employee.DeleteBankAccount(b.Id).IsSuccess.ShouldBeTrue();
        (a.IsPrimary, c.IsPrimary).ShouldBe((true, false));
        employee.DeleteBankAccount(c.Id).IsSuccess.ShouldBeTrue();
        employee.DeleteBankAccount(a.Id).IsSuccess.ShouldBeTrue();
        employee.BankAccounts.ShouldBeEmpty();
        employee.DeleteBankAccount(a.Id).Error.Code.ShouldBe(EmployeeDomainErrors.BankAccountNotFound);
    }

    [Fact]
    public void BankAccounts_IbanUniquePerEmployee_EditKeepsOrReplacesIban()
    {
        var employee = NewEmployee();
        var a = employee.AddBankAccount(Account(IbanA), makePrimary: false, Protector).Value;
        var b = employee.AddBankAccount(Account(IbanB), makePrimary: false, Protector).Value;

        ShouldFail(employee.AddBankAccount(Account("tr330006100519786457841326"), makePrimary: false, Protector), EmployeeDomainErrors.IbanDuplicate, ErrorType.Conflict);
        ShouldFail(employee.UpdateBankAccount(b.Id, Account(IbanA), Protector), EmployeeDomainErrors.IbanDuplicate, ErrorType.Conflict);
        ShouldFail(employee.UpdateBankAccount(b.Id, Account("TR00 1234"), Protector), EmployeeDomainErrors.IbanInvalid, ErrorType.Validation);

        // Null IBAN keeps the stored one; resending the same IBAN (any spacing) is not a change.
        employee.UpdateBankAccount(a.Id, Account(null), Protector).Value.ShouldBeFalse();
        employee.UpdateBankAccount(a.Id, Account(IbanA.Replace(" ", string.Empty, StringComparison.Ordinal)), Protector).Value.ShouldBeFalse();
        employee.UpdateBankAccount(a.Id, Account(null, BankAccountType.TimeDeposit), Protector).Value.ShouldBeTrue();

        employee.UpdateBankAccount(a.Id, Account(IbanC, BankAccountType.TimeDeposit), Protector).Value.ShouldBeTrue();
        (a.ProtectedIban, a.MaskedIban, a.IsPrimary).ShouldBe(("enc:TR200001000000009876543210", "TR** **** **** **** **** **32 10", true));

        // The freed IBAN can be used again by another account of the same employee.
        employee.AddBankAccount(Account(IbanA), makePrimary: false, Protector).IsSuccess.ShouldBeTrue();
        ShouldFail(employee.AddBankAccount(new BankAccountData(IbanB.Replace("TR13", "TR14", StringComparison.Ordinal), BankAccountType.Other), false, Protector), EmployeeDomainErrors.IbanInvalid, ErrorType.Validation);
        ShouldFail(employee.AddBankAccount(new BankAccountData(IbanC, BankAccountType.Other, Currency: "EURO"), false, Protector), EmployeeDomainErrors.CurrencyInvalid, ErrorType.Validation);
    }

    private static void ShouldFail<T>(Result<T> result, string code, ErrorType type)
    {
        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Type).ShouldBe((code, type));
    }

    internal sealed class ReversibleProtector : IIbanProtector
    {
        private const string Prefix = "enc:";

        public string Protect(string normalizedIban) => Prefix + normalizedIban;

        public string Reveal(string protectedIban) => protectedIban[Prefix.Length..];
    }
}

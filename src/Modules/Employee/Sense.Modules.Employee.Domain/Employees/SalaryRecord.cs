using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>"Maaş türü" (11.3.03): the amount is net or gross ("asgari" is not a type).</summary>
public enum SalaryType
{
    Net = 0,
    Gross = 1,
}

/// <summary>"Ödeme düzeni" (11.3.03); Payroll reads it as the wage basis (11.3.07).</summary>
public enum PayFrequency
{
    Monthly = 0,
    Daily = 1,
    Yearly = 2,
    Weekly = 3,
    Hourly = 4,
}

/// <summary>Where a salary record came from; only <see cref="Manual"/> is written today (import: E6, compensation/wizard: later).</summary>
public enum SalarySource
{
    Manual = 0,
    Import = 1,
    Compensation = 2,
    Wizard = 3,
}

/// <summary>Editable content of a salary record ("Maaş bloğu").</summary>
public sealed record SalaryRecordData(
    DateOnly EffectiveFrom,
    decimal Amount,
    string Currency,
    SalaryType SalaryType,
    PayFrequency PayFrequency,
    decimal? TransportAllowance = null,
    decimal? MealAllowance = null,
    string? Note = null,
    SalarySource Source = SalarySource.Manual);

/// <summary>
/// Result of a salary mutation: the affected record, whether anything changed (no event for a no-op) and the earliest
/// date whose effective salary changed (min of old/new EffectiveFrom on an edit that moved the date).
/// </summary>
public sealed record SalaryOutcome(SalaryRecord Record, bool Changed, DateOnly EffectiveFrom);

/// <summary>
/// One entry of an employee's salary history (11.3.03 <c>SalaryHistory</c>): in effect from <see cref="EffectiveFrom"/>
/// until the next record starts. Child entity of the <see cref="Employee"/> aggregate so the per-employee rules (one
/// record per effective date, current-record resolution) stay inside it. Amounts are plain numeric columns - Payroll and
/// reporting aggregate them - and are protected by employee.salary.view plus DTO omission, not by encryption.
/// </summary>
public sealed class SalaryRecord : TenantEntity<Guid>
{
    internal SalaryRecord(Guid id, Guid tenantId, Guid employeeId, SalaryRecordData data)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Source = data.Source;
        Apply(data);
    }

    private SalaryRecord()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code (uppercase).</summary>
    public string Currency { get; private set; } = Currencies.TRY;

    public SalaryType SalaryType { get; private set; }

    public PayFrequency PayFrequency { get; private set; }

    public decimal? TransportAllowance { get; private set; }

    public decimal? MealAllowance { get; private set; }

    public SalarySource Source { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Applies the editable content (Source is kept); returns true when something actually changed.</summary>
    internal bool Apply(SalaryRecordData data)
    {
        var note = Trim(data.Note);
        var currency = NormalizeCurrency(data.Currency)!;
        var changed = EffectiveFrom != data.EffectiveFrom ||
                      Amount != data.Amount ||
                      !string.Equals(Currency, currency, StringComparison.Ordinal) ||
                      SalaryType != data.SalaryType ||
                      PayFrequency != data.PayFrequency ||
                      TransportAllowance != data.TransportAllowance ||
                      MealAllowance != data.MealAllowance ||
                      !string.Equals(Note, note, StringComparison.Ordinal);

        EffectiveFrom = data.EffectiveFrom;
        Amount = data.Amount;
        Currency = currency;
        SalaryType = data.SalaryType;
        PayFrequency = data.PayFrequency;
        TransportAllowance = data.TransportAllowance;
        MealAllowance = data.MealAllowance;
        Note = note;
        return changed;
    }

    /// <summary>Content rules that do not need the rest of the aggregate.</summary>
    public static Result Validate(SalaryRecordData data)
    {
        if (!IsValidAmount(data.Amount, allowZero: false))
        {
            return Error.Validation(EmployeeDomainErrors.SalaryAmountInvalid);
        }

        if (NormalizeCurrency(data.Currency) is not { } currency)
        {
            return Error.Validation(EmployeeDomainErrors.CurrencyRequired);
        }

        if (!Currencies.IsKnown(currency))
        {
            return Error.Validation(EmployeeDomainErrors.CurrencyInvalid);
        }

        if (!Enum.IsDefined(data.SalaryType))
        {
            return Error.Validation(EmployeeDomainErrors.SalaryTypeRequired);
        }

        if (!Enum.IsDefined(data.PayFrequency))
        {
            return Error.Validation(EmployeeDomainErrors.SalaryPayFrequencyRequired);
        }

        if ((data.TransportAllowance is { } transport && !IsValidAmount(transport, allowZero: true)) ||
            (data.MealAllowance is { } meal && !IsValidAmount(meal, allowZero: true)))
        {
            return Error.Validation(EmployeeDomainErrors.SalaryAllowanceInvalid);
        }

        if (Trim(data.Note) is { Length: > EmployeeLimits.SalaryNoteMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.SalaryNoteTooLong);
        }

        return Result.Success();
    }

    /// <summary>Kuruş precision (at most 2 decimals), below <see cref="EmployeeLimits.MoneyMax"/>; strictly positive unless <paramref name="allowZero"/>.</summary>
    public static bool IsValidAmount(decimal amount, bool allowZero) =>
        (allowZero ? amount >= 0m : amount > 0m) &&
        amount < EmployeeLimits.MoneyMax &&
        decimal.Round(amount, EmployeeLimits.MoneyScale) == amount;

    /// <summary>Trimmed, uppercase ISO code; null when blank (shared with bank accounts).</summary>
    public static string? NormalizeCurrency(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

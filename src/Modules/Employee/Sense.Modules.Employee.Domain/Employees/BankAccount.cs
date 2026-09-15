using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>"Banka hesap tipi" (11.3.03); a currency phrase like "Euro hesabı" is not an account type.</summary>
public enum BankAccountType
{
    Cheque = 0,
    TimeDeposit = 1,
    DemandDeposit = 2,
    Other = 3,
}

/// <summary>
/// Encrypts/decrypts a normalized IBAN for storage (AES-GCM behind the shared IEncryptionProvider). A domain port so
/// the aggregate can compare an incoming IBAN with the stored ones (IBAN unique per employee) without ever persisting
/// the plain value.
/// </summary>
public interface IIbanProtector
{
    string Protect(string normalizedIban);

    string Reveal(string protectedIban);
}

/// <summary>
/// Editable content of a bank account. <see cref="Iban"/> is the raw user input (spaces/lowercase allowed); on an edit,
/// null keeps the stored IBAN (a caller without employee.sensitive.view only ever saw the masked value).
/// </summary>
public sealed record BankAccountData(
    string? Iban,
    BankAccountType AccountType,
    string? BankName = null,
    string? Currency = null,
    string? Note = null);

/// <summary>
/// An employee's bank account (11.3.03 <c>BankAccount</c>). Child entity of the <see cref="Employee"/> aggregate, which
/// keeps "exactly one primary account while any exist" and "IBAN unique per employee". The IBAN is stored only as
/// AES-GCM ciphertext (<see cref="ProtectedIban"/>) plus a display-safe mask (<see cref="MaskedIban"/>, country code
/// and last 4 characters), so masked reads never decrypt.
/// </summary>
public sealed class BankAccount : TenantEntity<Guid>
{
    /// <summary>Countries whose IBANs are accepted today; widen together with payroll bank-file support.</summary>
    public static readonly IReadOnlySet<string> SupportedCountries = new HashSet<string>(StringComparer.Ordinal) { "TR" };

    internal BankAccount(Guid id, Guid tenantId, Guid employeeId, string protectedIban, string maskedIban, BankAccountData data)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        ProtectedIban = protectedIban;
        MaskedIban = maskedIban;
        Apply(data);
    }

    private BankAccount()
    {
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>AES-GCM ciphertext of the normalized IBAN (see <see cref="IIbanProtector"/>).</summary>
    public string ProtectedIban { get; private set; } = string.Empty;

    /// <summary>"TR** **** **** **** **** **12 34".</summary>
    public string MaskedIban { get; private set; } = string.Empty;

    public BankAccountType AccountType { get; private set; }

    public string? BankName { get; private set; }

    /// <summary>ISO 4217 code of the account, when known.</summary>
    public string? Currency { get; private set; }

    public bool IsPrimary { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Parses raw input into an accepted IBAN: required, well-formed with a valid checksum, of a supported country.</summary>
    public static Result<Iban> ParseIban(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Error.Validation(EmployeeDomainErrors.IbanRequired);
        }

        if (!Iban.TryCreate(raw, out var iban) || iban is null)
        {
            return Error.Validation(EmployeeDomainErrors.IbanInvalid);
        }

        return SupportedCountries.Contains(iban.CountryCode)
            ? iban
            : Error.Validation(EmployeeDomainErrors.IbanCountryUnsupported);
    }

    /// <summary>Content rules other than the IBAN (which <see cref="ParseIban"/> checks).</summary>
    public static Result Validate(BankAccountData data)
    {
        if (!Enum.IsDefined(data.AccountType))
        {
            return Error.Validation(EmployeeDomainErrors.BankAccountTypeRequired);
        }

        if (Trim(data.BankName) is { Length: > EmployeeLimits.BankNameMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.BankNameTooLong);
        }

        if (SalaryRecord.NormalizeCurrency(data.Currency) is { } currency && !Currencies.IsKnown(currency))
        {
            return Error.Validation(EmployeeDomainErrors.CurrencyInvalid);
        }

        if (Trim(data.Note) is { Length: > EmployeeLimits.BankAccountNoteMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.BankAccountNoteTooLong);
        }

        return Result.Success();
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    internal void ReplaceIban(string protectedIban, string maskedIban)
    {
        ProtectedIban = protectedIban;
        MaskedIban = maskedIban;
    }

    /// <summary>Applies everything but the IBAN and the primary flag; returns true when something changed.</summary>
    internal bool Apply(BankAccountData data)
    {
        var bankName = Trim(data.BankName);
        var currency = SalaryRecord.NormalizeCurrency(data.Currency);
        var note = Trim(data.Note);
        var changed = AccountType != data.AccountType ||
                      !string.Equals(BankName, bankName, StringComparison.Ordinal) ||
                      !string.Equals(Currency, currency, StringComparison.Ordinal) ||
                      !string.Equals(Note, note, StringComparison.Ordinal);

        AccountType = data.AccountType;
        BankName = bankName;
        Currency = currency;
        Note = note;
        return changed;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

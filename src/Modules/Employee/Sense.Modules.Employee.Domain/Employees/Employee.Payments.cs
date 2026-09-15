using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Salary history and bank accounts ("Ödemeler", E3). Unlike career records these collections are not auto-included:
/// they are loaded only by the payment use cases, so salary amounts and IBAN ciphertext never ride along with the
/// employee loads every other screen makes. Every method here therefore assumes they were loaded.
/// </summary>
public sealed partial class Employee
{
    private readonly List<SalaryRecord> _salaryRecords = [];
    private readonly List<BankAccount> _bankAccounts = [];

    /// <summary>Salary records (EF navigation; use <see cref="SalaryHistory"/> for the ordered view).</summary>
    public IReadOnlyCollection<SalaryRecord> SalaryRecords => _salaryRecords.AsReadOnly();

    /// <summary>Salary records, newest effective date first.</summary>
    public IReadOnlyList<SalaryRecord> SalaryHistory => _salaryRecords.OrderByDescending(r => r.EffectiveFrom).ToList();

    public IReadOnlyCollection<BankAccount> BankAccounts => _bankAccounts.AsReadOnly();

    /// <summary>Primary account first, then in creation order (ids are version-7 GUIDs, which sort by creation time).</summary>
    public IReadOnlyList<BankAccount> OrderedBankAccounts =>
        _bankAccounts.OrderByDescending(a => a.IsPrimary).ThenBy(a => a.Id).ToList();

    // ------------------------------------------------------------------ salary history

    /// <summary>The salary in effect on <paramref name="today"/>: the latest record that has started; null when none has.</summary>
    public SalaryRecord? CurrentSalaryRecord(DateOnly today) =>
        _salaryRecords.Where(r => r.EffectiveFrom <= today).MaxBy(r => r.EffectiveFrom);

    public Result<SalaryOutcome> AddSalaryRecord(SalaryRecordData data)
    {
        var validated = SalaryRecord.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        if (_salaryRecords.Any(r => r.EffectiveFrom == data.EffectiveFrom))
        {
            return Error.Conflict(EmployeeDomainErrors.SalaryEffectiveDateDuplicate);
        }

        var record = new SalaryRecord(Guid.CreateVersion7(), TenantId, Id, data);
        _salaryRecords.Add(record);
        return new SalaryOutcome(record, Changed: true, record.EffectiveFrom);
    }

    /// <summary>Edits a salary record (its <see cref="SalaryRecord.Source"/> is kept).</summary>
    public Result<SalaryOutcome> UpdateSalaryRecord(Guid recordId, SalaryRecordData data)
    {
        var validated = SalaryRecord.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        if (_salaryRecords.FirstOrDefault(r => r.Id == recordId) is not { } record)
        {
            return Error.NotFound(EmployeeDomainErrors.SalaryRecordNotFound);
        }

        if (_salaryRecords.Any(r => r.Id != recordId && r.EffectiveFrom == data.EffectiveFrom))
        {
            return Error.Conflict(EmployeeDomainErrors.SalaryEffectiveDateDuplicate);
        }

        var oldEffectiveFrom = record.EffectiveFrom;
        var changed = record.Apply(data);
        var earliest = oldEffectiveFrom < record.EffectiveFrom ? oldEffectiveFrom : record.EffectiveFrom;
        return new SalaryOutcome(record, changed, earliest);
    }

    public Result<SalaryOutcome> DeleteSalaryRecord(Guid recordId)
    {
        if (_salaryRecords.FirstOrDefault(r => r.Id == recordId) is not { } record)
        {
            return Error.NotFound(EmployeeDomainErrors.SalaryRecordNotFound);
        }

        _salaryRecords.Remove(record);
        return new SalaryOutcome(record, Changed: true, record.EffectiveFrom);
    }

    // ------------------------------------------------------------------ bank accounts

    /// <summary>Adds an account; the first account, or one added with <paramref name="makePrimary"/>, becomes the primary one.</summary>
    public Result<BankAccount> AddBankAccount(BankAccountData data, bool makePrimary, IIbanProtector protector)
    {
        var iban = BankAccount.ParseIban(data.Iban);
        if (iban.IsFailure)
        {
            return iban.Error;
        }

        var validated = BankAccount.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        if (HasIban(iban.Value.Value, exceptAccountId: null, protector))
        {
            return Error.Conflict(EmployeeDomainErrors.IbanDuplicate);
        }

        var account = new BankAccount(Guid.CreateVersion7(), TenantId, Id, protector.Protect(iban.Value.Value), iban.Value.MaskedFormatted, data);
        _bankAccounts.Add(account);
        if (makePrimary || _bankAccounts.Count == 1)
        {
            MakePrimary(account);
        }

        return account;
    }

    /// <summary>Edits an account; a null <see cref="BankAccountData.Iban"/> keeps the stored one. Returns true when something changed.</summary>
    public Result<bool> UpdateBankAccount(Guid accountId, BankAccountData data, IIbanProtector protector)
    {
        if (_bankAccounts.FirstOrDefault(a => a.Id == accountId) is not { } account)
        {
            return Error.NotFound(EmployeeDomainErrors.BankAccountNotFound);
        }

        var validated = BankAccount.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var ibanChanged = false;
        if (data.Iban is not null)
        {
            var iban = BankAccount.ParseIban(data.Iban);
            if (iban.IsFailure)
            {
                return iban.Error;
            }

            var normalized = iban.Value.Value;
            if (!string.Equals(protector.Reveal(account.ProtectedIban), normalized, StringComparison.Ordinal))
            {
                if (HasIban(normalized, account.Id, protector))
                {
                    return Error.Conflict(EmployeeDomainErrors.IbanDuplicate);
                }

                account.ReplaceIban(protector.Protect(normalized), iban.Value.MaskedFormatted);
                ibanChanged = true;
            }
        }

        return Result.Success(account.Apply(data) || ibanChanged);
    }

    /// <summary>Deletes an account; when it was the primary one, the oldest remaining account becomes primary.</summary>
    public Result DeleteBankAccount(Guid accountId)
    {
        if (_bankAccounts.FirstOrDefault(a => a.Id == accountId) is not { } account)
        {
            return Error.NotFound(EmployeeDomainErrors.BankAccountNotFound);
        }

        _bankAccounts.Remove(account);
        if (account.IsPrimary && OrderedBankAccounts.FirstOrDefault() is { } next)
        {
            MakePrimary(next);
        }

        return Result.Success();
    }

    /// <summary>Makes the account the single primary one; returns false when it already was.</summary>
    public Result<bool> SetPrimaryBankAccount(Guid accountId)
    {
        if (_bankAccounts.FirstOrDefault(a => a.Id == accountId) is not { } account)
        {
            return Error.NotFound(EmployeeDomainErrors.BankAccountNotFound);
        }

        if (account.IsPrimary)
        {
            return Result.Success(false);
        }

        MakePrimary(account);
        return Result.Success(true);
    }

    private void MakePrimary(BankAccount primary)
    {
        foreach (var account in _bankAccounts)
        {
            account.SetPrimary(account.Id == primary.Id);
        }
    }

    private bool HasIban(string normalizedIban, Guid? exceptAccountId, IIbanProtector protector) =>
        _bankAccounts.Any(a => a.Id != exceptAccountId && string.Equals(protector.Reveal(a.ProtectedIban), normalizedIban, StringComparison.Ordinal));
}

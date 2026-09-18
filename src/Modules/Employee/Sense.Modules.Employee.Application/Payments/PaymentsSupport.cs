using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.ValueObjects;
using ContractEvents = Sense.Modules.Employee.Contracts.Events;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Payments;

/// <summary>
/// A salary record on the card's "Ödemeler" tab (salaryType/payFrequency/source as camelCase enum strings).
/// "Oluşturan"/"Oluşturulma Tarihi" columns: <see cref="CreatedByUserId"/>/<see cref="CreatedAt"/> come from the
/// entity's IAuditable columns (AuditTenantInterceptor fills them on insert, no new schema needed);
/// <see cref="CreatedByName"/> is the resolved display name, null when <see cref="IUserProfileLookup"/> finds no
/// match (e.g. the user account was later deleted).
/// </summary>
public sealed record SalaryRecordDto(
    Guid Id,
    DateOnly EffectiveFrom,
    decimal Amount,
    string Currency,
    SalaryType SalaryType,
    PayFrequency PayFrequency,
    decimal? TransportAllowance,
    decimal? MealAllowance,
    SalarySource Source,
    string? Note,
    bool IsCurrent,
    Guid? CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAt);

/// <summary>
/// A bank account. <see cref="Iban"/> is grouped ("TR33 0006 …") for callers with employee.sensitive.view (and for the
/// employee's own card), otherwise the stored mask ("TR** **** **** **** **** **12 34") with <see cref="IsIbanMasked"/> set.
/// </summary>
public sealed record BankAccountDto(
    Guid Id,
    string Iban,
    bool IsIbanMasked,
    BankAccountType AccountType,
    string? BankName,
    string? Currency,
    bool IsPrimary,
    string? Note);

/// <summary>AES-GCM (shared <see cref="IEncryptionProvider"/>) implementation of the domain's IBAN port.</summary>
public sealed class EncryptedIbanProtector(IEncryptionProvider encryption) : IIbanProtector
{
    public string Protect(string normalizedIban) => encryption.Encrypt(normalizedIban);

    public string Reveal(string protectedIban) => encryption.Decrypt(protectedIban);
}

/// <summary>Builds the payment DTOs from a loaded aggregate and raises the payment integration events.</summary>
public sealed class PaymentsService(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IIbanProtector protector,
    IIntegrationEventOutbox outbox,
    IUserProfileLookup users,
    TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Newest effective date first; <see cref="SalaryRecordDto.IsCurrent"/> marks the record in effect today (UTC).</summary>
    public async Task<IReadOnlyList<SalaryRecordDto>> SalariesAsync(EmployeeEntity employee, CancellationToken cancellationToken)
    {
        var current = employee.CurrentSalaryRecord(Today);
        var creatorIds = employee.SalaryHistory.Where(r => r.CreatedUserId is not null).Select(r => r.CreatedUserId!.Value).Distinct().ToList();
        var names = creatorIds.Count == 0
            ? new Dictionary<Guid, UserProfileInfo>()
            : await users.GetAsync(tenant.TenantId, creatorIds, cancellationToken);

        return employee.SalaryHistory
            .Select(r => new SalaryRecordDto(
                r.Id, r.EffectiveFrom, r.Amount, r.Currency, r.SalaryType, r.PayFrequency, r.TransportAllowance, r.MealAllowance,
                r.Source, r.Note, r.Id == current?.Id,
                r.CreatedUserId,
                r.CreatedUserId is { } creatorId && names.TryGetValue(creatorId, out var info) ? info.DisplayName : null,
                r.CreatedAt))
            .ToList();
    }

    /// <summary>Primary first; IBAN decrypted only when <paramref name="unmasked"/>.</summary>
    public IReadOnlyList<BankAccountDto> BankAccounts(EmployeeEntity employee, bool unmasked) =>
        employee.OrderedBankAccounts
            .Select(a => new BankAccountDto(
                a.Id,
                unmasked ? Iban.Of(protector.Reveal(a.ProtectedIban)).Formatted : a.MaskedIban,
                !unmasked,
                a.AccountType,
                a.BankName,
                a.Currency,
                a.IsPrimary,
                a.Note))
            .ToList();

    public void PublishSalaryChanged(EmployeeEntity employee, SalaryOutcome outcome)
    {
        if (!outcome.Changed)
        {
            return;
        }

        outbox.Enqueue(new ContractEvents.SalaryChanged(tenant.TenantId, employee.Id, outcome.Record.Id, outcome.EffectiveFrom)
        {
            ActorUserId = currentUser.UserId,
        });
    }

    public void PublishBankChanged(EmployeeEntity employee) =>
        outbox.Enqueue(new ContractEvents.EmployeeProfileChanged(tenant.TenantId, employee.Id, ContractEvents.EmployeeProfileSections.Bank)
        {
            ActorUserId = currentUser.UserId,
        });
}

/// <summary>Resolves the caller's own employee id for the "/me" reads (same fallback as GetMyEmployeeHandler: the eid claim can lag behind a link made after sign-in).</summary>
public sealed class SelfEmployeeResolver(ICurrentUser currentUser, IEmployeeReadStore store)
{
    public async Task<Guid?> ResolveAsync(CancellationToken ct)
    {
        var row = currentUser.EmployeeId is { } employeeId ? await store.GetAsync(employeeId, ct).ConfigureAwait(false) : null;
        if (row is null && currentUser.UserId is { } userId)
        {
            row = await store.GetByUserIdAsync(userId, ct).ConfigureAwait(false);
        }

        return row is null || (row.UserId is { } linkedUserId && linkedUserId != currentUser.UserId) ? null : row.Id;
    }
}

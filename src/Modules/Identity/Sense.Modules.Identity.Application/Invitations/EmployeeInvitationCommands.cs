using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Identity.Application.Invitations;

/// <summary>
/// Çalışan kartı "Davetiye gönder" (11.3.01 madde 10): hedef iş e-postası, yoksa kişisel e-posta. Hesabı olmayan çalışana
/// Employee rolüyle kullanıcı açılır ve iki taraf bağlanır; davetli kullanıcıya davet yenilenip tekrar gönderilir; aktif hesap atlanır.
/// <c>SendCredentials=false</c> (çalışan oluşturma formundaki "giriş bilgisi gönder" anahtarı kapalı): hiçbir şey yapılmaz, e-posta gitmez.
/// </summary>
[RequiresPermission(IdentityPermissions.UsersInvite)]
public sealed record InviteEmployeeCommand(Guid EmployeeId, bool SendCredentials = true) : ICommand<EmployeeInvitationResultDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class InviteEmployeeValidator : AbstractValidator<InviteEmployeeCommand>
{
    public InviteEmployeeValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class InviteEmployeeHandler(ITenantContext tenant, IEmployeeDirectory employees, EmployeeInvitationService invitations)
    : ICommandHandler<InviteEmployeeCommand, EmployeeInvitationResultDto>
{
    public async Task<Result<EmployeeInvitationResultDto>> Handle(InviteEmployeeCommand command, CancellationToken cancellationToken)
    {
        var contacts = await employees.GetContactsAsync(tenant.TenantId, [command.EmployeeId], cancellationToken).ConfigureAwait(false);
        if (contacts.Count == 0)
        {
            return Error.NotFound(IdentityErrors.InvitationEmployeeNotFound);
        }

        if (!command.SendCredentials)
        {
            return new EmployeeInvitationResultDto(command.EmployeeId, EmployeeInvitationOutcomes.NotRequested, null, null);
        }

        return await invitations.InviteAsync(contacts[0], batch: null, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Çalışanlar grid'i "Toplu işlemler → Giriş bilgilerini gönder": seçimdeki her çalışana davet (senkron, sonuç özeti).</summary>
[RequiresPermission(IdentityPermissions.UsersInvite)]
public sealed record BulkInviteEmployeesCommand(EmployeeSelection Selection) : ICommand<BulkOperationResult>;

public sealed class BulkInviteEmployeesValidator : AbstractValidator<BulkInviteEmployeesCommand>
{
    public BulkInviteEmployeesValidator() => RuleFor(x => x.Selection).NotNull();
}

public sealed class BulkInviteEmployeesHandler(ITenantContext tenant, IEmployeeDirectory employees, EmployeeInvitationService invitations)
    : ICommandHandler<BulkInviteEmployeesCommand, BulkOperationResult>
{
    public async Task<Result<BulkOperationResult>> Handle(BulkInviteEmployeesCommand command, CancellationToken cancellationToken)
    {
        // Seçim, çağıranın employee.employees.read kapsamıyla çözülür (kapsam dışı kimlikler düşer ve başarısız raporlanır).
        var resolved = await employees.ResolveSelectionAsync(tenant.TenantId, command.Selection, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<Guid> requested = resolved;
        if (command.Selection.EmployeeIds is { } explicitIds)
        {
            var excluded = (command.Selection.ExcludedEmployeeIds ?? []).ToHashSet();
            requested = explicitIds.Distinct().Where(id => !excluded.Contains(id)).ToList();
        }

        var contacts = (await employees.GetContactsAsync(tenant.TenantId, resolved, cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.EmployeeId);
        var batch = new InvitationBatch(Guid.CreateVersion7());

        var items = new List<BulkItemResult>(requested.Count);
        foreach (var id in requested)
        {
            if (!contacts.TryGetValue(id, out var contact))
            {
                items.Add(new BulkItemResult(id, BulkItemOutcome.Failed, IdentityErrors.InvitationEmployeeNotFound));
                continue;
            }

            var result = await invitations.InviteAsync(contact, batch, cancellationToken).ConfigureAwait(false);
            items.Add(ToItem(id, result));
        }

        return BulkOperationResult.From(requested.Count, items);
    }

    private static BulkItemResult ToItem(Guid employeeId, Result<EmployeeInvitationResultDto> result)
    {
        if (result.IsFailure)
        {
            var skipped = result.Error.Code == IdentityErrors.InvitationEmailRequired;
            return new BulkItemResult(employeeId, skipped ? BulkItemOutcome.Skipped : BulkItemOutcome.Failed, result.Error.Code);
        }

        return result.Value.Outcome == EmployeeInvitationOutcomes.AlreadyActive
            ? new BulkItemResult(employeeId, BulkItemOutcome.Skipped, IdentityErrors.InvitationAlreadyActive)
            : new BulkItemResult(employeeId, BulkItemOutcome.Succeeded, null);
    }
}

/// <summary>Bir toplu gönderim partisi: kimliği ve bu partide (henüz kaydedilmemiş) yeni kullanıcılara ayrılan e-postalar.</summary>
public sealed class InvitationBatch(Guid id)
{
    public Guid Id { get; } = id;

    internal HashSet<string> ClaimedEmails { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Tekil ve toplu davetin ortak akışı (aynı Identity unit of work içinde çalışır).</summary>
public sealed class EmployeeInvitationService(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IUserRepository users,
    IRoleRepository roles,
    ITenantRepository tenants,
    IInvitationRepository invitations,
    IEmployeeUserLinker employeeLinker,
    ISecretGenerator secrets,
    IIdentityMailer mailer,
    IFrontendLinks links,
    IOptions<IdentityOptions> options,
    TimeProvider clock)
{
    private Tenant? _tenant;

    public async Task<Result<EmployeeInvitationResultDto>> InviteAsync(EmployeeContact contact, InvitationBatch? batch, CancellationToken ct)
    {
        // Çalışandaki UserId kaydı olmayan bir kullanıcıyı gösteriyorsa (yarım kalmış bağlantı) hesap yokmuş gibi davranılır.
        var user = contact.UserId is { } linkedUserId ? await users.GetByIdAsync(linkedUserId, ct).ConfigureAwait(false) : null;
        if (user is not null && IsActivated(user))
        {
            return AlreadyActive(contact);
        }

        if (user is { Status: UserStatus.Disabled })
        {
            return Error.Rule(IdentityErrors.InvitationUserDisabled);
        }

        var (email, source) = PickEmail(contact);
        if (email is null)
        {
            return Error.Rule(IdentityErrors.InvitationEmailRequired);
        }

        var isNewUser = false;
        if (user is null)
        {
            var existing = await users.GetByEmailAsync(email, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.EmployeeId is { } otherEmployeeId && otherEmployeeId != contact.EmployeeId)
                {
                    return Error.Conflict(IdentityErrors.InvitationEmailInUse);
                }

                if (existing.Status == UserStatus.Disabled)
                {
                    return Error.Rule(IdentityErrors.InvitationUserDisabled);
                }

                user = existing;
            }
            else
            {
                if (batch is not null && !batch.ClaimedEmails.Add(email))
                {
                    return Error.Conflict(IdentityErrors.InvitationEmailInUse);
                }

                var created = await CreateEmployeeUserAsync(contact, email, ct).ConfigureAwait(false);
                if (created.IsFailure)
                {
                    return created.Error;
                }

                user = created.Value;
                isNewUser = true;
            }
        }
        else if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            // Hiç giriş yapmamış davetli hesabın adresi, çalışanın güncel e-postasına taşınır (giriş adresi = davet adresi).
            if (await users.EmailExistsAsync(email, user.Id, ct).ConfigureAwait(false) || (batch is not null && !batch.ClaimedEmails.Add(email)))
            {
                return Error.Conflict(IdentityErrors.InvitationEmailInUse);
            }

            var changed = user.ChangeEmail(email);
            if (changed.IsFailure)
            {
                return changed.Error;
            }
        }

        if (user.EmployeeId != contact.EmployeeId)
        {
            user.LinkEmployee(contact.EmployeeId);
        }

        if (IsActivated(user))
        {
            await LinkEmployeeAsync(contact, user, ct).ConfigureAwait(false);
            return AlreadyActive(contact);
        }

        var outcome = await SendAsync(user, email, source, batch?.Id, ct).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        await LinkEmployeeAsync(contact, user, ct).ConfigureAwait(false);
        return new EmployeeInvitationResultDto(
            contact.EmployeeId,
            isNewUser ? EmployeeInvitationOutcomes.Sent : outcome.Value,
            email,
            source.ToString());
    }

    private static bool IsActivated(User user) => user.Status is UserStatus.Active or UserStatus.Locked;

    private static EmployeeInvitationResultDto AlreadyActive(EmployeeContact contact) =>
        new(contact.EmployeeId, EmployeeInvitationOutcomes.AlreadyActive, null, null);

    private static (string? Email, InvitationEmailSource Source) PickEmail(EmployeeContact contact)
    {
        if (!string.IsNullOrWhiteSpace(contact.WorkEmail) && EmailAddress.IsValid(contact.WorkEmail.Trim()))
        {
            return (EmailAddress.Of(contact.WorkEmail).Value, InvitationEmailSource.Work);
        }

        if (!string.IsNullOrWhiteSpace(contact.PersonalEmail) && EmailAddress.IsValid(contact.PersonalEmail.Trim()))
        {
            return (EmailAddress.Of(contact.PersonalEmail).Value, InvitationEmailSource.Personal);
        }

        return (null, InvitationEmailSource.Account);
    }

    private async Task<Result<User>> CreateEmployeeUserAsync(EmployeeContact contact, string email, CancellationToken ct)
    {
        var employeeRole = await roles.GetByCodeAsync(SystemRoleCodes.Employee, ct).ConfigureAwait(false);
        if (employeeRole is null)
        {
            return Error.NotFound(IdentityErrors.RoleNotFound);
        }

        var tenantEntity = await GetTenantAsync(ct).ConfigureAwait(false);
        var created = User.Create(tenant.TenantId, email, contact.FirstName, contact.LastName, DefaultLanguage(tenantEntity));
        if (created.IsFailure)
        {
            return created.Error;
        }

        var user = created.Value;
        user.SetRoles([employeeRole.Id]);
        users.Add(user);
        return user;
    }

    /// <returns>Sent (ilk davet) veya Resent (bekleyen davet yenilendi).</returns>
    private async Task<Result<string>> SendAsync(User user, string email, InvitationEmailSource source, Guid? batchId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var validity = TimeSpan.FromHours(options.Value.InvitationValidityHours);
        var rawToken = secrets.NewToken();
        var hash = secrets.Hash(rawToken);

        var outcome = EmployeeInvitationOutcomes.Sent;
        var invitation = await invitations.GetPendingByUserAsync(user.Id, ct).ConfigureAwait(false);
        if (invitation is null)
        {
            invitation = Invitation.Create(tenant.TenantId, user.Id, email, hash, now, validity, currentUser.UserId, source, batchId);
            invitations.Add(invitation);
        }
        else
        {
            var renewed = invitation.Renew(hash, now, validity, email, source, batchId);
            if (renewed.IsFailure)
            {
                return renewed.Error;
            }

            outcome = EmployeeInvitationOutcomes.Resent;
        }

        var tenantEntity = await GetTenantAsync(ct).ConfigureAwait(false);
        await mailer.SendInvitationAsync(email, user.FullName, tenantEntity?.Name ?? tenant.TenantSlug ?? string.Empty,
            links.InviteAccept(rawToken), invitation.ExpiresAt, ct).ConfigureAwait(false);

        return outcome;
    }

    private async Task LinkEmployeeAsync(EmployeeContact contact, User user, CancellationToken ct)
    {
        if (contact.UserId != user.Id)
        {
            await employeeLinker.LinkUserAsync(tenant.TenantId, contact.EmployeeId, user.Id, ct).ConfigureAwait(false);
        }
    }

    private async Task<Tenant?> GetTenantAsync(CancellationToken ct) =>
        _tenant ??= await tenants.GetByIdAsync(tenant.TenantId, ct).ConfigureAwait(false);

    private string DefaultLanguage(Tenant? tenantEntity)
    {
        var locale = tenantEntity?.Locale;
        if (string.IsNullOrWhiteSpace(locale))
        {
            return options.Value.DefaultLanguage;
        }

        var dash = locale.IndexOf('-', StringComparison.Ordinal);
        return (dash > 0 ? locale[..dash] : locale).ToLowerInvariant();
    }
}

using Microsoft.Extensions.Options;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>
/// An employee resolved for notification delivery. <see cref="UserId"/> is set only for an active user account (in-app
/// needs one); <see cref="Email"/> follows 11.3.13: work e-mail, then personal e-mail, then the login e-mail.
/// </summary>
public sealed record NotificationRecipient(
    Guid EmployeeId, Guid? UserId, string DisplayName, string? Email, string? AddressSource, string Language);

public static class AddressSources
{
    public const string WorkEmail = "work_email";
    public const string PersonalEmail = "personal_email";
    public const string UserEmail = "user_email";
}

/// <summary>Employee ids → recipients (contact data from Employee, account/language from Identity). Terminated employees are dropped.</summary>
public sealed class NotificationRecipientResolver(
    IEmployeeContactLookup contacts,
    IUserProfileLookup userProfiles,
    IRoleAssignmentLookup roleAssignments,
    IOptions<NotificationsOptions> options)
{
    private const string TerminatedStatus = "Terminated";

    /// <summary>
    /// Permission key → system role codes that hold it, as resolved from
    /// Sense.Modules.Identity.Application.Roles.SystemRoleDefinitions at the time each entry was added.
    /// Notifications.Application cannot reference Identity.Application/Domain (modules talk only via *.Contracts,
    /// same constraint as Workflow's DefaultApproverResolver.FallbackApproverRoleCode), and IRoleAssignmentLookup
    /// (Identity.Contracts) only supports "employee ids in role X", not "employee ids with permission Y" - so this
    /// map is a hand-kept duplicate of SystemRoleDefinitions' role→permission wildcards for the specific keys
    /// Notifications needs to fan out to. Keep in sync by hand if SystemRoleDefinitions changes which roles include
    /// a mapped key. Role code literals mirror Sense.Modules.Identity.Domain.Roles.SystemRoleCodes.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> PermissionRoleCodes = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // documents.requirements.view: Owner/Admin ("*"), HR/HRSpecialist ("documents.*"), PayrollSpecialist/Manager/
        // Recruiter ("documents.*.view").
        ["documents.requirements.view"] = ["Owner", "Admin", "HR", "HRSpecialist", "PayrollSpecialist", "Manager", "Recruiter"],
    };

    /// <summary>Owner/Admin always hold every permission (SystemRoleDefinitions' "*" wildcard) - the safe fallback for an unmapped key: no false positives, only possibly incomplete.</summary>
    private static readonly string[] OwnerAdminOnly = ["Owner", "Admin"];

    /// <summary>Employee ids of every user whose role(s) include <paramref name="permissionKey"/>, tenant-scoped, deduplicated. See <see cref="PermissionRoleCodes"/>'s remarks for the resolution strategy and its limits.</summary>
    public async Task<IReadOnlyList<Guid>> ResolveByPermissionAsync(Guid tenantId, string permissionKey, CancellationToken ct)
    {
        var roleCodes = PermissionRoleCodes.GetValueOrDefault(permissionKey, OwnerAdminOnly);

        var employeeIds = new HashSet<Guid>();
        foreach (var roleCode in roleCodes)
        {
            foreach (var employeeId in await roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, roleCode, ct))
            {
                employeeIds.Add(employeeId);
            }
        }

        return employeeIds.Count == 0 ? [] : employeeIds.ToArray();
    }

    public async Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var contactList = await contacts.GetContactsAsync(tenantId, employeeIds.Distinct().ToList(), ct);
        var userIds = contactList.Where(c => c.UserId is not null).Select(c => c.UserId!.Value).ToList();
        var profiles = await userProfiles.GetAsync(tenantId, userIds, ct);

        var recipients = new List<NotificationRecipient>(contactList.Count);
        foreach (var contact in contactList.Where(c => !string.Equals(c.Status, TerminatedStatus, StringComparison.Ordinal)))
        {
            var profile = contact.UserId is { } uid ? profiles.GetValueOrDefault(uid) : null;
            var (email, source) = PickAddress(contact, profile);
            var name = $"{contact.FirstName} {contact.LastName}".Trim();

            recipients.Add(new NotificationRecipient(
                contact.EmployeeId,
                profile is { IsActive: true } ? profile.UserId : null,
                name.Length > 0 ? name : profile?.DisplayName ?? string.Empty,
                email,
                source,
                NormalizeLanguage(profile?.PreferredLanguage) ?? options.Value.DefaultLanguage));
        }

        return recipients;
    }

    /// <summary>"en-US" → "en"; null/blank → null.</summary>
    public static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var dash = language.IndexOf('-', StringComparison.Ordinal);
        return (dash > 0 ? language[..dash] : language).Trim().ToLowerInvariant();
    }

    private static (string? Email, string? Source) PickAddress(EmployeeContact contact, UserProfileInfo? profile)
    {
        if (!string.IsNullOrWhiteSpace(contact.WorkEmail))
        {
            return (contact.WorkEmail, AddressSources.WorkEmail);
        }

        if (!string.IsNullOrWhiteSpace(contact.PersonalEmail))
        {
            return (contact.PersonalEmail, AddressSources.PersonalEmail);
        }

        return string.IsNullOrWhiteSpace(profile?.Email) ? (null, null) : (profile.Email, AddressSources.UserEmail);
    }
}

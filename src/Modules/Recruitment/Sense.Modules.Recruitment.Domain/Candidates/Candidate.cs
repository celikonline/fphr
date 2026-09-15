using System.Text.RegularExpressions;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.Candidates;

/// <summary>Where a candidate record came from. Only <see cref="Manual"/> is reachable in R0 - the career-site apply flow (R3) adds CareerSite.</summary>
public enum CandidateSource
{
    Manual,
    CareerSite,
    Referral,
    Other,
}

/// <summary>
/// A candidate (Kolay İK "Aday"). R0 scope: contact info only, added manually by a recruiter; the career-site public
/// apply form, CV parsing and tagging (roadmap #89) are deferred (see faz6-durum-takibi.md).
/// </summary>
public sealed class Candidate : TenantAggregateRoot<Guid>, IAuditLogged
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private Candidate()
    {
    }

    private Candidate(Guid id, Guid tenantId, string firstName, string lastName, string email, string? phone, CandidateSource source)
        : base(id, tenantId)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        Source = source;
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public CandidateSource Source { get; private set; }

    public static Result<Candidate> Create(Guid tenantId, string firstName, string lastName, string email, string? phone, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            return Error.Validation(RecruitmentDomainErrors.CandidateNameRequired);
        }

        if (string.IsNullOrWhiteSpace(email) || !EmailPattern.IsMatch(email))
        {
            return Error.Validation(RecruitmentDomainErrors.CandidateEmailRequired);
        }

        return new Candidate(
            id ?? Guid.CreateVersion7(), tenantId,
            Guard.MaxLength(firstName.Trim(), RecruitmentLimits.NameMaxLength),
            Guard.MaxLength(lastName.Trim(), RecruitmentLimits.NameMaxLength),
            Guard.MaxLength(email.Trim(), RecruitmentLimits.EmailMaxLength),
            string.IsNullOrWhiteSpace(phone) ? null : Guard.MaxLength(phone.Trim(), RecruitmentLimits.PhoneMaxLength),
            CandidateSource.Manual);
    }

    public Result UpdateContactInfo(string firstName, string lastName, string email, string? phone)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            return Error.Validation(RecruitmentDomainErrors.CandidateNameRequired);
        }

        if (string.IsNullOrWhiteSpace(email) || !EmailPattern.IsMatch(email))
        {
            return Error.Validation(RecruitmentDomainErrors.CandidateEmailRequired);
        }

        FirstName = Guard.MaxLength(firstName.Trim(), RecruitmentLimits.NameMaxLength);
        LastName = Guard.MaxLength(lastName.Trim(), RecruitmentLimits.NameMaxLength);
        Email = Guard.MaxLength(email.Trim(), RecruitmentLimits.EmailMaxLength);
        Phone = string.IsNullOrWhiteSpace(phone) ? null : Guard.MaxLength(phone.Trim(), RecruitmentLimits.PhoneMaxLength);
        return Result.Success();
    }
}

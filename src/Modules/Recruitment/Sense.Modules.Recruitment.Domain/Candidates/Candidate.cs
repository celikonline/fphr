using System.Text.RegularExpressions;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.Candidates;

/// <summary>Where a candidate record came from. <see cref="CareerSite"/> is set by the public career-site apply flow (R3, see Application/Public).</summary>
public enum CandidateSource
{
    Manual,
    CareerSite,
    Referral,
    Other,
}

/// <summary>
/// A candidate (Kolay İK "Aday"): contact info, added either manually by a recruiter or by the candidate themselves
/// via the public career-site apply form (R3, see Application/Public). CV parsing and auto-tagging (roadmap #89)
/// are still deferred (see faz6-durum-takibi.md).
/// </summary>
public sealed class Candidate : TenantAggregateRoot<Guid>, IAuditLogged
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<CandidateTagAssignment> _tags = [];

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

    /// <summary>
    /// Fixed domain part an anonymized candidate's e-mail always carries (see <see cref="Anonymize"/>). Public so
    /// the infrastructure repository's retention sweep query can filter already-anonymized rows server-side,
    /// without loading every candidate into memory.
    /// </summary>
    public const string AnonymizedEmailDomain = "@kvkk.anonim.local";

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public CandidateSource Source { get; private set; }

    public IReadOnlyList<CandidateTagAssignment> Tags => _tags.AsReadOnly();

    /// <summary>
    /// True once <see cref="Anonymize"/> has run (KVKK imha). No dedicated boolean column exists yet (see
    /// <see cref="RecruitmentLimits.CandidateDataRetentionMonths"/> doc comment for why) - detected from the
    /// deterministic anonymized e-mail shape instead, so no migration is needed to add this capability.
    /// </summary>
    public bool IsAnonymized => Email.EndsWith(AnonymizedEmailDomain, StringComparison.Ordinal);

    /// <summary>
    /// KVKK imha: irreversibly scrubs this candidate's personal data (name, e-mail, phone, tags) in place. The row
    /// itself is kept (job application history, audit trail) but no longer identifies a real person. Idempotency
    /// guard: calling this twice fails with <see cref="RecruitmentDomainErrors.CandidateAlreadyAnonymized"/> rather
    /// than silently re-scrubbing (an already-anonymized row has nothing left to remove).
    /// </summary>
    public Result Anonymize()
    {
        if (IsAnonymized)
        {
            return Error.Conflict(RecruitmentDomainErrors.CandidateAlreadyAnonymized);
        }

        FirstName = "Silinmiş";
        LastName = "Aday";
        Email = $"silinmis-{Id:N}{AnonymizedEmailDomain}".ToLowerInvariant();
        Phone = null;
        _tags.Clear();
        return Result.Success();
    }

    public static Result<Candidate> Create(
        Guid tenantId, string firstName, string lastName, string email, string? phone, Guid? id = null,
        CandidateSource source = CandidateSource.Manual)
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
            source);
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

    /// <summary>Replaces the candidate's tag set (R2, roadmap #89 - manual catalog/assignment only; auto-tagging from application answers is R3).</summary>
    public void SetTags(IReadOnlyCollection<Guid> tagIds)
    {
        _tags.Clear();
        foreach (var tagId in tagIds.Distinct())
        {
            _tags.Add(new CandidateTagAssignment(Id, tagId));
        }
    }
}

/// <summary>One tag assigned to a candidate. Owned collection (see JobPostingAccess precedent) - tenant isolation comes from the owning Candidate.</summary>
public sealed class CandidateTagAssignment : AuditableRecord
{
    private CandidateTagAssignment()
    {
    }

    public CandidateTagAssignment(Guid candidateId, Guid tagId)
    {
        CandidateId = candidateId;
        TagId = tagId;
    }

    public Guid CandidateId { get; private set; }

    public Guid TagId { get; private set; }
}

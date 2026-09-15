using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.Overtime;

public enum OvertimeConsentSource
{
    EmploymentContract = 0,
    StandaloneForm = 1,
}

/// <summary>
/// Employee's written overtime consent (Fazla Çalışma Yönetmeliği, RG 25.08.2017/30165): given once, no expiry (there is
/// deliberately no ValidUntil); revocable with written notice that takes effect <c>Overtime.ConsentRevocationNoticeDays</c>
/// (30) days later. Night overtime needs a consent with <see cref="CoversNightWork"/>.
/// </summary>
public sealed class OvertimeConsent : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OvertimeConsent()
    {
    }

    private OvertimeConsent(Guid tenantId, Guid employeeId, DateOnly givenAt, OvertimeConsentSource source, bool coversNightWork, Guid? documentId, Guid? recordedByUserId)
        : base(Guid.CreateVersion7(), tenantId)
    {
        EmployeeId = employeeId;
        GivenAt = givenAt;
        Source = source;
        CoversNightWork = coversNightWork;
        DocumentId = documentId;
        RecordedByUserId = recordedByUserId;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly GivenAt { get; private set; }

    public OvertimeConsentSource Source { get; private set; }

    public bool CoversNightWork { get; private set; }

    public DateOnly? RevocationNoticeAt { get; private set; }

    /// <summary>RevocationNoticeAt + notice days; the consent no longer covers overtime on or after this date.</summary>
    public DateOnly? RevocationEffectiveAt { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid? RecordedByUserId { get; private set; }

    public static OvertimeConsent Record(
        Guid tenantId, Guid employeeId, DateOnly givenAt, OvertimeConsentSource source, bool coversNightWork, Guid? documentId, Guid? recordedByUserId) =>
        new(tenantId, employeeId, givenAt, source, coversNightWork, documentId, recordedByUserId);

    public Result Revoke(DateOnly noticeAt, int noticeDays)
    {
        if (RevocationNoticeAt is not null)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeConsentAlreadyRevoked);
        }

        if (noticeAt < GivenAt || noticeDays < 0)
        {
            return Error.Validation(LeaveDomainErrors.OvertimeConsentInvalidRevocation);
        }

        RevocationNoticeAt = noticeAt;
        RevocationEffectiveAt = noticeAt.AddDays(noticeDays);
        return Result.Success();
    }

    /// <summary>Given on/before <paramref name="date"/> and not yet revoked for that date (the notice period still counts as consent).</summary>
    public bool IsInEffectOn(DateOnly date) => GivenAt <= date && (RevocationEffectiveAt is null || date < RevocationEffectiveAt);

    public bool IsRevokedOn(DateOnly date) => RevocationEffectiveAt is { } effective && effective <= date;
}

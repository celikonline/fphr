using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;

namespace Sense.Modules.Workflow.Domain.Delegations;

/// <summary>
/// Onay vekaleti (Z2.2): <see cref="DelegatorEmployeeId"/>, [<see cref="StartDate"/>, <see cref="EndDate"/>] aralığında (uçlar dahil)
/// onay yetkisini <see cref="DelegateEmployeeId"/>'ye devreder. <see cref="RequestTypes"/> boşsa (null) tüm talep türleri kapsanır.
/// Vekalet zincirlenmez (A→B, B→C ise C, A'nın işlerini görmez). İptal (<see cref="Revoke"/>) yalnız ileriye etkilidir;
/// geçmiş kararlar ApprovalTask.DecidedOnBehalfOfEmployeeId ile kalıcıdır.
/// </summary>
public sealed class ApprovalDelegation : TenantEntity<Guid>, IAuditLogged
{
    public const char RequestTypeSeparator = ',';

    private ApprovalDelegation()
    {
    }

    private ApprovalDelegation(
        Guid tenantId, Guid delegatorEmployeeId, Guid delegateEmployeeId, DateOnly startDate, DateOnly endDate, string? requestTypes, Guid? createdByEmployeeId)
        : base(Guid.CreateVersion7(), tenantId)
    {
        DelegatorEmployeeId = delegatorEmployeeId;
        DelegateEmployeeId = delegateEmployeeId;
        StartDate = startDate;
        EndDate = endDate;
        RequestTypes = requestTypes;
        CreatedByEmployeeId = createdByEmployeeId;
    }

    public Guid DelegatorEmployeeId { get; private set; }

    public Guid DelegateEmployeeId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Son gün dahil: EndDate günü boyunca vekalet geçerlidir, ertesi gün biter.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>Virgülle ayrılmış talep türü kodları; null = tüm türler.</summary>
    public string? RequestTypes { get; private set; }

    public Guid? CreatedByEmployeeId { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public IReadOnlyList<string> RequestTypeList =>
        RequestTypes is null ? [] : RequestTypes.Split(RequestTypeSeparator, StringSplitOptions.RemoveEmptyEntries);

    public static Result<ApprovalDelegation> Create(
        Guid tenantId, Guid delegatorEmployeeId, Guid delegateEmployeeId, DateOnly startDate, DateOnly endDate,
        IReadOnlyCollection<string>? requestTypes, Guid? createdByEmployeeId = null)
    {
        if (delegatorEmployeeId == Guid.Empty || delegateEmployeeId == Guid.Empty)
        {
            return Error.Validation(WorkflowDomainErrors.DelegationEmployeeRequired);
        }

        if (delegatorEmployeeId == delegateEmployeeId)
        {
            return Error.Rule(WorkflowDomainErrors.DelegationToSelfNotAllowed);
        }

        if (endDate < startDate)
        {
            return Error.Validation(WorkflowDomainErrors.DelegationRangeInvalid);
        }

        string? joined = null;
        if (requestTypes is { Count: > 0 })
        {
            var distinct = requestTypes.Select(t => t?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Any(t => !ApprovalPolicy.IsValidRequestType(t)))
            {
                return Error.Validation(WorkflowDomainErrors.PolicyRequestTypeInvalid);
            }

            joined = string.Join(RequestTypeSeparator, distinct);
        }

        return new ApprovalDelegation(tenantId, delegatorEmployeeId, delegateEmployeeId, startDate, endDate, joined, createdByEmployeeId);
    }

    public bool IsActiveOn(DateOnly today, string requestType) =>
        !IsRevoked
        && today >= StartDate
        && today <= EndDate
        && (RequestTypes is null || RequestTypeList.Contains(requestType, StringComparer.Ordinal));

    public Result Revoke(DateTime now)
    {
        if (IsRevoked)
        {
            return Error.Rule(WorkflowDomainErrors.DelegationAlreadyRevoked);
        }

        RevokedAt = now;
        return Result.Success();
    }
}

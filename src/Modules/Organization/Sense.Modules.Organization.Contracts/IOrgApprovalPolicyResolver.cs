namespace Sense.Modules.Organization.Contracts;

/// <summary>
/// Request types whose approval policy can be assigned to an org unit (Kolay İK "Opsiyonel alanlar": İzin / Avans /
/// Harcama / Fazla mesai onay süreci). A request type is a plain string so the Workflow module can introduce new
/// ones without an Organization schema change. Rule: ASCII PascalCase identifier - an uppercase letter followed by
/// letters/digits, at most <see cref="MaxLength"/> characters, compared case-sensitively (e.g. "Leave",
/// "VisaDocument", "ProfileChange"). Storage and <see cref="IOrgApprovalPolicyResolver"/> accept any such
/// identifier; the org unit assignment API (GET/PUT /organization/units/{id}/approval-policies) only offers
/// <see cref="All"/>, so a new unit-assignable type is added here first (contract-first PR).
/// </summary>
public static class ApprovalRequestTypes
{
    public const string Leave = "Leave";
    public const string Advance = "Advance";
    public const string Expense = "Expense";
    public const string Overtime = "Overtime";

    /// <summary>Recruitment's job requisition approval (Faz 6 R1, roadmap #87 "Requisition submit→approval→posting unlock").</summary>
    public const string Requisition = "Requisition";

    /// <summary>Assets' zimmet assignment approval (TK-018, roadmap faz4 "As0").</summary>
    public const string AssetAssignment = "AssetAssignment";

    public const int MaxLength = 50;

    /// <summary>Unit-assignable request types, in display order.</summary>
    public static readonly IReadOnlyList<string> All = [Leave, Advance, Expense, Overtime, Requisition, AssetAssignment];

    /// <summary>True if <paramref name="requestType"/> follows the identifier rule above.</summary>
    public static bool IsWellFormed(string? requestType) =>
        !string.IsNullOrEmpty(requestType)
        && requestType.Length <= MaxLength
        && char.IsAsciiLetterUpper(requestType[0])
        && requestType.All(char.IsAsciiLetterOrDigit);
}

/// <summary>Effective approval policy for an org unit: the policy id and the unit it was assigned on.</summary>
/// <param name="ApprovalPolicyId">Workflow-owned policy id (Organization stores it without a foreign key).</param>
/// <param name="SourceOrgUnitId">The unit carrying the assignment (the requested unit itself or an ancestor).</param>
/// <param name="Inherited">True when <paramref name="SourceOrgUnitId"/> is an ancestor of the requested unit.</param>
public sealed record ApprovalPolicyResolution(Guid ApprovalPolicyId, Guid SourceOrgUnitId, bool Inherited);

/// <summary>
/// Cross-module read port for unit-based approval policy selection (11.3.02 "Birime onay süreci ataması",
/// 11.3.04 "Birim bazlı süreç ataması + devralma"). Implemented by Organization.Infrastructure.
/// </summary>
public interface IOrgApprovalPolicyResolver
{
    /// <summary>
    /// Walks <paramref name="orgUnitId"/> and then its parents up to the root; inactive units are passed through but
    /// their assignments are ignored. The first assignment for <paramref name="requestType"/> wins. Null when no unit on
    /// the path has one (or the unit is unknown) - the caller then applies its own default (Workflow: first manager).
    /// </summary>
    Task<ApprovalPolicyResolution?> ResolveAsync(Guid tenantId, Guid orgUnitId, string requestType, CancellationToken ct = default);
}

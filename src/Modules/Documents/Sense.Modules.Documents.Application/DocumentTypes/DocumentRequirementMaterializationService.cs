using Sense.Modules.Assets.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>
/// Shared "resolve applicable Onboarding/Conditional document types for an employee and materialize the missing
/// EmployeeDocumentRequirement rows" logic, used by both <see cref="EmployeeHiredDocumentRequirementHandler"/> (hire
/// date from the integration event) and <see cref="ReevaluateEmployeeDocumentRequirementsCommand"/> (manual HR
/// action on an existing employee - see the DueDateBasis remark on <see cref="MaterializeAsync"/>). Does not save;
/// callers own their own IDocumentsUnitOfWork.SaveChangesAsync.
/// </summary>
public sealed class DocumentRequirementMaterializationService(
    IDocumentTypeDefinitionRepository types,
    IEmployeeDocumentRequirementRepository requirements,
    IEmployeeDocumentEligibilityLookup eligibility,
    IAssetLookup assetLookup,
    DocumentTypeProvisioningService provisioning,
    ILegislationParameterReader legislation)
{
    /// <summary>Grace period (days) added to <c>dueDateBasis</c> for a materialized requirement's DueDate. A tenant-configurable TenantSettings value is a future enhancement, not built in this pass.</summary>
    public const int DefaultGraceDays = 30;

    /// <summary>4857 md.8: two months to hand over the written contract or a working-conditions document. Used when no Contract.WorkingConditionsDocDeadlineDays row is seeded.</summary>
    public const int DefaultContractDocumentDeadlineDays = 60;

    /// <summary>
    /// WaivedReason used when re-materialization auto-clears a still-<see cref="DocumentRequirementStatus.Missing"/>
    /// Conditional requirement because its condition no longer matches (faz4 TB-9: "append-only materialization
    /// sorunu"). Never touches Uploaded/already-Waived rows or non-Conditional stages.
    /// </summary>
    public const string AutoWaivedReason = "Koşul artık geçerli değil (otomatik yeniden değerlendirme)";

    /// <summary>Actor recorded on an auto-waive - no human triggered it, same "no user" convention as
    /// <c>WaiveDocumentRequirementHandler</c>'s <c>currentUser.UserId ?? Guid.Empty</c>.</summary>
    public static readonly Guid SystemActorId = Guid.Empty;

    /// <param name="dueDateBasis">
    /// For EmployeeHired, the employee's real HireDate. For the manual reevaluate command there is no cross-module
    /// HireDate lookup today (Employee.Contracts exposes only derived booleans for sensitive fields, and HireDate
    /// isn't exposed either) - callers without a real hire date pass "today" instead, which is a deliberate,
    /// documented simplification of the manual path only.
    /// </param>
    public async Task<IReadOnlyList<EmployeeDocumentRequirement>> MaterializeAsync(
        Guid tenantId, Guid employeeId, DateOnly dueDateBasis, CancellationToken cancellationToken)
    {
        await provisioning.EnsureSeededAsync(tenantId, cancellationToken);

        var signals = await eligibility.GetSignalsAsync(tenantId, employeeId, cancellationToken);
        var hasOpenAssets = await assetLookup.HasOpenAssetsAsync(tenantId, employeeId, cancellationToken);

        // Three signals are available cross-module today: BirthDate/DisabilityDegree live on Employee and are
        // sensitive (only derived booleans are exposed, per architecture rule); HasOpenAssets comes from the Assets
        // module's IAssetLookup (roadmap #65). The rest are hardcoded false until their owning data exists:
        //  - NonTurkishNational: no Nationality field on Employee yet
        //  - HasIncentive / UsesDisabilityTaxReduction: Payroll incentive tracking doesn't exist yet
        //  - ExConvictOrTerrorVictim: no such field anywhere yet (manual/HR-only data point)
        //  - RequiresPeriodicHealthCheck / VeryHazardousJob: no job-hazard classification yet
        //  - RequiresDriving: no job/position attribute for this yet
        //  - HasOvertimeConsent: Leave's OvertimeConsent isn't wired as a cross-module signal yet
        //  - IsShiftWorker: Attendance shift scheduling doesn't exist yet
        var context = new EmployeeDocumentContext(
            IsUnder18: signals?.IsUnder18 ?? false,
            HasDisability: signals?.HasDisability ?? false,
            NonTurkishNational: false,
            HasIncentive: false,
            UsesDisabilityTaxReduction: false,
            IsExConvictOrTerrorVictim: false,
            RequiresPeriodicHealthCheck: false,
            RequiresDriving: false,
            HasOpenAssets: hasOpenAssets,
            IsVeryHazardousJob: false,
            HasOvertimeConsent: false,
            IsShiftWorker: false,
            IsUndergroundWorker: signals?.IsUndergroundWorker ?? false);

        var activeTypes = await types.ListAsync(stage: null, isActive: true, cancellationToken);
        var resolved = DocumentRequirementEvaluator.ResolveAtHire(activeTypes, context);
        await AutoWaiveStaleConditionalRequirementsAsync(employeeId, activeTypes, resolved, cancellationToken);
        var dueDate = dueDateBasis.AddDays(DefaultGraceDays);

        // The written contract has a statutory deadline (4857 md.8) rather than the generic grace period.
        var contractDeadlineDays = (int)await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.ContractWorkingConditionsDocDeadlineDays,
            dueDateBasis,
            DefaultContractDocumentDeadlineDays,
            cancellationToken).ConfigureAwait(false);
        var contractDueDate = dueDateBasis.AddDays(contractDeadlineDays);

        var created = new List<EmployeeDocumentRequirement>();
        foreach (var type in resolved)
        {
            if (await requirements.FindAsync(employeeId, type.Id, cancellationToken) is not null)
            {
                continue;
            }

            var typeDueDate = type.Code == DocumentTypeSeed.EmploymentContractCode ? contractDueDate : dueDate;
            var materialized = EmployeeDocumentRequirement.Materialize(tenantId, employeeId, type.Id, typeDueDate);
            if (materialized.IsFailure)
            {
                continue;
            }

            requirements.Add(materialized.Value);
            created.Add(materialized.Value);
        }

        return created;
    }

    /// <summary>
    /// Append-only materialization never removed a requirement once its condition stopped matching (e.g. the
    /// employee turned 18, a disability record was corrected). This clears the gap for the one case that's safe to
    /// automate: a still-<see cref="DocumentRequirementStatus.Missing"/> Conditional requirement whose type is no
    /// longer in <paramref name="resolvedTypes"/> gets auto-waived. Uploaded requirements (real evidence already on
    /// file) and manually-Waived ones are left untouched; non-Conditional stages (Onboarding/DuringEmployment/
    /// Offboarding) are out of scope here - their disappearance means the type itself was deactivated, a distinct
    /// HR decision, not a condition re-evaluation.
    /// </summary>
    private async Task AutoWaiveStaleConditionalRequirementsAsync(
        Guid employeeId,
        IReadOnlyCollection<DocumentTypeDefinition> activeTypes,
        IReadOnlyCollection<DocumentTypeDefinition> resolvedTypes,
        CancellationToken cancellationToken)
    {
        var conditionalTypeIds = activeTypes
            .Where(t => t.Stage == DocumentTypeStage.Conditional)
            .Select(t => t.Id)
            .ToHashSet();
        if (conditionalTypeIds.Count == 0)
        {
            return;
        }

        var resolvedTypeIds = resolvedTypes.Select(t => t.Id).ToHashSet();
        // Defensive `?? []`: existing unit tests stub IEmployeeDocumentRequirementRepository with NSubstitute without
        // configuring ListByEmployeeAsync, and NSubstitute's default for an unconfigured Task<IReadOnlyList<T>>
        // member is a completed task wrapping null, not an empty list.
        var existingRequirements = await requirements.ListByEmployeeAsync(employeeId, cancellationToken) ?? [];
        foreach (var requirement in existingRequirements)
        {
            if (requirement.Status != DocumentRequirementStatus.Missing)
            {
                continue;
            }

            if (!conditionalTypeIds.Contains(requirement.DocumentTypeId) || resolvedTypeIds.Contains(requirement.DocumentTypeId))
            {
                continue;
            }

            // AutoWaivedReason is a non-empty constant, so this can never fail the domain's "reason required" check.
            _ = requirement.Waive(AutoWaivedReason, SystemActorId);
        }
    }
}

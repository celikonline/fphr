using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;

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
    DocumentTypeProvisioningService provisioning)
{
    /// <summary>Grace period (days) added to <c>dueDateBasis</c> for a materialized requirement's DueDate. A tenant-configurable TenantSettings value is a future enhancement, not built in this pass.</summary>
    public const int DefaultGraceDays = 30;

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

        // Only two signals are available cross-module today (BirthDate/DisabilityDegree live on Employee and are
        // sensitive - only derived booleans are exposed, per architecture rule). The rest are hardcoded false until
        // their owning data exists:
        //  - NonTurkishNational: no Nationality field on Employee yet
        //  - HasIncentive / UsesDisabilityTaxReduction: Payroll incentive tracking doesn't exist yet
        //  - ExConvictOrTerrorVictim: no such field anywhere yet (manual/HR-only data point)
        //  - RequiresPeriodicHealthCheck / VeryHazardousJob: no job-hazard classification yet
        //  - RequiresDriving: no job/position attribute for this yet
        //  - HasOpenAssets: Assets module doesn't exist yet
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
            HasOpenAssets: false,
            IsVeryHazardousJob: false,
            HasOvertimeConsent: false,
            IsShiftWorker: false);

        var activeTypes = await types.ListAsync(stage: null, isActive: true, cancellationToken);
        var resolved = DocumentRequirementEvaluator.ResolveAtHire(activeTypes, context);
        var dueDate = dueDateBasis.AddDays(DefaultGraceDays);

        var created = new List<EmployeeDocumentRequirement>();
        foreach (var type in resolved)
        {
            if (await requirements.FindAsync(employeeId, type.Id, cancellationToken) is not null)
            {
                continue;
            }

            var materialized = EmployeeDocumentRequirement.Materialize(tenantId, employeeId, type.Id, dueDate);
            if (materialized.IsFailure)
            {
                continue;
            }

            requirements.Add(materialized.Value);
            created.Add(materialized.Value);
        }

        return created;
    }
}

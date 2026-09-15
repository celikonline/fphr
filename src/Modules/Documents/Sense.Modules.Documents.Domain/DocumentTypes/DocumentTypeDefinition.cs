using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain.DocumentTypes;

/// <summary>When a personnel document type is expected within the employment lifecycle (roadmap: "zorunlu ve koşullu özlük belgesi kuralları").</summary>
public enum DocumentTypeStage
{
    Onboarding = 0,
    Conditional = 1,
    DuringEmployment = 2,
    Offboarding = 3,
    Recommended = 4,
}

/// <summary>Condition that makes a <see cref="DocumentTypeStage.Conditional"/> document type applicable to a given employee.</summary>
public enum DocumentRequirementCondition
{
    AgeUnder18 = 0,
    HasDisability = 1,
    NonTurkishNational = 2,
    HasIncentive = 3,
    UsesDisabilityTaxReduction = 4,
    ExConvictOrTerrorVictim = 5,
    RequiresPeriodicHealthCheck = 6,
    RequiresDriving = 7,
    HasOpenAssets = 8,
    VeryHazardousJob = 9,
    HasOvertimeConsent = 10,
    IsShiftWorker = 11,
}

/// <summary>
/// Tenant-scoped personnel document type catalog row. Every tenant gets its own editable copy of the system seed
/// (<see cref="DocumentTypeSeed"/>) the first time it's needed, mirroring how Leave's LeaveType/LeaveRuleSet are
/// seeded per tenant (<c>LeaveRuleSetSeed</c>).
/// </summary>
public sealed class DocumentTypeDefinition : TenantAggregateRoot<Guid>, IAuditLogged
{
    private DocumentTypeDefinition()
    {
    }

    private DocumentTypeDefinition(
        Guid id,
        Guid tenantId,
        string code,
        DocumentTypeStage stage,
        DocumentRequirementCondition? condition,
        bool isSystem) : base(id, tenantId)
    {
        Code = code;
        Stage = stage;
        Condition = condition;
        IsSystem = isSystem;
        IsActive = true;
        Quantity = 1;
    }

    /// <summary>Stable identifier (e.g. "NationalIdCopy"); immutable after creation.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Tenant-editable display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Immutable after creation.</summary>
    public DocumentTypeStage Stage { get; private set; }

    public bool IsRequired { get; private set; }

    /// <summary>Non-null iff <see cref="Stage"/> is <see cref="DocumentTypeStage.Conditional"/>; immutable after creation.</summary>
    public DocumentRequirementCondition? Condition { get; private set; }

    public int? RenewalPeriodDays { get; private set; }

    public int Quantity { get; private set; } = 1;

    public bool IsSensitive { get; private set; }

    public int? DefaultRetentionDays { get; private set; }

    /// <summary>True for seed rows, false for tenant-created custom types; immutable after creation.</summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int SortOrder { get; private set; }

    public static Result<DocumentTypeDefinition> Create(
        Guid tenantId,
        string code,
        string name,
        DocumentTypeStage stage,
        bool isRequired,
        DocumentRequirementCondition? condition,
        int? renewalPeriodDays,
        int quantity,
        bool isSensitive,
        int? defaultRetentionDays,
        bool isSystem,
        int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeCodeRequired);
        }

        if (code.Trim().Length > DocumentsLimits.DocumentTypeCodeMaxLength)
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeCodeTooLong);
        }

        if (stage == DocumentTypeStage.Conditional && condition is null)
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeConditionRequiredForConditional);
        }

        if (stage != DocumentTypeStage.Conditional && condition is not null)
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeConditionNotAllowed);
        }

        var definition = new DocumentTypeDefinition(Guid.CreateVersion7(), tenantId, code.Trim(), stage, condition, isSystem);
        var applied = definition.Apply(name, isRequired, renewalPeriodDays, quantity, isSensitive, defaultRetentionDays, sortOrder);
        return applied.IsFailure ? applied.Error : definition;
    }

    /// <summary>Editable fields only (not Code/Stage/Condition/IsSystem).</summary>
    public Result Update(string name, bool isRequired, int? renewalPeriodDays, int quantity, bool isSensitive, int? defaultRetentionDays, int sortOrder) =>
        Apply(name, isRequired, renewalPeriodDays, quantity, isSensitive, defaultRetentionDays, sortOrder);

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private Result Apply(string name, bool isRequired, int? renewalPeriodDays, int quantity, bool isSensitive, int? defaultRetentionDays, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeNameRequired);
        }

        if (quantity < 1)
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeInvalidQuantity);
        }

        if (renewalPeriodDays is < 1)
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeInvalidRenewalPeriod);
        }

        if (defaultRetentionDays is < 1)
        {
            return Error.Validation(DocumentsDomainErrors.DocumentTypeInvalidRetention);
        }

        Name = name.Trim();
        IsRequired = isRequired;
        RenewalPeriodDays = renewalPeriodDays;
        Quantity = quantity;
        IsSensitive = isSensitive;
        DefaultRetentionDays = defaultRetentionDays;
        SortOrder = sortOrder;
        return Result.Success();
    }
}

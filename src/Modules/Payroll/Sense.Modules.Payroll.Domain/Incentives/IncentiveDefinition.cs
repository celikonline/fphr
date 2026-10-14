using System.Text.RegularExpressions;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Incentives;

/// <summary>Editable content of an <see cref="IncentiveDefinition"/> (everything except the immutable <c>Code</c>).</summary>
public sealed record IncentiveDefinitionData(
    string Name,
    string? LawReference,
    string? DeclarationCode,
    IncentiveKind Kind,
    IncentiveParameters Parameters,
    int? MaxMonths,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

/// <summary>
/// A tenant-wide incentive model (PAY-INCENTIVE-PLAN "IncentiveDefinition"). Rates and durations are tenant data, never
/// code: only an SMMM-approved definition may influence the calculation. The approval is a declaration flag and is
/// dropped automatically whenever a calculation-relevant field changes (kind, parameters, declaration code, max months,
/// dates); renaming or editing the law reference keeps it.
/// </summary>
public sealed partial class IncentiveDefinition : TenantAggregateRoot<Guid>
{
    public const int NameMaxLength = 100;
    public const int LawReferenceMaxLength = 200;
    public const int ApprovalNoteMaxLength = 500;
    public const int DeclarationCodeMaxLength = 10;
    public const int MinMaxMonths = 1;
    public const int MaxMaxMonths = 120;

    private const string DeclarationCodeField = "declarationCode";

    private IncentiveDefinition()
    {
    }

    private IncentiveDefinition(Guid tenantId, string code, IncentiveDefinitionData data, string? templateKey)
        : base(Guid.NewGuid(), tenantId)
    {
        Code = code;
        TemplateKey = templateKey;
        IsActive = true;
        Apply(data);
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? LawReference { get; private set; }

    /// <summary>Digits only (1..10); required for approval and for assignment in the SGK category.</summary>
    public string? DeclarationCode { get; private set; }

    public IncentiveKind Kind { get; private set; }

    public IncentiveParameters Parameters { get; private set; } = IncentiveParameters.Empty;

    /// <summary>When set, an assignment needs an end month and the employee's total months of this definition cannot exceed it.</summary>
    public int? MaxMonths { get; private set; }

    public bool IsSmmmApproved { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Null for the system-approved 5510 template.</summary>
    public Guid? ApprovedByUserId { get; private set; }

    public string? ApprovalNote { get; private set; }

    public bool IsActive { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public string? TemplateKey { get; private set; }

    public IncentiveCategory Category => CategoryOf(Kind);

    /// <summary>Whether everything approval needs is present (parameters of the kind, and the declaration code in the SGK category).</summary>
    public bool IsComplete => MissingField() is null;

    public static IncentiveCategory CategoryOf(IncentiveKind kind) =>
        kind == IncentiveKind.IncomeTaxWithholdingRelief ? IncentiveCategory.Tax : IncentiveCategory.Sgk;

    public static Result<IncentiveDefinition> Create(Guid tenantId, string code, IncentiveDefinitionData data, string? templateKey = null)
    {
        Guard.NotDefault(tenantId);
        var normalizedCode = (code ?? string.Empty).Trim();
        if (!CodePattern().IsMatch(normalizedCode))
        {
            return Error.Validation(PayrollDomainErrors.IncentiveCodeInvalid);
        }

        var validated = Validate(data);
        return validated.IsFailure ? validated.Error : new IncentiveDefinition(tenantId, normalizedCode, Normalize(data), templateKey);
    }

    /// <summary>Creates a definition from a catalog template; pre-approved templates are approved by the system (no user).</summary>
    public static Result<IncentiveDefinition> CreateFromTemplate(Guid tenantId, IncentiveTemplate template, DateOnly validFrom, DateTimeOffset now)
    {
        var created = Create(tenantId, template.Code, template.ToData(validFrom), template.TemplateKey);
        if (created.IsFailure || !template.PreApproved)
        {
            return created;
        }

        var definition = created.Value;
        definition.IsSmmmApproved = true;
        definition.ApprovedAt = now;
        definition.ApprovalNote = template.ApprovalNote;
        return definition;
    }

    /// <summary>Replaces the content. The approval is dropped when a calculation-relevant field changed.</summary>
    public Result Update(IncentiveDefinitionData data)
    {
        var validated = Validate(data);
        if (validated.IsFailure)
        {
            return validated;
        }

        var normalized = Normalize(data);
        if (AffectsCalculation(normalized))
        {
            ClearApproval(note: null);
        }

        Apply(normalized);
        return Result.Success();
    }

    /// <summary>Approves a complete definition. The legislation keys' existence is verified by the Application layer first.</summary>
    public Result Approve(Guid userId, DateTimeOffset now, string? note)
    {
        Guard.NotDefault(userId);
        var trimmedNote = TrimNote(note);
        if (trimmedNote is { Length: > ApprovalNoteMaxLength })
        {
            return Error.Validation(PayrollDomainErrors.IncentiveParametersInvalid, ("field", "approvalNote"));
        }

        if (MissingField() is { } missing)
        {
            return missing == DeclarationCodeField
                ? Error.Validation(PayrollDomainErrors.IncentiveDeclarationCodeRequired)
                : Error.Validation(PayrollDomainErrors.IncentiveParametersIncomplete, ("field", missing));
        }

        IsSmmmApproved = true;
        ApprovedAt = now;
        ApprovedByUserId = userId;
        ApprovalNote = trimmedNote;
        return Result.Success();
    }

    public Result RevokeApproval(string? note)
    {
        var trimmedNote = TrimNote(note);
        if (trimmedNote is { Length: > ApprovalNoteMaxLength })
        {
            return Error.Validation(PayrollDomainErrors.IncentiveParametersInvalid, ("field", "approvalNote"));
        }

        ClearApproval(trimmedNote);
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Whether the definition is usable (active and date range overlapping) in the month starting <paramref name="monthStart"/>.</summary>
    public bool IsEffectiveInMonth(DateOnly monthStart)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return IsActive && ValidFrom <= monthEnd && (ValidTo is null || ValidTo >= monthStart);
    }

    private static string? TrimNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private string? MissingField()
    {
        if (Category == IncentiveCategory.Sgk && DeclarationCode is null) return DeclarationCodeField;
        if (Kind == IncentiveKind.IncomeTaxWithholdingRelief
            && TemplateKey is IncentiveTemplates.Tr5746Gv or IncentiveTemplates.Tr4691Gv
            && (ValidTo is null || ValidTo >= new DateOnly(2025, 8, 1))
            && Parameters.TaxReliefGrossCapMultiplier != 40m)
            return "taxReliefGrossCapMultiplier";
        return Parameters.FirstMissingField(Kind);
    }

    private bool AffectsCalculation(IncentiveDefinitionData data) =>
        data.Kind != Kind
        || data.Parameters != Parameters
        || data.DeclarationCode != DeclarationCode
        || data.MaxMonths != MaxMonths
        || data.ValidFrom != ValidFrom
        || data.ValidTo != ValidTo;

    private void ClearApproval(string? note)
    {
        IsSmmmApproved = false;
        ApprovedAt = null;
        ApprovedByUserId = null;
        ApprovalNote = note;
    }

    private static IncentiveDefinitionData Normalize(IncentiveDefinitionData data) => data with
    {
        Name = data.Name.Trim(),
        LawReference = string.IsNullOrWhiteSpace(data.LawReference) ? null : data.LawReference.Trim(),
        DeclarationCode = string.IsNullOrWhiteSpace(data.DeclarationCode) ? null : data.DeclarationCode.Trim(),
        Parameters = (data.Parameters ?? IncentiveParameters.Empty).Copy(),
    };

    private static Result Validate(IncentiveDefinitionData data)
    {
        var name = (data.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > NameMaxLength || (data.LawReference?.Trim().Length ?? 0) > LawReferenceMaxLength)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveNameInvalid);
        }

        if (!Enum.IsDefined(data.Kind))
        {
            return Error.Validation(PayrollDomainErrors.IncentiveParametersInvalid, ("field", "kind"));
        }

        var declarationCode = data.DeclarationCode?.Trim();
        if (!string.IsNullOrEmpty(declarationCode) && !DeclarationCodePattern().IsMatch(declarationCode))
        {
            return Error.Validation(PayrollDomainErrors.IncentiveDeclarationCodeInvalid);
        }

        if (data.MaxMonths is { } max && (max < MinMaxMonths || max > MaxMaxMonths))
        {
            return Error.Validation(PayrollDomainErrors.IncentiveParametersInvalid, ("field", "maxMonths"));
        }

        if (data.ValidTo is { } to && to < data.ValidFrom)
        {
            return Error.Validation(PayrollDomainErrors.IncentiveDateRangeInvalid);
        }

        return (data.Parameters ?? IncentiveParameters.Empty).Validate(data.Kind);
    }

    private void Apply(IncentiveDefinitionData data)
    {
        Name = data.Name;
        LawReference = data.LawReference;
        DeclarationCode = data.DeclarationCode;
        Kind = data.Kind;
        Parameters = data.Parameters;
        MaxMonths = data.MaxMonths;
        ValidFrom = data.ValidFrom;
        ValidTo = data.ValidTo;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeclarationCodePattern();
}

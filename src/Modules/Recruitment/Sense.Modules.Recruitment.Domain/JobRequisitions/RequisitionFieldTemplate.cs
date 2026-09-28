using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.JobRequisitions;

/// <summary>
/// The existing <see cref="JobRequisition"/> fields whose visibility/required-ness can be customized per
/// department (roadmap #87 "departman bazlı sablon/alan özellestirme"). Title/OrgUnitId/HeadcountRequested/
/// RequesterEmployeeId are always visible and required - only these three carry real optionality today.
/// </summary>
public enum RequisitionFormField
{
    EmploymentType,
    Reason,
    Justification,
}

/// <summary>
/// Caller-supplied shape for one field's rule, used by <see cref="RequisitionFieldTemplate.Create"/>/
/// <see cref="RequisitionFieldTemplate.ReplaceRules"/>.
/// </summary>
public sealed record RequisitionFieldRuleInput(RequisitionFormField Field, bool IsVisible, bool IsRequired, string? DefaultValue);

/// <summary>
/// One field's customized rule, owned by a <see cref="RequisitionFieldTemplate"/>. <see cref="DefaultValue"/> only
/// applies to the enum fields (<see cref="RequisitionFormField.EmploymentType"/>/<see cref="RequisitionFormField.Reason"/>):
/// when <see cref="IsRequired"/> is false, it is the value <see cref="JobRequisition.Create"/> falls back to if the
/// caller omits the field (an enum can't be left null, unlike <see cref="RequisitionFormField.Justification"/>).
/// </summary>
public sealed class RequisitionFieldRule
{
    private RequisitionFieldRule()
    {
    }

    public RequisitionFieldRule(RequisitionFormField field, bool isVisible, bool isRequired, string? defaultValue)
    {
        Field = field;
        IsVisible = isVisible;
        IsRequired = isRequired;
        DefaultValue = defaultValue;
    }

    public RequisitionFormField Field { get; private set; }

    public bool IsVisible { get; private set; }

    public bool IsRequired { get; private set; }

    public string? DefaultValue { get; private set; }
}

/// <summary>
/// Per-department (<see cref="JobRequisition.OrgUnitId"/>) customization of which <see cref="JobRequisition"/>
/// fields are shown/required (roadmap #87). At most one template per (tenant, OrgUnit). When no template exists
/// for a department, or a field has no rule in it, <see cref="Resolve"/> returns the original fixed behavior
/// (EmploymentType/Reason visible+required, Justification visible+optional) - department customization is
/// additive, never a prerequisite. No new field TYPES are introduced here, only show/require toggles for the
/// three fields already on <see cref="JobRequisition"/> that carry real optionality.
/// </summary>
public sealed class RequisitionFieldTemplate : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<RequisitionFieldRule> _rules = [];

    private RequisitionFieldTemplate()
    {
    }

    private RequisitionFieldTemplate(Guid id, Guid tenantId, Guid orgUnitId)
        : base(id, tenantId)
    {
        OrgUnitId = orgUnitId;
    }

    public Guid OrgUnitId { get; private set; }

    public IReadOnlyList<RequisitionFieldRule> Rules => _rules.AsReadOnly();

    public static Result<RequisitionFieldTemplate> Create(Guid tenantId, Guid orgUnitId, IReadOnlyList<RequisitionFieldRuleInput> rules, Guid? id = null)
    {
        var validation = ValidateRules(rules);
        if (validation is not null)
        {
            return validation;
        }

        var template = new RequisitionFieldTemplate(id ?? Guid.CreateVersion7(), tenantId, orgUnitId);
        template.ReplaceRulesUnchecked(rules);
        return template;
    }

    /// <summary>Replaces the whole rule set (like <see cref="RecruitmentFormTemplate.ReplaceFields"/>).</summary>
    public Result ReplaceRules(IReadOnlyList<RequisitionFieldRuleInput> rules)
    {
        var validation = ValidateRules(rules);
        if (validation is not null)
        {
            return validation;
        }

        ReplaceRulesUnchecked(rules);
        return Result.Success();
    }

    /// <summary>The effective rule for one field: the department's override if it has one, otherwise the original fixed behavior.</summary>
    public static RequisitionFieldRule Resolve(RequisitionFormField field, RequisitionFieldTemplate? template)
    {
        var overrideRule = template?._rules.FirstOrDefault(r => r.Field == field);
        if (overrideRule is not null)
        {
            return overrideRule;
        }

        var isRequired = field != RequisitionFormField.Justification;
        return new RequisitionFieldRule(field, isVisible: true, isRequired, defaultValue: null);
    }

    private void ReplaceRulesUnchecked(IReadOnlyList<RequisitionFieldRuleInput> rules)
    {
        _rules.Clear();
        foreach (var r in rules)
        {
            _rules.Add(new RequisitionFieldRule(r.Field, r.IsVisible, r.IsRequired, r.DefaultValue?.Trim()));
        }
    }

    private static Error? ValidateRules(IReadOnlyList<RequisitionFieldRuleInput> rules)
    {
        if (rules.Count == 0)
        {
            return Error.Validation(RecruitmentDomainErrors.RequisitionFieldTemplateRulesRequired);
        }

        if (rules.Select(r => r.Field).Distinct().Count() != rules.Count)
        {
            return Error.Validation(RecruitmentDomainErrors.RequisitionFieldTemplateDuplicateField);
        }

        if (rules.Any(r => r.IsRequired && !r.IsVisible))
        {
            return Error.Validation(RecruitmentDomainErrors.RequisitionFieldTemplateRequiredFieldMustBeVisible);
        }

        foreach (var r in rules)
        {
            if (r.Field == RequisitionFormField.Justification)
            {
                continue;
            }

            // EmploymentType/Reason can't be left null, so an optional one needs a concrete fallback value.
            if (!r.IsRequired && !HasValidDefault(r.Field, r.DefaultValue))
            {
                return Error.Validation(RecruitmentDomainErrors.RequisitionFieldTemplateDefaultValueRequired);
            }
        }

        return null;
    }

    private static bool HasValidDefault(RequisitionFormField field, string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return false;
        }

        return field switch
        {
            RequisitionFormField.EmploymentType => Enum.TryParse<RecruitmentEmploymentType>(defaultValue, ignoreCase: true, out _),
            RequisitionFormField.Reason => Enum.TryParse<RequisitionReason>(defaultValue, ignoreCase: true, out _),
            _ => false,
        };
    }
}

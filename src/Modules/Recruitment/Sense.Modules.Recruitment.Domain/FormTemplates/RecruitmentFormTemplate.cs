using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.FormTemplates;

/// <summary>What a form template is used for (roadmap #86/#87's unified `RecruitmentFormTemplate`).</summary>
public enum RecruitmentFormTemplateType
{
    /// <summary>Interview feedback, filled in by an interviewer after a stage.</summary>
    Feedback,

    /// <summary>Weighted feedback: its <see cref="FormFieldType.ScoreCard"/> fields' weights must sum to 100.</summary>
    Scorecard,

    /// <summary>A stage's outcome/result form (e.g. "işe alım kararı").</summary>
    Outcome,
}

/// <summary>A form field's input type (roadmap #86: Skor kart / Paragraf / Tek satır / Tarih / Açılır liste / Onay kutusu).</summary>
public enum FormFieldType
{
    ScoreCard,
    Paragraph,
    SingleLine,
    Date,
    Dropdown,
    Checkbox,
}

/// <summary>
/// A reusable form definition (roadmap #86 form builder). R2b scope: fields are fixed at creation
/// (<see cref="ReplaceFields"/> replaces the whole set, like <see cref="Sense.Modules.Recruitment.Domain.JobPostings.JobPosting.SetAccess"/>) -
/// per-stage rendering/branching logic is not modeled. <see cref="FormResponses.RecruitmentFormResponse"/> answers
/// reference a field by its <see cref="FormField.Id"/>, which is why fields carry a stable, client-generated id
/// instead of a positional index.
/// </summary>
public sealed class RecruitmentFormTemplate : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<FormField> _fields = [];

    private RecruitmentFormTemplate()
    {
    }

    private RecruitmentFormTemplate(Guid id, Guid tenantId, string name, RecruitmentFormTemplateType templateType)
        : base(id, tenantId)
    {
        Name = name;
        TemplateType = templateType;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public RecruitmentFormTemplateType TemplateType { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<FormField> Fields => _fields.AsReadOnly();

    public static Result<RecruitmentFormTemplate> Create(
        Guid tenantId, string name, RecruitmentFormTemplateType templateType, IReadOnlyList<FormFieldInput> fields, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(RecruitmentDomainErrors.FormTemplateNameRequired);
        }

        var validation = ValidateFields(templateType, fields);
        if (validation is not null)
        {
            return validation;
        }

        var template = new RecruitmentFormTemplate(id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(name.Trim(), RecruitmentLimits.NameMaxLength), templateType);
        template.ReplaceFieldsUnchecked(fields);
        return template;
    }

    /// <summary>Replaces the field set. Existing <see cref="FormResponses.RecruitmentFormResponse"/> answers keep referencing removed fields' ids by value (orphaned, not cascaded).</summary>
    public Result ReplaceFields(IReadOnlyList<FormFieldInput> fields)
    {
        var validation = ValidateFields(TemplateType, fields);
        if (validation is not null)
        {
            return validation;
        }

        ReplaceFieldsUnchecked(fields);
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(RecruitmentDomainErrors.FormTemplateAlreadyInactive);
        }

        IsActive = false;
        return Result.Success();
    }

    public void Reactivate() => IsActive = true;

    private void ReplaceFieldsUnchecked(IReadOnlyList<FormFieldInput> fields)
    {
        _fields.Clear();
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            _fields.Add(new FormField(f.Id ?? Guid.CreateVersion7(), f.Label, f.FieldType, f.IsRequired, f.IsSensitive, i, f.Options, f.Weight));
        }
    }

    private static Error? ValidateFields(RecruitmentFormTemplateType templateType, IReadOnlyList<FormFieldInput> fields)
    {
        if (fields.Count == 0)
        {
            return Error.Validation(RecruitmentDomainErrors.FormTemplateFieldsRequired);
        }

        if (fields.Any(f => string.IsNullOrWhiteSpace(f.Label)))
        {
            return Error.Validation(RecruitmentDomainErrors.FormFieldLabelRequired);
        }

        if (fields.Any(f => f.FieldType == FormFieldType.Dropdown && (f.Options is null || f.Options.Count == 0)))
        {
            return Error.Validation(RecruitmentDomainErrors.FormFieldOptionsRequired);
        }

        if (templateType == RecruitmentFormTemplateType.Scorecard)
        {
            var scoreFields = fields.Where(f => f.FieldType == FormFieldType.ScoreCard).ToList();
            if (scoreFields.Count == 0)
            {
                return Error.Validation(RecruitmentDomainErrors.ScorecardRequiresScoreFields);
            }

            if (scoreFields.Sum(f => f.Weight ?? 0) != 100)
            {
                return Error.Validation(RecruitmentDomainErrors.ScorecardWeightsMustSumTo100);
            }
        }

        return null;
    }
}

/// <summary>Caller-supplied shape for a field being added/kept via <see cref="RecruitmentFormTemplate.Create"/>/<see cref="RecruitmentFormTemplate.ReplaceFields"/>; <see cref="Id"/> null means "new field".</summary>
public sealed record FormFieldInput(Guid? Id, string Label, FormFieldType FieldType, bool IsRequired, bool IsSensitive, IReadOnlyList<string>? Options, int? Weight);

/// <summary>One field of a <see cref="RecruitmentFormTemplate"/>. Owned collection; <see cref="Id"/> is stable so answers can reference it.</summary>
public sealed class FormField : AuditableRecord
{
    private FormField()
    {
    }

    public FormField(Guid id, string label, FormFieldType fieldType, bool isRequired, bool isSensitive, int sortOrder, IReadOnlyList<string>? options, int? weight)
    {
        Id = id;
        Label = Guard.MaxLength(label.Trim(), RecruitmentLimits.NameMaxLength);
        FieldType = fieldType;
        IsRequired = isRequired;
        IsSensitive = isSensitive;
        SortOrder = sortOrder;
        Options = options ?? [];
        Weight = fieldType == FormFieldType.ScoreCard ? weight : null;
    }

    /// <summary>Client-generated (not store-generated) - see <see cref="Sense.Modules.Workflow.Domain.ApprovalRequests.ApprovalTask"/>'s identical comment for why this matters to EF's Added/Modified tracking.</summary>
    public Guid Id { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public FormFieldType FieldType { get; private set; }

    public bool IsRequired { get; private set; }

    public bool IsSensitive { get; private set; }

    public int SortOrder { get; private set; }

    public IReadOnlyList<string> Options { get; private set; } = [];

    /// <summary>Scorecard weight (0-100); only meaningful for <see cref="FormFieldType.ScoreCard"/> fields.</summary>
    public int? Weight { get; private set; }
}

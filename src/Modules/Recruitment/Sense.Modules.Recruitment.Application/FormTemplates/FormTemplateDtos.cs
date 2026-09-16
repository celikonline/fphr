namespace Sense.Modules.Recruitment.Application.FormTemplates;

/// <summary>Read model for a form field.</summary>
public sealed record FormFieldDto(Guid Id, string Label, string FieldType, bool IsRequired, bool IsSensitive, int SortOrder, IReadOnlyList<string> Options, int? Weight);

/// <summary>A field being created/kept via <see cref="CreateFormTemplateCommand"/>/<see cref="ReplaceFormTemplateFieldsCommand"/>; null <paramref name="Id"/> means "new field".</summary>
public sealed record FormFieldInputDto(Guid? Id, string Label, string FieldType, bool IsRequired, bool IsSensitive, IReadOnlyList<string>? Options, int? Weight);

/// <summary>Read model for a <see cref="Sense.Modules.Recruitment.Domain.FormTemplates.RecruitmentFormTemplate"/>.</summary>
public sealed record FormTemplateDto(Guid Id, string Name, string TemplateType, bool IsActive, IReadOnlyList<FormFieldDto> Fields);

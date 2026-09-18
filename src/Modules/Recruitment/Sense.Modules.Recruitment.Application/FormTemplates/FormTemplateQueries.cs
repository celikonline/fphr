using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.FormTemplates;

/// <summary>Lists form templates, optionally active only.</summary>
[RequiresPermission("recruitment.form_templates.read")]
public sealed record ListFormTemplatesQuery(bool ActiveOnly, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<FormTemplateDto>>;

public sealed class ListFormTemplatesHandler(IRecruitmentFormTemplateRepository templates)
    : IQueryHandler<ListFormTemplatesQuery, IReadOnlyList<FormTemplateDto>>
{
    public async Task<Result<IReadOnlyList<FormTemplateDto>>> Handle(ListFormTemplatesQuery query, CancellationToken cancellationToken)
    {
        var list = await templates.ListAsync(query.ActiveOnly, query.Take, cancellationToken);
        IReadOnlyList<FormTemplateDto> dtos = list.Select(FormTemplateMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>A single form template by id.</summary>
[RequiresPermission("recruitment.form_templates.read")]
public sealed record GetFormTemplateQuery(Guid TemplateId) : IQuery<FormTemplateDto>;

public sealed class GetFormTemplateHandler(IRecruitmentFormTemplateRepository templates) : IQueryHandler<GetFormTemplateQuery, FormTemplateDto>
{
    public async Task<Result<FormTemplateDto>> Handle(GetFormTemplateQuery query, CancellationToken cancellationToken)
    {
        var existing = await templates.GetByIdAsync(query.TemplateId, cancellationToken);
        return existing.IsFailure ? existing.Error : FormTemplateMapper.ToDto(existing.Value);
    }
}

internal static class FormTemplateMapper
{
    public static FormTemplateDto ToDto(RecruitmentFormTemplate template) =>
        new(
            template.Id,
            template.Name,
            template.TemplateType.ToString(),
            template.IsActive,
            template.Fields.Select(f => new FormFieldDto(f.Id, f.Label, f.FieldType.ToString(), f.IsRequired, f.IsSensitive, f.SortOrder, f.Options, f.Weight)).ToList());
}

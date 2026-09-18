using FluentValidation;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.FormTemplates;

/// <summary>Creates a form template (roadmap #86).</summary>
[RequiresPermission("recruitment.form_templates.manage")]
public sealed record CreateFormTemplateCommand(string Name, string TemplateType, IReadOnlyList<FormFieldInputDto> Fields) : ICommand<Guid>;

public sealed class CreateFormTemplateValidator : AbstractValidator<CreateFormTemplateCommand>
{
    public CreateFormTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.TemplateType).IsEnumName(typeof(RecruitmentFormTemplateType), caseSensitive: false);
        RuleFor(x => x.Fields).NotEmpty();
        RuleForEach(x => x.Fields).ChildRules(field =>
        {
            field.RuleFor(f => f.Label).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
            field.RuleFor(f => f.FieldType).IsEnumName(typeof(FormFieldType), caseSensitive: false);
        });
    }
}

public sealed class CreateFormTemplateHandler(ITenantContext tenant, IRecruitmentFormTemplateRepository templates)
    : ICommandHandler<CreateFormTemplateCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateFormTemplateCommand command, CancellationToken cancellationToken)
    {
        var created = RecruitmentFormTemplate.Create(
            tenant.TenantId, command.Name, FormFieldMapper.ParseTemplateType(command.TemplateType), FormFieldMapper.ToDomainInputs(command.Fields));
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        templates.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

/// <summary>Replaces a template's field set.</summary>
[RequiresPermission("recruitment.form_templates.manage")]
public sealed record ReplaceFormTemplateFieldsCommand(Guid TemplateId, IReadOnlyList<FormFieldInputDto> Fields) : ICommand;

public sealed class ReplaceFormTemplateFieldsValidator : AbstractValidator<ReplaceFormTemplateFieldsCommand>
{
    public ReplaceFormTemplateFieldsValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.Fields).NotEmpty();
        RuleForEach(x => x.Fields).ChildRules(field =>
        {
            field.RuleFor(f => f.Label).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
            field.RuleFor(f => f.FieldType).IsEnumName(typeof(FormFieldType), caseSensitive: false);
        });
    }
}

public sealed class ReplaceFormTemplateFieldsHandler(IRecruitmentFormTemplateRepository templates) : ICommandHandler<ReplaceFormTemplateFieldsCommand>
{
    public async Task<Result> Handle(ReplaceFormTemplateFieldsCommand command, CancellationToken cancellationToken)
    {
        var existing = await templates.GetByIdAsync(command.TemplateId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.ReplaceFields(FormFieldMapper.ToDomainInputs(command.Fields));
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Deactivates a form template.</summary>
[RequiresPermission("recruitment.form_templates.manage")]
public sealed record DeactivateFormTemplateCommand(Guid TemplateId) : ICommand;

public sealed class DeactivateFormTemplateHandler(IRecruitmentFormTemplateRepository templates) : ICommandHandler<DeactivateFormTemplateCommand>
{
    public async Task<Result> Handle(DeactivateFormTemplateCommand command, CancellationToken cancellationToken)
    {
        var existing = await templates.GetByIdAsync(command.TemplateId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Deactivate();
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Reactivates a form template.</summary>
[RequiresPermission("recruitment.form_templates.manage")]
public sealed record ReactivateFormTemplateCommand(Guid TemplateId) : ICommand;

public sealed class ReactivateFormTemplateHandler(IRecruitmentFormTemplateRepository templates) : ICommandHandler<ReactivateFormTemplateCommand>
{
    public async Task<Result> Handle(ReactivateFormTemplateCommand command, CancellationToken cancellationToken)
    {
        var existing = await templates.GetByIdAsync(command.TemplateId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        existing.Value.Reactivate();
        return Result.Success();
    }
}

internal static class FormFieldMapper
{
    public static IReadOnlyList<FormFieldInput> ToDomainInputs(IReadOnlyList<FormFieldInputDto> fields) =>
        fields.Select(f => new FormFieldInput(f.Id, f.Label, ParseFieldType(f.FieldType, f.Label), f.IsRequired, f.IsSensitive, f.Options, f.Weight)).ToList();

    public static RecruitmentFormTemplateType ParseTemplateType(string value) =>
        Enum.TryParse<RecruitmentFormTemplateType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Invalid template type: '{value}'.", nameof(value));

    private static FormFieldType ParseFieldType(string value, string label) =>
        Enum.TryParse<FormFieldType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Invalid field type: '{value}' (field '{label}').", nameof(value));
}

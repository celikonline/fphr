using FluentValidation;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.JobRequisitions;

/// <summary>One field's visible/required rule, as read or written through the API (roadmap #87). <see cref="Field"/> is a <see cref="RequisitionFormField"/> name.</summary>
public sealed record RequisitionFieldRuleDto(string Field, bool IsVisible, bool IsRequired, string? DefaultValue);

/// <summary>
/// Creates or replaces a department's <see cref="JobRequisition"/> field customization (roadmap #87). A department
/// (OrgUnit) without a template keeps the original fixed behavior - see <see cref="RequisitionFieldTemplate.Resolve"/>.
/// </summary>
[RequiresPermission("recruitment.requisitions.manage")]
public sealed record SetRequisitionFieldTemplateCommand(Guid OrgUnitId, IReadOnlyList<RequisitionFieldRuleDto> Rules) : ICommand;

public sealed class SetRequisitionFieldTemplateValidator : AbstractValidator<SetRequisitionFieldTemplateCommand>
{
    public SetRequisitionFieldTemplateValidator()
    {
        RuleFor(x => x.OrgUnitId).NotEmpty();
        RuleFor(x => x.Rules).NotEmpty();
        RuleForEach(x => x.Rules).ChildRules(rule =>
            rule.RuleFor(r => r.Field).IsEnumName(typeof(RequisitionFormField), caseSensitive: false));
    }
}

public sealed class SetRequisitionFieldTemplateHandler(ITenantContext tenant, IRequisitionFieldTemplateRepository templates, IOrgUnitLookup orgUnits)
    : ICommandHandler<SetRequisitionFieldTemplateCommand>
{
    public async Task<Result> Handle(SetRequisitionFieldTemplateCommand command, CancellationToken cancellationToken)
    {
        if (!await orgUnits.ExistsActiveAsync(tenant.TenantId, command.OrgUnitId, cancellationToken))
        {
            return Error.Validation(RecruitmentErrors.OrgUnitInvalid);
        }

        var inputs = command.Rules
            .Select(r => new RequisitionFieldRuleInput(Enum.Parse<RequisitionFormField>(r.Field, ignoreCase: true), r.IsVisible, r.IsRequired, r.DefaultValue))
            .ToList();

        var existing = await templates.FindByOrgUnitAsync(command.OrgUnitId, cancellationToken);
        if (existing is not null)
        {
            var replaced = existing.ReplaceRules(inputs);
            return replaced.IsFailure ? replaced.Error : Result.Success();
        }

        var created = RequisitionFieldTemplate.Create(tenant.TenantId, command.OrgUnitId, inputs);
        if (created.IsFailure)
        {
            return created.Error;
        }

        templates.Add(created.Value);
        return Result.Success();
    }
}

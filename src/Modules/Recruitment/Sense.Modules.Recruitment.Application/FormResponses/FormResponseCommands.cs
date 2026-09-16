using FluentValidation;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.FormResponses;

/// <summary>One field's answer, as submitted by the caller.</summary>
public sealed record FieldAnswerInput(Guid FieldId, string? Value);

/// <summary>Submits a form response for an application (roadmap flow #3 "Mülakat geri bildirimi"; also satisfies AutomationRule gating, see <see cref="Applications.MoveJobApplicationStageCommand"/>).</summary>
[RequiresPermission("recruitment.form_responses.submit")]
public sealed record SubmitFormResponseCommand(Guid TemplateId, Guid JobApplicationId, IReadOnlyList<FieldAnswerInput> Answers) : ICommand<Guid>;

public sealed class SubmitFormResponseValidator : AbstractValidator<SubmitFormResponseCommand>
{
    public SubmitFormResponseValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.JobApplicationId).NotEmpty();
    }
}

public sealed class SubmitFormResponseHandler(
    ITenantContext tenant, ICurrentUser currentUser, IRecruitmentFormTemplateRepository templates,
    IJobApplicationRepository applications, IRecruitmentFormResponseRepository responses)
    : ICommandHandler<SubmitFormResponseCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitFormResponseCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.Validation(RecruitmentErrors.NoEmployeeForUser);
        }

        var template = await templates.GetByIdAsync(command.TemplateId, cancellationToken);
        if (template.IsFailure)
        {
            return template.Error;
        }

        var application = await applications.GetByIdAsync(command.JobApplicationId, cancellationToken);
        if (application.IsFailure)
        {
            return application.Error;
        }

        var answersByFieldId = command.Answers.ToDictionary(a => a.FieldId, a => a.Value);
        var submitted = RecruitmentFormResponse.Submit(tenant.TenantId, template.Value, command.JobApplicationId, employeeId, answersByFieldId);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        responses.Add(submitted.Value);
        return submitted.Value.Id;
    }
}

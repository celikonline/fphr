using FluentValidation;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.JobRequisitions;

/// <summary>
/// Creates a job requisition in Draft status. EmploymentType/Reason are nullable so a department whose
/// <see cref="RequisitionFieldTemplate"/> makes them optional can omit them (roadmap #87) - see
/// <see cref="CreateJobRequisitionHandler"/> for how an omitted field is resolved.
/// </summary>
[RequiresPermission("recruitment.requisitions.manage")]
public sealed record CreateJobRequisitionCommand(
    string Title, Guid OrgUnitId, string? EmploymentType, int HeadcountRequested, string? Reason, string? Justification) : ICommand<Guid>;

public sealed class CreateJobRequisitionValidator : AbstractValidator<CreateJobRequisitionCommand>
{
    public CreateJobRequisitionValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(RecruitmentLimits.TitleMaxLength);
        RuleFor(x => x.OrgUnitId).NotEmpty();
        RuleFor(x => x.EmploymentType).IsEnumName(typeof(RecruitmentEmploymentType), caseSensitive: false).When(x => x.EmploymentType is not null);
        RuleFor(x => x.HeadcountRequested).GreaterThan(0);
        RuleFor(x => x.Reason).IsEnumName(typeof(RequisitionReason), caseSensitive: false).When(x => x.Reason is not null);
    }
}

public sealed class CreateJobRequisitionHandler(
    ITenantContext tenant, ICurrentUser currentUser, IJobRequisitionRepository requisitions, IOrgUnitLookup orgUnits, IRequisitionFieldTemplateRepository fieldTemplates)
    : ICommandHandler<CreateJobRequisitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateJobRequisitionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.Validation(RecruitmentErrors.NoEmployeeForUser);
        }

        if (!await orgUnits.ExistsActiveAsync(tenant.TenantId, command.OrgUnitId, cancellationToken))
        {
            return Error.Validation(RecruitmentErrors.OrgUnitInvalid);
        }

        var template = await fieldTemplates.FindByOrgUnitAsync(command.OrgUnitId, cancellationToken);

        var employmentType = ResolveEnumField<RecruitmentEmploymentType>(
            RequisitionFormField.EmploymentType, template, command.EmploymentType, RecruitmentDomainErrors.RequisitionEmploymentTypeRequired);
        if (employmentType.IsFailure)
        {
            return employmentType.Error;
        }

        var reason = ResolveEnumField<RequisitionReason>(
            RequisitionFormField.Reason, template, command.Reason, RecruitmentDomainErrors.RequisitionReasonRequired);
        if (reason.IsFailure)
        {
            return reason.Error;
        }

        var justificationRule = RequisitionFieldTemplate.Resolve(RequisitionFormField.Justification, template);
        if (justificationRule.IsRequired && string.IsNullOrWhiteSpace(command.Justification))
        {
            return Error.Validation(RecruitmentDomainErrors.RequisitionJustificationRequired);
        }

        var created = JobRequisition.Create(
            tenant.TenantId, command.Title, command.OrgUnitId, employmentType.Value,
            command.HeadcountRequested, reason.Value, command.Justification, employeeId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        requisitions.Add(created.Value);
        return created.Value.Id;
    }

    /// <summary>
    /// A supplied value is always parsed as-is (already enum-validated by <see cref="CreateJobRequisitionValidator"/>).
    /// An omitted value falls back to the department's rule: an error if the field is required there, otherwise the
    /// template's configured <see cref="RequisitionFieldRule.DefaultValue"/> (an enum can't be left null, unlike
    /// <see cref="RequisitionFormField.Justification"/>) - no department template means the original fixed
    /// behavior (always required), matching pre-existing behavior exactly.
    /// </summary>
    private static Result<TEnum> ResolveEnumField<TEnum>(
        RequisitionFormField field, RequisitionFieldTemplate? template, string? rawValue, string requiredErrorCode)
        where TEnum : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            return Enum.Parse<TEnum>(rawValue, ignoreCase: true);
        }

        var rule = RequisitionFieldTemplate.Resolve(field, template);
        if (rule.IsRequired)
        {
            return Error.Validation(requiredErrorCode);
        }

        return Enum.Parse<TEnum>(rule.DefaultValue!, ignoreCase: true);
    }
}

/// <summary>Submits a requisition for approval (Draft -> PendingApproval), starting a Workflow "Requisition" approval process.</summary>
[RequiresPermission("recruitment.requisitions.manage")]
public sealed record SubmitJobRequisitionCommand(Guid RequisitionId) : ICommand;

public sealed class SubmitJobRequisitionHandler(IJobRequisitionRepository requisitions, IWorkflowService workflow, ICurrentUser currentUser)
    : ICommandHandler<SubmitJobRequisitionCommand>
{
    public async Task<Result> Handle(SubmitJobRequisitionCommand command, CancellationToken cancellationToken)
    {
        var existing = await requisitions.GetByIdAsync(command.RequisitionId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var submitted = existing.Value.Submit();
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var approval = await workflow.StartAsync(
            ApprovalRequestTypes.Requisition, existing.Value.Id, existing.Value.RequesterEmployeeId, null,
            currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        existing.Value.AttachApproval(approval.Value);
        return Result.Success();
    }
}

/// <summary>Cancels a requisition (Draft/PendingApproval -> Cancelled), cancelling its Workflow approval first if one is pending.</summary>
[RequiresPermission("recruitment.requisitions.manage")]
public sealed record CancelJobRequisitionCommand(Guid RequisitionId) : ICommand;

public sealed class CancelJobRequisitionHandler(IJobRequisitionRepository requisitions, IWorkflowService workflow, ICurrentUser currentUser)
    : ICommandHandler<CancelJobRequisitionCommand>
{
    public async Task<Result> Handle(CancelJobRequisitionCommand command, CancellationToken cancellationToken)
    {
        var existing = await requisitions.GetByIdAsync(command.RequisitionId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.ApprovalRequestId is { } approvalRequestId)
        {
            var cancelled = await workflow.CancelAsync(approvalRequestId, currentUser.EmployeeId ?? existing.Value.RequesterEmployeeId, cancellationToken);
            if (cancelled.IsFailure)
            {
                return cancelled.Error;
            }
        }

        var result = existing.Value.Cancel();
        return result.IsFailure ? result.Error : Result.Success();
    }
}

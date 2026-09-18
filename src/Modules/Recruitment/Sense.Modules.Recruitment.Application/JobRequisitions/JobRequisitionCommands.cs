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

/// <summary>Creates a job requisition in Draft status.</summary>
[RequiresPermission("recruitment.requisitions.manage")]
public sealed record CreateJobRequisitionCommand(
    string Title, Guid OrgUnitId, string EmploymentType, int HeadcountRequested, string Reason, string? Justification) : ICommand<Guid>;

public sealed class CreateJobRequisitionValidator : AbstractValidator<CreateJobRequisitionCommand>
{
    public CreateJobRequisitionValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(RecruitmentLimits.TitleMaxLength);
        RuleFor(x => x.OrgUnitId).NotEmpty();
        RuleFor(x => x.EmploymentType).IsEnumName(typeof(RecruitmentEmploymentType), caseSensitive: false);
        RuleFor(x => x.HeadcountRequested).GreaterThan(0);
        RuleFor(x => x.Reason).IsEnumName(typeof(RequisitionReason), caseSensitive: false);
    }
}

public sealed class CreateJobRequisitionHandler(ITenantContext tenant, ICurrentUser currentUser, IJobRequisitionRepository requisitions, IOrgUnitLookup orgUnits)
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

        var created = JobRequisition.Create(
            tenant.TenantId, command.Title, command.OrgUnitId, Enum.Parse<RecruitmentEmploymentType>(command.EmploymentType, ignoreCase: true),
            command.HeadcountRequested, Enum.Parse<RequisitionReason>(command.Reason, ignoreCase: true), command.Justification, employeeId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        requisitions.Add(created.Value);
        return created.Value.Id;
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

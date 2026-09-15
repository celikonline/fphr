using FluentValidation;
using Sense.Modules.Recruitment.Contracts.Events;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Applications;

/// <summary>Applies a candidate to a job posting (manual, pipeline stage starts at Applied).</summary>
[RequiresPermission("recruitment.applications.manage")]
public sealed record CreateJobApplicationCommand(Guid JobPostingId, Guid CandidateId) : ICommand<Guid>;

public sealed class CreateJobApplicationValidator : AbstractValidator<CreateJobApplicationCommand>
{
    public CreateJobApplicationValidator()
    {
        RuleFor(x => x.JobPostingId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class CreateJobApplicationHandler(
    ITenantContext tenant, IJobApplicationRepository applications, IJobPostingRepository postings, ICandidateRepository candidates)
    : ICommandHandler<CreateJobApplicationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateJobApplicationCommand command, CancellationToken cancellationToken)
    {
        var posting = await postings.GetByIdAsync(command.JobPostingId, cancellationToken);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        var candidate = await candidates.GetByIdAsync(command.CandidateId, cancellationToken);
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        var application = JobApplication.Create(tenant.TenantId, command.JobPostingId, command.CandidateId);
        applications.Add(application);
        return application.Id;
    }
}

/// <summary>Moves an application to a new pipeline stage (manual in R0; rule-engine side effects are R2).</summary>
[RequiresPermission("recruitment.applications.manage")]
public sealed record MoveJobApplicationStageCommand(Guid ApplicationId, string Stage) : ICommand;

public sealed class MoveJobApplicationStageValidator : AbstractValidator<MoveJobApplicationStageCommand>
{
    public MoveJobApplicationStageValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.Stage).IsEnumName(typeof(ApplicationStage));
    }
}

public sealed class MoveJobApplicationStageHandler(IJobApplicationRepository applications, IIntegrationEventOutbox outbox)
    : ICommandHandler<MoveJobApplicationStageCommand>
{
    public async Task<Result> Handle(MoveJobApplicationStageCommand command, CancellationToken cancellationToken)
    {
        var existing = await applications.GetByIdAsync(command.ApplicationId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var moved = existing.Value.MoveStage(Enum.Parse<ApplicationStage>(command.Stage));
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        outbox.Enqueue(new JobApplicationStageChanged(
            existing.Value.TenantId, existing.Value.Id, existing.Value.JobPostingId, existing.Value.CandidateId, command.Stage));
        return Result.Success();
    }
}

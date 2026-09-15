using FluentValidation;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using ContractEvents = Sense.Modules.Performance.Contracts.Events;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record ReviewAnswerInputDto(Guid QuestionId, decimal Score, string? Comment);

/// <summary>Submits one evaluator's full form (all questions at once) for a single assignment.</summary>
[RequiresPermission("performance.form.fill")]
public sealed record SubmitReviewResponseCommand(Guid ReviewCycleId, Guid AssignmentId, Guid SubmittingEmployeeId, IReadOnlyList<ReviewAnswerInputDto> Answers) : ICommand;

public sealed class SubmitReviewResponseValidator : AbstractValidator<SubmitReviewResponseCommand>
{
    public SubmitReviewResponseValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.AssignmentId).NotEmpty();
        RuleFor(x => x.SubmittingEmployeeId).NotEmpty();
        RuleFor(x => x.Answers).NotNull().NotEmpty();
        RuleForEach(x => x.Answers).ChildRules(a =>
        {
            a.RuleFor(x => x.QuestionId).NotEmpty();
            a.RuleFor(x => x.Score).InclusiveBetween(0m, 100m);
            a.RuleFor(x => x.Comment).MaximumLength(PerformanceLimits.CommentMaxLength);
        });
    }
}

public sealed class SubmitReviewResponseHandler(IReviewCycleRepository cycles, IIntegrationEventOutbox outbox, TimeProvider clock)
    : ICommandHandler<SubmitReviewResponseCommand>
{
    public async Task<Result> Handle(SubmitReviewResponseCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var cycle = existing.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var answers = command.Answers.Select(a => (a.QuestionId, a.Score, a.Comment)).ToList();
        var result = cycle.SubmitResponse(command.AssignmentId, command.SubmittingEmployeeId, answers, now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var participant = cycle.Participants.FirstOrDefault(p => p.Assignments.Any(a => a.Id == command.AssignmentId));
        if (participant is { Status: ReviewParticipantStatus.Completed, CompletedAtUtc: { } completedAt } && completedAt == now)
        {
            outbox.Enqueue(new ContractEvents.ReviewParticipantCompleted(cycle.TenantId, cycle.Id, participant.EmployeeId, WasForced: false));
        }

        return Result.Success();
    }
}

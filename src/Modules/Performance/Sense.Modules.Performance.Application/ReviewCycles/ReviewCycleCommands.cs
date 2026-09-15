using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using ContractEvents = Sense.Modules.Performance.Contracts.Events;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record RelationConfigInputDto(EvaluatorRelation Relation, bool IsEnabled, decimal? Weight, int? MinRequired);

public sealed record QuestionInputDto(int Order, string Text, decimal? Weight, CommentMode? CommentMode);

public sealed record CompetencyInputDto(string Name, decimal? Weight, IReadOnlyList<QuestionInputDto> Questions);

/// <summary>Creates a review cycle (sihirbaz "Kaydet ve bitir" - Genel bilgiler + Yetkinlik + Sorular + Erişim in one call).</summary>
[RequiresPermission("performance.cycle.create")]
public sealed record CreateReviewCycleCommand(
    string Name, DateOnly PeriodStart, DateOnly PeriodEnd, RatingScale RatingScale, CommentMode DefaultCommentMode,
    EvaluatorLabelMode EvaluatorLabelMode, bool ReportShowPhoto, bool EmployeeCanViewResults, bool EmployeeCanDownloadPdf,
    IReadOnlyList<RelationConfigInputDto> RelationConfigs, IReadOnlyList<CompetencyInputDto> Competencies) : ICommand<Guid>;

public sealed class CreateReviewCycleValidator : AbstractValidator<CreateReviewCycleCommand>
{
    public CreateReviewCycleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart);
        RuleFor(x => x.RelationConfigs).NotNull().Must(c => c.Count == 4);
        RuleFor(x => x.Competencies).NotNull().NotEmpty();
        RuleForEach(x => x.Competencies).ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
            c.RuleFor(x => x.Questions).NotNull().NotEmpty();
            c.RuleForEach(x => x.Questions).ChildRules(q => q.RuleFor(x => x.Text).NotEmpty().MaximumLength(PerformanceLimits.TextMaxLength));
        });
    }
}

public sealed class CreateReviewCycleHandler(ITenantContext tenant, IReviewCycleRepository cycles) : ICommandHandler<CreateReviewCycleCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateReviewCycleCommand command, CancellationToken cancellationToken)
    {
        var created = ReviewCycle.Create(
            tenant.TenantId, command.Name, command.PeriodStart, command.PeriodEnd, ReviewKind.Competency,
            command.RatingScale, command.DefaultCommentMode, command.EvaluatorLabelMode, command.ReportShowPhoto,
            command.EmployeeCanViewResults, command.EmployeeCanDownloadPdf,
            ToDomainRelationConfigs(command.RelationConfigs), ToDomainCompetencies(command.Competencies));

        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        cycles.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }

    internal static IReadOnlyList<RelationConfigInput> ToDomainRelationConfigs(IReadOnlyList<RelationConfigInputDto> configs) =>
        configs.Select(c => new RelationConfigInput(c.Relation, c.IsEnabled, c.Weight, c.MinRequired)).ToList();

    internal static IReadOnlyList<CompetencyInput> ToDomainCompetencies(IReadOnlyList<CompetencyInputDto> competencies) =>
        competencies.Select(c => new CompetencyInput(
            c.Name, c.Weight, c.Questions.Select(q => new QuestionInput(q.Order, q.Text, q.Weight, q.CommentMode)).ToList())).ToList();
}

/// <summary>Edits a review cycle while it is still Draft (sihirbaza geri dönüp düzenleme).</summary>
[RequiresPermission("performance.cycle.update")]
public sealed record UpdateReviewCycleDraftCommand(
    Guid ReviewCycleId, string Name, DateOnly PeriodStart, DateOnly PeriodEnd, RatingScale RatingScale, CommentMode DefaultCommentMode,
    EvaluatorLabelMode EvaluatorLabelMode, bool ReportShowPhoto, bool EmployeeCanViewResults, bool EmployeeCanDownloadPdf,
    IReadOnlyList<RelationConfigInputDto> RelationConfigs, IReadOnlyList<CompetencyInputDto> Competencies) : ICommand;

public sealed class UpdateReviewCycleDraftValidator : AbstractValidator<UpdateReviewCycleDraftCommand>
{
    public UpdateReviewCycleDraftValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart);
        RuleFor(x => x.RelationConfigs).NotNull().Must(c => c.Count == 4);
        RuleFor(x => x.Competencies).NotNull().NotEmpty();
        RuleForEach(x => x.Competencies).ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
            c.RuleFor(x => x.Questions).NotNull().NotEmpty();
            c.RuleForEach(x => x.Questions).ChildRules(q => q.RuleFor(x => x.Text).NotEmpty().MaximumLength(PerformanceLimits.TextMaxLength));
        });
    }
}

public sealed class UpdateReviewCycleDraftHandler(IReviewCycleRepository cycles) : ICommandHandler<UpdateReviewCycleDraftCommand>
{
    public async Task<Result> Handle(UpdateReviewCycleDraftCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.UpdateDraft(
            command.Name, command.PeriodStart, command.PeriodEnd, command.RatingScale, command.DefaultCommentMode,
            command.EvaluatorLabelMode, command.ReportShowPhoto, command.EmployeeCanViewResults, command.EmployeeCanDownloadPdf,
            CreateReviewCycleHandler.ToDomainRelationConfigs(command.RelationConfigs), CreateReviewCycleHandler.ToDomainCompetencies(command.Competencies));
        return result.IsFailure ? result.Error : Result.Success();
    }
}

public sealed record ParticipantEvaluatorsInputDto(EvaluatorRelation Relation, IReadOnlyList<Guid> EvaluatorEmployeeIds);

public sealed record AddParticipantInputDto(Guid EmployeeId, bool IncludeGoals, IReadOnlyList<ParticipantEvaluatorsInputDto> Evaluators);

/// <summary>Adds participants with their per-category evaluators (Katılımcılar adımı).</summary>
[RequiresPermission("performance.participant.manage")]
public sealed record AddParticipantsCommand(Guid ReviewCycleId, IReadOnlyList<AddParticipantInputDto> Participants) : ICommand;

public sealed class AddParticipantsValidator : AbstractValidator<AddParticipantsCommand>
{
    public AddParticipantsValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.Participants).NotNull().NotEmpty();
        RuleForEach(x => x.Participants).ChildRules(p => p.RuleFor(x => x.EmployeeId).NotEmpty());
    }
}

public sealed class AddParticipantsHandler(ITenantContext tenant, IReviewCycleRepository cycles, IEmployeeLookup employeeLookup)
    : ICommandHandler<AddParticipantsCommand>
{
    public async Task<Result> Handle(AddParticipantsCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var allEmployeeIds = command.Participants
            .SelectMany(p => new[] { p.EmployeeId }.Concat(p.Evaluators.SelectMany(e => e.EvaluatorEmployeeIds)))
            .Distinct()
            .ToList();

        foreach (var employeeId in allEmployeeIds)
        {
            if (!await employeeLookup.ExistsActiveAsync(tenant.TenantId, employeeId, cancellationToken))
            {
                return Error.Validation(PerformanceErrors.EvaluatorNotActive, (nameof(employeeId), employeeId));
            }
        }

        var inputs = command.Participants
            .Select(p => new ParticipantInput(
                p.EmployeeId, p.IncludeGoals,
                p.Evaluators.ToDictionary(e => e.Relation, e => (IReadOnlyList<Guid>)e.EvaluatorEmployeeIds)))
            .ToList();

        var result = existing.Value.AddParticipants(inputs);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Draft → Published: opens the cycle's forms for evaluators.</summary>
[RequiresPermission("performance.cycle.publish")]
public sealed record PublishReviewCycleCommand(Guid ReviewCycleId) : ICommand;

public sealed class PublishReviewCycleValidator : AbstractValidator<PublishReviewCycleCommand>
{
    public PublishReviewCycleValidator() => RuleFor(x => x.ReviewCycleId).NotEmpty();
}

public sealed class PublishReviewCycleHandler(IReviewCycleRepository cycles, IIntegrationEventOutbox outbox, TimeProvider clock)
    : ICommandHandler<PublishReviewCycleCommand>
{
    public async Task<Result> Handle(PublishReviewCycleCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var cycle = existing.Value;
        var result = cycle.Publish(clock.GetUtcNow().UtcDateTime);
        if (result.IsFailure)
        {
            return result.Error;
        }

        outbox.Enqueue(new ContractEvents.ReviewCyclePublished(cycle.TenantId, cycle.Id));
        return Result.Success();
    }
}

/// <summary>"Değerlendirmeyi tamamla": force-completes every participant currently meeting the per-category minimum.</summary>
[RequiresPermission("performance.cycle.force_complete")]
public sealed record ForceCompleteReviewCycleCommand(Guid ReviewCycleId) : ICommand<int>;

public sealed class ForceCompleteReviewCycleValidator : AbstractValidator<ForceCompleteReviewCycleCommand>
{
    public ForceCompleteReviewCycleValidator() => RuleFor(x => x.ReviewCycleId).NotEmpty();
}

public sealed class ForceCompleteReviewCycleHandler(IReviewCycleRepository cycles, IIntegrationEventOutbox outbox, TimeProvider clock)
    : ICommandHandler<ForceCompleteReviewCycleCommand, int>
{
    public async Task<Result<int>> Handle(ForceCompleteReviewCycleCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var cycle = existing.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var result = cycle.ForceComplete(now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        foreach (var participant in cycle.Participants.Where(p => p.CompletedAtUtc == now))
        {
            outbox.Enqueue(new ContractEvents.ReviewParticipantCompleted(cycle.TenantId, cycle.Id, participant.EmployeeId, WasForced: true));
        }

        return result.Value;
    }
}

using FluentValidation;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Modules.Performance.Domain.ReviewForms;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewForms;

public sealed record ReviewFormQuestionInput(string Text, int Weight);

/// <summary>Creates a review form (form + soru/ağırlık) under a review cycle. Rejected once the cycle is Closed.</summary>
[RequiresPermission("performance.reviews.manage")]
public sealed record CreateReviewFormCommand(Guid ReviewCycleId, string Title, IReadOnlyList<ReviewFormQuestionInput> Questions) : ICommand<Guid>;

public sealed class CreateReviewFormValidator : AbstractValidator<CreateReviewFormCommand>
{
    public CreateReviewFormValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
        RuleFor(x => x.Questions).NotEmpty();
    }
}

public sealed class CreateReviewFormHandler(ITenantContext tenant, IReviewCycleRepository cycles, IReviewFormRepository forms)
    : ICommandHandler<CreateReviewFormCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateReviewFormCommand command, CancellationToken cancellationToken)
    {
        var cycle = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (cycle.IsFailure)
        {
            return cycle.Error;
        }

        if (cycle.Value.Status == ReviewCycleStatus.Closed)
        {
            return Error.Rule(PerformanceErrors.FormCycleClosed);
        }

        var created = ReviewForm.Create(tenant.TenantId, command.ReviewCycleId, command.Title);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var questionsSet = created.Value.SetQuestions(command.Questions.Select(q => (q.Text, q.Weight)).ToList());
        if (questionsSet.IsFailure)
        {
            return questionsSet.Error;
        }

        forms.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Draft -> Published, terminal for the form's question structure.</summary>
[RequiresPermission("performance.reviews.manage")]
public sealed record PublishReviewFormCommand(Guid ReviewFormId) : ICommand;

public sealed class PublishReviewFormHandler(IReviewFormRepository forms) : ICommandHandler<PublishReviewFormCommand>
{
    public async Task<Result> Handle(PublishReviewFormCommand command, CancellationToken cancellationToken)
    {
        var existing = await forms.GetByIdAsync(command.ReviewFormId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Publish();
        return result.IsFailure ? result.Error : Result.Success();
    }
}

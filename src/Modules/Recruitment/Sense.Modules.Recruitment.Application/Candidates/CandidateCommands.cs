using FluentValidation;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Candidates;

/// <summary>Adds a candidate manually (career-site self-apply is a later Recruitment package).</summary>
[RequiresPermission("recruitment.candidates.manage")]
public sealed record CreateCandidateCommand(string FirstName, string LastName, string Email, string? Phone) : ICommand<Guid>;

public sealed class CreateCandidateValidator : AbstractValidator<CreateCandidateCommand>
{
    public CreateCandidateValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(RecruitmentLimits.EmailMaxLength);
    }
}

public sealed class CreateCandidateHandler(ITenantContext tenant, ICandidateRepository candidates) : ICommandHandler<CreateCandidateCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateCandidateCommand command, CancellationToken cancellationToken)
    {
        var created = Candidate.Create(tenant.TenantId, command.FirstName, command.LastName, command.Email, command.Phone);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        candidates.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

/// <summary>Edits a candidate's contact information.</summary>
[RequiresPermission("recruitment.candidates.manage")]
public sealed record UpdateCandidateCommand(Guid CandidateId, string FirstName, string LastName, string Email, string? Phone) : ICommand;

public sealed class UpdateCandidateValidator : AbstractValidator<UpdateCandidateCommand>
{
    public UpdateCandidateValidator()
    {
        RuleFor(x => x.CandidateId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(RecruitmentLimits.NameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(RecruitmentLimits.EmailMaxLength);
    }
}

public sealed class UpdateCandidateHandler(ICandidateRepository candidates) : ICommandHandler<UpdateCandidateCommand>
{
    public async Task<Result> Handle(UpdateCandidateCommand command, CancellationToken cancellationToken)
    {
        var existing = await candidates.GetByIdAsync(command.CandidateId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var updated = existing.Value.UpdateContactInfo(command.FirstName, command.LastName, command.Email, command.Phone);
        return updated.IsFailure ? updated.Error : Result.Success();
    }
}

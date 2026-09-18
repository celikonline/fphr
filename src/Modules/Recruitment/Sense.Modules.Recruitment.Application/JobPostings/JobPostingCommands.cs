using FluentValidation;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Contracts.Events;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.JobPostings;

/// <summary>Creates a job posting in Draft status. <paramref name="RequisitionId"/> is optional (roadmap #87); when set, <see cref="PublishJobPostingCommand"/> requires it to be Approved.</summary>
[RequiresPermission("recruitment.postings.create")]
public sealed record CreateJobPostingCommand(
    string Title, Guid OrgUnitId, Guid? LocationId, string EmploymentType, string? Description, Guid OwnerUserId,
    Guid? HiringManagerUserId, Guid? RequisitionId = null)
    : ICommand<Guid>;

public sealed class CreateJobPostingValidator : AbstractValidator<CreateJobPostingCommand>
{
    public CreateJobPostingValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(RecruitmentLimits.TitleMaxLength);
        RuleFor(x => x.OrgUnitId).NotEmpty();
        RuleFor(x => x.EmploymentType).IsEnumName(typeof(RecruitmentEmploymentType), caseSensitive: false);
        RuleFor(x => x.OwnerUserId).NotEmpty();
    }
}

public sealed class CreateJobPostingHandler(
    ITenantContext tenant, IJobPostingRepository postings, IOrgUnitLookup orgUnits, ILocationLookup locations, IUserAccountLookup users)
    : ICommandHandler<CreateJobPostingCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateJobPostingCommand command, CancellationToken cancellationToken)
    {
        var validation = await ValidateReferencesAsync(
            tenant.TenantId, command.OrgUnitId, command.LocationId, command.OwnerUserId, command.HiringManagerUserId,
            orgUnits, locations, users, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var created = JobPosting.Create(
            tenant.TenantId, command.Title, command.OrgUnitId, command.LocationId,
            Enum.Parse<RecruitmentEmploymentType>(command.EmploymentType, ignoreCase: true), command.Description, command.OwnerUserId,
            command.HiringManagerUserId, command.RequisitionId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        postings.Add(created.Value);
        return created.Value.Id;
    }

    internal static async Task<Error?> ValidateReferencesAsync(
        Guid tenantId, Guid orgUnitId, Guid? locationId, Guid ownerUserId, Guid? hiringManagerUserId,
        IOrgUnitLookup orgUnits, ILocationLookup locations, IUserAccountLookup users, CancellationToken ct)
    {
        if (!await orgUnits.ExistsActiveAsync(tenantId, orgUnitId, ct))
        {
            return Error.Validation(RecruitmentErrors.OrgUnitInvalid);
        }

        if (locationId is { } locId && !await locations.ExistsActiveAsync(tenantId, locId, ct))
        {
            return Error.Validation(RecruitmentErrors.LocationInvalid);
        }

        var userIds = hiringManagerUserId is { } managerId ? [ownerUserId, managerId] : (IReadOnlyCollection<Guid>)[ownerUserId];
        var accounts = await users.GetAsync(tenantId, userIds, ct);

        if (!accounts.ContainsKey(ownerUserId))
        {
            return Error.Validation(RecruitmentErrors.OwnerUserInvalid);
        }

        if (hiringManagerUserId is { } hmId && !accounts.ContainsKey(hmId))
        {
            return Error.Validation(RecruitmentErrors.HiringManagerUserInvalid);
        }

        return null;
    }
}

/// <summary>Edits a job posting's fields (does not change its status).</summary>
[RequiresPermission("recruitment.postings.update")]
public sealed record UpdateJobPostingCommand(
    Guid PostingId, string Title, Guid OrgUnitId, Guid? LocationId, string EmploymentType,
    string? Description, string? CompanyNotes, Guid? HiringManagerUserId, DateOnly? ClosingDate) : ICommand;

public sealed class UpdateJobPostingValidator : AbstractValidator<UpdateJobPostingCommand>
{
    public UpdateJobPostingValidator()
    {
        RuleFor(x => x.PostingId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(RecruitmentLimits.TitleMaxLength);
        RuleFor(x => x.OrgUnitId).NotEmpty();
        RuleFor(x => x.EmploymentType).IsEnumName(typeof(RecruitmentEmploymentType), caseSensitive: false);
    }
}

public sealed class UpdateJobPostingHandler(
    ITenantContext tenant, IJobPostingRepository postings, IOrgUnitLookup orgUnits, ILocationLookup locations, IUserAccountLookup users)
    : ICommandHandler<UpdateJobPostingCommand>
{
    public async Task<Result> Handle(UpdateJobPostingCommand command, CancellationToken cancellationToken)
    {
        var existing = await postings.GetByIdAsync(command.PostingId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var validation = await CreateJobPostingHandler.ValidateReferencesAsync(
            tenant.TenantId, command.OrgUnitId, command.LocationId, existing.Value.OwnerUserId, command.HiringManagerUserId,
            orgUnits, locations, users, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var updated = existing.Value.Update(
            command.Title, command.OrgUnitId, command.LocationId, Enum.Parse<RecruitmentEmploymentType>(command.EmploymentType, ignoreCase: true),
            command.Description, command.CompanyNotes, command.HiringManagerUserId, command.ClosingDate);
        return updated.IsFailure ? updated.Error : Result.Success();
    }
}

/// <summary>Publishes a posting (Draft/OnHold/Closed -> Open).</summary>
[RequiresPermission("recruitment.postings.update")]
public sealed record PublishJobPostingCommand(Guid PostingId) : ICommand;

public sealed class PublishJobPostingHandler(IJobPostingRepository postings, IJobRequisitionRepository requisitions, IIntegrationEventOutbox outbox)
    : ICommandHandler<PublishJobPostingCommand>
{
    public async Task<Result> Handle(PublishJobPostingCommand command, CancellationToken cancellationToken)
    {
        var existing = await postings.GetByIdAsync(command.PostingId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.RequisitionId is { } requisitionId)
        {
            var requisition = await requisitions.FindByIdAsync(requisitionId, cancellationToken);
            if (requisition is null || requisition.Status != RequisitionStatus.Approved)
            {
                return Error.Rule(RecruitmentErrors.RequisitionNotApproved);
            }
        }

        var wasPublishedBefore = existing.Value.PublishedAt is not null;
        var published = existing.Value.Publish();
        if (published.IsFailure)
        {
            return published.Error;
        }

        if (!wasPublishedBefore)
        {
            outbox.Enqueue(new JobPostingPublished(existing.Value.TenantId, existing.Value.Id, existing.Value.Title));
        }

        return Result.Success();
    }
}

/// <summary>Closes an open posting (reversible via Publish).</summary>
[RequiresPermission("recruitment.postings.update")]
public sealed record CloseJobPostingCommand(Guid PostingId) : ICommand;

public sealed class CloseJobPostingHandler(IJobPostingRepository postings) : ICommandHandler<CloseJobPostingCommand>
{
    public async Task<Result> Handle(CloseJobPostingCommand command, CancellationToken cancellationToken)
    {
        var existing = await postings.GetByIdAsync(command.PostingId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var closed = existing.Value.Close();
        return closed.IsFailure ? closed.Error : Result.Success();
    }
}

/// <summary>Archives a posting (terminal).</summary>
[RequiresPermission("recruitment.postings.archive")]
public sealed record ArchiveJobPostingCommand(Guid PostingId, string Reason) : ICommand;

public sealed class ArchiveJobPostingValidator : AbstractValidator<ArchiveJobPostingCommand>
{
    public ArchiveJobPostingValidator()
    {
        RuleFor(x => x.PostingId).NotEmpty();
        RuleFor(x => x.Reason).IsEnumName(typeof(JobPostingArchiveReason), caseSensitive: false);
    }
}

public sealed class ArchiveJobPostingHandler(IJobPostingRepository postings) : ICommandHandler<ArchiveJobPostingCommand>
{
    public async Task<Result> Handle(ArchiveJobPostingCommand command, CancellationToken cancellationToken)
    {
        var existing = await postings.GetByIdAsync(command.PostingId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var archived = existing.Value.Archive(Enum.Parse<JobPostingArchiveReason>(command.Reason, ignoreCase: true));
        return archived.IsFailure ? archived.Error : Result.Success();
    }
}

/// <summary>Replaces a posting's explicit access list (roadmap #83).</summary>
[RequiresPermission("recruitment.postings.update")]
public sealed record SetJobPostingAccessCommand(Guid PostingId, IReadOnlyList<Guid> UserIds) : ICommand;

public sealed class SetJobPostingAccessHandler(IJobPostingRepository postings) : ICommandHandler<SetJobPostingAccessCommand>
{
    public async Task<Result> Handle(SetJobPostingAccessCommand command, CancellationToken cancellationToken)
    {
        var existing = await postings.GetByIdAsync(command.PostingId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        existing.Value.SetAccess(command.UserIds);
        return Result.Success();
    }
}

using FluentValidation;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Length/enum checks for AnnouncementContent (cross-field event rules live in the Announcement aggregate).</summary>
public sealed class AnnouncementContentValidator : AbstractValidator<AnnouncementContent>
{
    public AnnouncementContentValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(AnnouncementsLimits.TitleMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(AnnouncementsLimits.BodyMaxLength);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Location).MaximumLength(AnnouncementsLimits.LocationMaxLength);
        RuleFor(x => x.MeetingUrl).MaximumLength(AnnouncementsLimits.UrlMaxLength);
    }
}

/// <summary>Creates a Draft announcement (Category=Event requires EventStartsAt; other categories forbid event fields).</summary>
[RequiresPermission("announcements.posts.manage")]
public sealed record CreateAnnouncementCommand(AnnouncementContent Content) : ICommand<Guid>;

public sealed class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator() => RuleFor(x => x.Content).NotNull().SetValidator(new AnnouncementContentValidator());
}

public sealed class CreateAnnouncementHandler(ITenantContext tenant, IAnnouncementRepository announcements)
    : ICommandHandler<CreateAnnouncementCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var created = Announcement.CreateDraft(tenant.TenantId, command.Content);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        announcements.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

[RequiresPermission("announcements.posts.manage")]
public sealed record UpdateAnnouncementCommand(Guid AnnouncementId, AnnouncementContent Content) : ICommand;

public sealed class UpdateAnnouncementValidator : AbstractValidator<UpdateAnnouncementCommand>
{
    public UpdateAnnouncementValidator()
    {
        RuleFor(x => x.AnnouncementId).NotEmpty();
        RuleFor(x => x.Content).NotNull().SetValidator(new AnnouncementContentValidator());
    }
}

public sealed class UpdateAnnouncementHandler(IAnnouncementRepository announcements) : ICommandHandler<UpdateAnnouncementCommand>
{
    public async Task<Result> Handle(UpdateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var existing = await announcements.GetByIdAsync(command.AnnouncementId, cancellationToken);
        return existing.IsFailure ? existing.Error : existing.Value.Update(command.Content);
    }
}

[RequiresPermission("announcements.posts.manage")]
public sealed record PublishAnnouncementCommand(Guid AnnouncementId) : ICommand;

public sealed class PublishAnnouncementValidator : AbstractValidator<PublishAnnouncementCommand>
{
    public PublishAnnouncementValidator() => RuleFor(x => x.AnnouncementId).NotEmpty();
}

public sealed class PublishAnnouncementHandler(IAnnouncementRepository announcements, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<PublishAnnouncementCommand>
{
    public async Task<Result> Handle(PublishAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var existing = await announcements.GetByIdAsync(command.AnnouncementId, cancellationToken);
        return existing.IsFailure
            ? existing.Error
            : existing.Value.Publish(currentUser.UserId, clock.GetUtcNow().UtcDateTime);
    }
}

[RequiresPermission("announcements.posts.manage")]
public sealed record ArchiveAnnouncementCommand(Guid AnnouncementId) : ICommand;

public sealed class ArchiveAnnouncementValidator : AbstractValidator<ArchiveAnnouncementCommand>
{
    public ArchiveAnnouncementValidator() => RuleFor(x => x.AnnouncementId).NotEmpty();
}

public sealed class ArchiveAnnouncementHandler(IAnnouncementRepository announcements) : ICommandHandler<ArchiveAnnouncementCommand>
{
    public async Task<Result> Handle(ArchiveAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var existing = await announcements.GetByIdAsync(command.AnnouncementId, cancellationToken);
        return existing.IsFailure ? existing.Error : existing.Value.Archive();
    }
}

/// <summary>Pins (Published only) or unpins an announcement; pinned announcements lead the feed.</summary>
[RequiresPermission("announcements.posts.manage")]
public sealed record SetAnnouncementPinnedCommand(Guid AnnouncementId, bool IsPinned) : ICommand;

public sealed class SetAnnouncementPinnedValidator : AbstractValidator<SetAnnouncementPinnedCommand>
{
    public SetAnnouncementPinnedValidator() => RuleFor(x => x.AnnouncementId).NotEmpty();
}

public sealed class SetAnnouncementPinnedHandler(IAnnouncementRepository announcements) : ICommandHandler<SetAnnouncementPinnedCommand>
{
    public async Task<Result> Handle(SetAnnouncementPinnedCommand command, CancellationToken cancellationToken)
    {
        var existing = await announcements.GetByIdAsync(command.AnnouncementId, cancellationToken);
        return existing.IsFailure ? existing.Error : existing.Value.SetPinned(command.IsPinned);
    }
}

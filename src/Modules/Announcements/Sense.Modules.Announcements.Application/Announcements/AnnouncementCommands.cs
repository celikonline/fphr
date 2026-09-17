using FluentValidation;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Commands;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Duyuru oluştur (İK tarafından).</summary>
[RequiresPermission("announcements.manage")]
public sealed record CreateAnnouncementCommand(
    string Title,
    string Content,
    AnnouncementType Type,
    AnnouncementPriority Priority,
    string? TargetAudience = null,
    DateTime? ExpiresAt = null
) : ICommand<Guid>;

public sealed class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode(AnnouncementsErrors.TitleRequired.Code);

        RuleFor(x => x.Content)
            .NotEmpty().WithErrorCode(AnnouncementsErrors.ContentRequired.Code);
    }
}

/// <summary>Duyuru oluştur — İK/HR tarafından (announcements.manage izni gerekir).</summary>
public sealed class CreateAnnouncementHandler(ITenantContext tenant, IAnnouncementRepository announcements)
    : ICommandHandler<CreateAnnouncementCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var created = Announcement.Create(
            tenant.TenantId,
            command.Title,
            command.Content,
            command.Type,
            command.Priority,
            command.TargetAudience);

        if (created.IsFailure)
        {
            return created.Error;
        }

        await announcements.AddAsync(created.Value, cancellationToken);
        return created.Value.Id;
    }
}

/// <summary>Duyuruyu yayınla.</summary>
[RequiresPermission("announcements.manage")]
public sealed record PublishAnnouncementCommand(
    Guid AnnouncementId
) : ICommand;

/// <summary>Duyuruyu yayınla — İK/HR tarafından.</summary>
public sealed class PublishAnnouncementHandler(ITenantContext tenant, IAnnouncementRepository announcements)
    : ICommandHandler<PublishAnnouncementCommand>
{
    public async Task<Result> Handle(PublishAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(command.AnnouncementId, cancellationToken);
        if (announcement is null)
        {
            return AnnouncementsErrors.AnnouncementNotFound;
        }

        var result = announcement.Publish(tenant.TenantId);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await announcements.UpdateAsync(announcement, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Duyuruyu arşivle.</summary>
[RequiresPermission("announcements.manage")]
public sealed record ArchiveAnnouncementCommand(
    Guid AnnouncementId
) : ICommand;

/// <summary>Duyuruyu arşivle — İK/HR tarafından.</summary>
public sealed class ArchiveAnnouncementHandler(IAnnouncementRepository announcements)
    : ICommandHandler<ArchiveAnnouncementCommand>
{
    public async Task<Result> Handle(ArchiveAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(command.AnnouncementId, cancellationToken);
        if (announcement is null)
        {
            return AnnouncementsErrors.AnnouncementNotFound;
        }

        var result = announcement.Archive();
        if (result.IsFailure)
        {
            return result.Error;
        }

        await announcements.UpdateAsync(announcement, cancellationToken);
        return Result.Success();
    }
}

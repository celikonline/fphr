using FluentValidation;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Duyuru oluştur (taslak; İK/Admin tarafından).</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record CreateAnnouncementCommand(
    string Title,
    string Content,
    AnnouncementType Type,
    AnnouncementPriority Priority,
    string? TargetAudience = null) : ICommand<Guid>;

public sealed class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(AnnouncementsLimits.TitleMaxLength);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(AnnouncementsLimits.ContentMaxLength);

        // Tanımsız enum değerleri (ör. type:99) JsonStringEnumConverter'dan sayısal olarak geçebilir; kabul edilmez.
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.TargetAudience).MaximumLength(AnnouncementsLimits.TargetAudienceMaxLength);
    }
}

public sealed class CreateAnnouncementHandler(ITenantContext tenant, IAnnouncementRepository announcements)
    : ICommandHandler<CreateAnnouncementCommand, Guid>
{
#pragma warning disable CS1998 // senkron domain oluşturma; imza pipeline ile uyumlu olsun diye async
    public async Task<Result<Guid>> Handle(CreateAnnouncementCommand command, CancellationToken cancellationToken)
#pragma warning restore CS1998
    {
        var sanitizedContent = AnnouncementContentSanitizer.Sanitize(command.Content);
        var created = Announcement.Create(tenant.TenantId, command.Title, sanitizedContent, command.Type, command.Priority, command.TargetAudience);
        if (created.IsFailure)
        {
            return created.Error;
        }

        announcements.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Taslak duyuruyu düzenle (tam sayfa editör). Yalnızca taslak durumdayken serbesttir.</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record UpdateAnnouncementCommand(
    Guid AnnouncementId,
    string Title,
    string Content,
    AnnouncementType Type,
    AnnouncementPriority Priority,
    string? TargetAudience = null) : ICommand;

public sealed class UpdateAnnouncementValidator : AbstractValidator<UpdateAnnouncementCommand>
{
    public UpdateAnnouncementValidator()
    {
        RuleFor(x => x.AnnouncementId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(AnnouncementsLimits.TitleMaxLength);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(AnnouncementsLimits.ContentMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.TargetAudience).MaximumLength(AnnouncementsLimits.TargetAudienceMaxLength);
    }
}

public sealed class UpdateAnnouncementHandler(ITenantContext tenant, IAnnouncementRepository announcements)
    : ICommandHandler<UpdateAnnouncementCommand>
{
    public async Task<Result> Handle(UpdateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, command.AnnouncementId, cancellationToken);
        if (announcement is null)
        {
            return Error.NotFound(AnnouncementDomainErrors.NotFound);
        }

        var sanitizedContent = AnnouncementContentSanitizer.Sanitize(command.Content);
        return announcement.Update(command.Title, sanitizedContent, command.Type, command.Priority, command.TargetAudience);
    }
}

/// <summary>
/// Taslak duyuruyu yayınla. <see cref="NotifyEmployees"/> açıksa <see cref="AnnouncementPublished"/> yayınlanır ve
/// Notifications tüm aktif çalışanlara e-posta + uygulama içi bildirim gönderir.
/// </summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record PublishAnnouncementCommand(Guid AnnouncementId, DateTime? ExpiresAt = null, bool NotifyEmployees = false) : ICommand;

public sealed class PublishAnnouncementHandler(
    ITenantContext tenant, ICurrentUser currentUser, IAnnouncementRepository announcements, IIntegrationEventOutbox outbox)
    : ICommandHandler<PublishAnnouncementCommand>
{
    public async Task<Result> Handle(PublishAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, command.AnnouncementId, cancellationToken);
        if (announcement is null)
        {
            return Error.NotFound(AnnouncementDomainErrors.NotFound);
        }

        var published = announcement.Publish(currentUser.UserId ?? Guid.Empty, command.ExpiresAt);
        if (published.IsSuccess && command.NotifyEmployees)
        {
            outbox.Enqueue(new AnnouncementPublished(
                tenant.TenantId,
                announcement.Id,
                announcement.Title,
                AnnouncementExcerpt.FromHtml(announcement.Content),
                announcement.Priority >= AnnouncementPriority.High));
        }

        return published;
    }
}

/// <summary>Duyuruyu arşivle.</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record ArchiveAnnouncementCommand(Guid AnnouncementId) : ICommand;

public sealed class ArchiveAnnouncementHandler(ITenantContext tenant, IAnnouncementRepository announcements)
    : ICommandHandler<ArchiveAnnouncementCommand>
{
    public async Task<Result> Handle(ArchiveAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, command.AnnouncementId, cancellationToken);
        if (announcement is null)
        {
            return Error.NotFound(AnnouncementDomainErrors.NotFound);
        }

        return announcement.Archive();
    }
}

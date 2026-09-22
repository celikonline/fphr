using FluentValidation;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Attachments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Attachments;

/// <summary>One duyuru eki with a fresh, permission-checked download URL - never a stored/permanent one (see AnnouncementsLimits.ContentMaxLength's doc).</summary>
public sealed record AnnouncementAttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Kind, string DownloadUrl);

/// <summary>Response of initiating an attachment upload: the presigned PUT target plus the pending attachment's id.</summary>
public sealed record InitiateAnnouncementAttachmentUploadResponse(Guid AttachmentId, string UploadUrl, string RequestId, DateTime ExpiresAt);

/// <summary>Response of confirming an upload: the attachment is now usable, with a download URL ready for immediate use (e.g. inserting an inline image into the editor).</summary>
public sealed record ConfirmAnnouncementAttachmentUploadResponse(Guid AttachmentId, string DownloadUrl);

/// <summary>Starts uploading a duyuru attachment (inline image or downloadable file); only while the duyuru is still a draft.</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record InitiateAnnouncementAttachmentUploadCommand(
    Guid AnnouncementId, string FileName, string ContentType, long FileSizeBytes, AnnouncementAttachmentKind Kind)
    : ICommand<InitiateAnnouncementAttachmentUploadResponse>;

public sealed class InitiateAnnouncementAttachmentUploadValidator : AbstractValidator<InitiateAnnouncementAttachmentUploadCommand>
{
    public InitiateAnnouncementAttachmentUploadValidator()
    {
        RuleFor(x => x.AnnouncementId).NotEmpty();
        RuleFor(x => x.FileName).Must(name => AnnouncementUploadPolicy.SanitizeFileName(name).Length > 0)
            .WithMessage(AnnouncementDomainErrors.AttachmentTypeNotAllowed);
        RuleFor(x => x.FileSizeBytes).GreaterThan(0).LessThanOrEqualTo(AnnouncementsLimits.MaxAttachmentSizeBytes)
            .WithMessage(AnnouncementDomainErrors.AttachmentTooLarge);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x)
            .Must(x => AnnouncementUploadPolicy.IsAllowed(x.FileName, x.ContentType, x.Kind))
            .WithMessage(AnnouncementDomainErrors.AttachmentTypeNotAllowed);
    }
}

public sealed class InitiateAnnouncementAttachmentUploadHandler(
    IAnnouncementRepository announcements,
    IAnnouncementAttachmentRepository attachments,
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : ICommandHandler<InitiateAnnouncementAttachmentUploadCommand, InitiateAnnouncementAttachmentUploadResponse>
{
    public async Task<Result<InitiateAnnouncementAttachmentUploadResponse>> Handle(InitiateAnnouncementAttachmentUploadCommand command, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, command.AnnouncementId, cancellationToken);
        if (announcement is null)
        {
            return Error.NotFound(AnnouncementDomainErrors.NotFound);
        }

        if (announcement.Status != AnnouncementStatus.Draft)
        {
            return Error.Conflict(AnnouncementDomainErrors.CannotEditPublished);
        }

        var existingCount = await attachments.CountConfirmedByAnnouncementAsync(tenant.TenantId, command.AnnouncementId, cancellationToken);
        if (existingCount >= AnnouncementsLimits.MaxAttachmentsPerAnnouncement)
        {
            return Error.Validation(AnnouncementDomainErrors.TooManyAttachments);
        }

        var safeName = AnnouncementUploadPolicy.SanitizeFileName(command.FileName);
        var fileKey = $"{command.AnnouncementId:N}/{Guid.NewGuid():N}/{safeName}";
        var presigned = await fileStorage.InitiateUploadAsync(tenant.TenantId, AnnouncementAttachment.BucketName, fileKey, command.ContentType, cancellationToken);

        var attachment = AnnouncementAttachment.CreatePending(
            tenant.TenantId, command.AnnouncementId, safeName, fileKey, command.ContentType, command.Kind, currentUser.UserId ?? Guid.Empty);
        attachments.Add(attachment);

        return new InitiateAnnouncementAttachmentUploadResponse(attachment.Id, presigned.UploadUrl, presigned.RequestId, presigned.ExpiresAt);
    }
}

/// <summary>Confirms an attachment upload once the client's presigned PUT finished.</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record ConfirmAnnouncementAttachmentUploadCommand(Guid AttachmentId, string ETag, long ActualFileSizeBytes)
    : ICommand<ConfirmAnnouncementAttachmentUploadResponse>;

public sealed class ConfirmAnnouncementAttachmentUploadValidator : AbstractValidator<ConfirmAnnouncementAttachmentUploadCommand>
{
    public ConfirmAnnouncementAttachmentUploadValidator()
    {
        RuleFor(x => x.AttachmentId).NotEmpty();
        RuleFor(x => x.ETag).NotEmpty();
        RuleFor(x => x.ActualFileSizeBytes).GreaterThan(0);
    }
}

public sealed class ConfirmAnnouncementAttachmentUploadHandler(
    IAnnouncementAttachmentRepository attachments, IFileStorage fileStorage, ITenantContext tenant)
    : ICommandHandler<ConfirmAnnouncementAttachmentUploadCommand, ConfirmAnnouncementAttachmentUploadResponse>
{
    public async Task<Result<ConfirmAnnouncementAttachmentUploadResponse>> Handle(ConfirmAnnouncementAttachmentUploadCommand command, CancellationToken cancellationToken)
    {
        var attachment = await attachments.FindByIdAsync(tenant.TenantId, command.AttachmentId, cancellationToken);
        if (attachment is null)
        {
            return Error.NotFound(AnnouncementDomainErrors.AttachmentNotFound);
        }

        var actualSize = await fileStorage.ConfirmUploadAsync(tenant.TenantId, AnnouncementAttachment.BucketName, attachment.StorageKey, command.ETag, cancellationToken);
        attachment.Confirm(actualSize);

        var download = await fileStorage.GetDownloadUrlAsync(tenant.TenantId, AnnouncementAttachment.BucketName, attachment.StorageKey, expirationMinutes: 60, cancellationToken);
        return new ConfirmAnnouncementAttachmentUploadResponse(attachment.Id, download.DownloadUrl);
    }
}

/// <summary>Removes an attachment (draft duyuru only) from storage and the repository.</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record DeleteAnnouncementAttachmentCommand(Guid AttachmentId) : ICommand;

public sealed class DeleteAnnouncementAttachmentHandler(
    IAnnouncementAttachmentRepository attachments, IAnnouncementRepository announcements, IFileStorage fileStorage, ITenantContext tenant)
    : ICommandHandler<DeleteAnnouncementAttachmentCommand>
{
    public async Task<Result> Handle(DeleteAnnouncementAttachmentCommand command, CancellationToken cancellationToken)
    {
        var attachment = await attachments.FindByIdAsync(tenant.TenantId, command.AttachmentId, cancellationToken);
        if (attachment is null)
        {
            return Error.NotFound(AnnouncementDomainErrors.AttachmentNotFound);
        }

        var announcement = await announcements.GetByIdAsync(tenant.TenantId, attachment.AnnouncementId, cancellationToken);
        if (announcement is not null && announcement.Status != AnnouncementStatus.Draft)
        {
            return Error.Conflict(AnnouncementDomainErrors.CannotEditPublished);
        }

        await fileStorage.DeleteFileAsync(tenant.TenantId, AnnouncementAttachment.BucketName, attachment.StorageKey, cancellationToken);
        attachments.Remove(attachment);
        return Result.Success();
    }
}

/// <summary>Lists a duyuru's confirmed attachments for the manage panel/editor (any status).</summary>
[RequiresPermission("announcements.announcements.view")]
public sealed record ListAnnouncementAttachmentsQuery(Guid AnnouncementId) : IQuery<IReadOnlyList<AnnouncementAttachmentDto>>;

public sealed class ListAnnouncementAttachmentsHandler(IAnnouncementAttachmentRepository attachments, IFileStorage fileStorage, ITenantContext tenant)
    : IQueryHandler<ListAnnouncementAttachmentsQuery, IReadOnlyList<AnnouncementAttachmentDto>>
{
    public async Task<Result<IReadOnlyList<AnnouncementAttachmentDto>>> Handle(ListAnnouncementAttachmentsQuery query, CancellationToken cancellationToken) =>
        Result.Success(await AnnouncementAttachmentMapper.ToDtosAsync(attachments, fileStorage, tenant.TenantId, query.AnnouncementId, cancellationToken));
}

/// <summary>Lists a *published, not-expired* duyuru's confirmed attachments, for the employee feed (lower-privilege "my.feed" permission).</summary>
[RequiresPermission("announcements.my.feed")]
public sealed record ListMyAnnouncementAttachmentsQuery(Guid AnnouncementId) : IQuery<IReadOnlyList<AnnouncementAttachmentDto>>;

public sealed class ListMyAnnouncementAttachmentsHandler(
    IAnnouncementRepository announcements, IAnnouncementAttachmentRepository attachments, IFileStorage fileStorage, ITenantContext tenant)
    : IQueryHandler<ListMyAnnouncementAttachmentsQuery, IReadOnlyList<AnnouncementAttachmentDto>>
{
    public async Task<Result<IReadOnlyList<AnnouncementAttachmentDto>>> Handle(ListMyAnnouncementAttachmentsQuery query, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, query.AnnouncementId, cancellationToken);
        var visible = announcement is not null && announcement.Status == AnnouncementStatus.Published
            && (announcement.ExpiresAt is null || announcement.ExpiresAt > DateTime.UtcNow);
        if (!visible)
        {
            return Error.NotFound(AnnouncementDomainErrors.NotFound);
        }

        return Result.Success(await AnnouncementAttachmentMapper.ToDtosAsync(attachments, fileStorage, tenant.TenantId, query.AnnouncementId, cancellationToken));
    }
}

internal static class AnnouncementAttachmentMapper
{
    public static async Task<IReadOnlyList<AnnouncementAttachmentDto>> ToDtosAsync(
        IAnnouncementAttachmentRepository attachments, IFileStorage fileStorage, Guid tenantId, Guid announcementId, CancellationToken cancellationToken)
    {
        var list = (await attachments.ListByAnnouncementAsync(tenantId, announcementId, cancellationToken)).Where(a => a.IsConfirmed).ToList();
        var dtos = new List<AnnouncementAttachmentDto>(list.Count);
        foreach (var attachment in list)
        {
            var download = await fileStorage.GetDownloadUrlAsync(tenantId, AnnouncementAttachment.BucketName, attachment.StorageKey, expirationMinutes: 60, cancellationToken);
            dtos.Add(new AnnouncementAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Kind.ToString(), download.DownloadUrl));
        }

        return dtos;
    }
}

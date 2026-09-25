using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Notes;

public static class EmployeeNotePermissions
{
    public const string ViewKey = "employee.notes.view";
    public const string ManageKey = "employee.notes.manage";
}

public sealed record EmployeeNoteAttachmentDto(string FileName, string ContentType, long SizeBytes, string DownloadUrl);

public sealed record EmployeeNoteDto(
    Guid Id,
    Guid EmployeeId,
    string Title,
    string Content,
    EmployeeNoteImportance Importance,
    Guid AuthorUserId,
    string? AuthorName,
    DateTime CreatedAt,
    EmployeeNoteAttachmentDto? Attachment);

public sealed record InitiateNoteAttachmentUploadResponse(string UploadUrl, string RequestId, DateTime ExpiresAt);

/// <summary>Shared mapping and scope check for note handlers; the download URL is fresh per read, never stored.</summary>
public sealed class EmployeeNoteAccess(ITenantContext tenant, IFileStorage fileStorage, IDataScopeService dataScope, IIntegrationEventOutbox outbox)
{
    private const int DownloadUrlMinutes = 15;

    public Guid TenantId => tenant.TenantId;

    /// <summary>Out-of-scope notes are reported as not found (no existence leak), like IScopedRequest.</summary>
    public Task<bool> CanAccessAsync(string permission, Guid employeeId, CancellationToken cancellationToken) =>
        dataScope.CanAccessEmployeeAsync(permission, employeeId, cancellationToken);

    public async Task<EmployeeNoteDto> ToDtoAsync(EmployeeNote note, CancellationToken cancellationToken)
    {
        EmployeeNoteAttachmentDto? attachment = null;
        if (note.HasConfirmedAttachment)
        {
            var download = await fileStorage.GetDownloadUrlAsync(
                tenant.TenantId, EmployeeNote.BucketName, note.AttachmentStorageKey!, DownloadUrlMinutes, cancellationToken).ConfigureAwait(false);
            attachment = new EmployeeNoteAttachmentDto(
                note.AttachmentFileName!, note.AttachmentContentType!, note.AttachmentSizeBytes ?? 0, download.DownloadUrl);
        }

        return new EmployeeNoteDto(
            note.Id, note.EmployeeId, note.Title, note.Content, note.Importance, note.AuthorUserId, note.AuthorName, note.CreatedAt, attachment);
    }

    public async Task<Result<EmployeeNote>> LoadForManageAsync(IEmployeeNoteRepository notes, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await notes.GetTrackedAsync(noteId, cancellationToken).ConfigureAwait(false);
        if (note is null || !await CanAccessAsync(EmployeeNotePermissions.ManageKey, note.EmployeeId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound(EmployeeNoteErrors.NotFound);
        }

        return note;
    }

    /// <summary>Queues the stored object's deletion through the outbox: it runs only after the DB change committed (see <see cref="EmployeeNoteFileCleanupHandler"/>).</summary>
    public void QueueFileDeletion(IEnumerable<string?> storageKeys)
    {
        foreach (var key in storageKeys.Where(k => !string.IsNullOrEmpty(k)))
        {
            outbox.Enqueue(new EmployeeNoteFileOrphaned(tenant.TenantId, key!));
        }
    }
}

/// <summary>Deletes the orphaned attachment object after commit; a storage failure is logged and never rolls anything back.</summary>
public sealed class EmployeeNoteFileCleanupHandler(IFileStorage fileStorage, ILogger<EmployeeNoteFileCleanupHandler> logger)
    : IIntegrationEventHandler<EmployeeNoteFileOrphaned>
{
    public async Task Handle(EmployeeNoteFileOrphaned integrationEvent, CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteFileAsync(integrationEvent.TenantId, EmployeeNote.BucketName, integrationEvent.StorageKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Employee note attachment {StorageKey} (tenant {TenantId}) could not be deleted from storage", integrationEvent.StorageKey, integrationEvent.TenantId);
        }
    }
}

[RequiresPermission(EmployeeNotePermissions.ManageKey)]
public sealed record CreateEmployeeNoteCommand(Guid EmployeeId, string Title, string? Content, EmployeeNoteImportance Importance)
    : ICommand<EmployeeNoteDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class CreateEmployeeNoteHandler(
    IEmployeeReadStore employees, IEmployeeNoteRepository notes, EmployeeNoteAccess access, ICurrentUser user)
    : ICommandHandler<CreateEmployeeNoteCommand, EmployeeNoteDto>
{
    public async Task<Result<EmployeeNoteDto>> Handle(CreateEmployeeNoteCommand command, CancellationToken cancellationToken)
    {
        if (await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        if (user.UserId is not { } authorId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var created = EmployeeNote.Create(
            access.TenantId, command.EmployeeId, command.Title, command.Content, command.Importance, authorId, user.DisplayName);
        if (created.IsFailure)
        {
            return created.Error;
        }

        notes.Add(created.Value);
        return await access.ToDtoAsync(created.Value, cancellationToken).ConfigureAwait(false);
    }
}

[RequiresPermission(EmployeeNotePermissions.ManageKey)]
public sealed record UpdateEmployeeNoteCommand(Guid NoteId, string Title, string? Content, EmployeeNoteImportance Importance)
    : ICommand<EmployeeNoteDto>;

public sealed class UpdateEmployeeNoteHandler(IEmployeeNoteRepository notes, EmployeeNoteAccess access)
    : ICommandHandler<UpdateEmployeeNoteCommand, EmployeeNoteDto>
{
    public async Task<Result<EmployeeNoteDto>> Handle(UpdateEmployeeNoteCommand command, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadForManageAsync(notes, command.NoteId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var updated = loaded.Value.Update(command.Title, command.Content, command.Importance);
        return updated.IsFailure ? updated.Error : await access.ToDtoAsync(loaded.Value, cancellationToken).ConfigureAwait(false);
    }
}

[RequiresPermission(EmployeeNotePermissions.ManageKey)]
public sealed record DeleteEmployeeNoteCommand(Guid NoteId) : ICommand;

public sealed class DeleteEmployeeNoteHandler(IEmployeeNoteRepository notes, EmployeeNoteAccess access) : ICommandHandler<DeleteEmployeeNoteCommand>
{
    public async Task<Result> Handle(DeleteEmployeeNoteCommand command, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadForManageAsync(notes, command.NoteId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        access.QueueFileDeletion(loaded.Value.StorageKeys());
        notes.Remove(loaded.Value);
        return Result.Success();
    }
}

/// <summary>Filters by employee and/or importance; without an employee id only tenant-wide (Company scope) readers see anything.</summary>
[RequiresPermission(EmployeeNotePermissions.ViewKey)]
public sealed record ListEmployeeNotesQuery(Guid? EmployeeId, EmployeeNoteImportance? Importance, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<EmployeeNoteDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class ListEmployeeNotesHandler(IEmployeeNoteRepository notes, EmployeeNoteAccess access, IDataScopeService dataScope)
    : IQueryHandler<ListEmployeeNotesQuery, PagedResult<EmployeeNoteDto>>
{
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResult<EmployeeNoteDto>>> Handle(ListEmployeeNotesQuery query, CancellationToken cancellationToken)
    {
        if (query.EmployeeId is null
            && await dataScope.GetVisibleEmployeeIdsAsync(EmployeeNotePermissions.ViewKey, cancellationToken).ConfigureAwait(false) is not null)
        {
            // A limited (team/department) scope cannot be applied to a tenant-wide list: ask for one employee's notes instead.
            return Error.Forbidden(ErrorCodes.Forbidden);
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var (items, total) = await notes.ListAsync(query.EmployeeId, query.Importance, (page - 1) * pageSize, pageSize, cancellationToken).ConfigureAwait(false);
        var dtos = new List<EmployeeNoteDto>(items.Count);
        foreach (var note in items)
        {
            dtos.Add(await access.ToDtoAsync(note, cancellationToken).ConfigureAwait(false));
        }

        return new PagedResult<EmployeeNoteDto>(dtos, page, pageSize, total);
    }
}

[RequiresPermission(EmployeeNotePermissions.ManageKey)]
public sealed record InitiateNoteAttachmentUploadCommand(Guid NoteId, string FileName, string ContentType, long FileSizeBytes)
    : ICommand<InitiateNoteAttachmentUploadResponse>;

public sealed class InitiateNoteAttachmentUploadHandler(IEmployeeNoteRepository notes, EmployeeNoteAccess access, IFileStorage fileStorage)
    : ICommandHandler<InitiateNoteAttachmentUploadCommand, InitiateNoteAttachmentUploadResponse>
{
    public async Task<Result<InitiateNoteAttachmentUploadResponse>> Handle(InitiateNoteAttachmentUploadCommand command, CancellationToken cancellationToken)
    {
        var safeName = EmployeeNoteUploadPolicy.SanitizeFileName(command.FileName);
        if (safeName.Length == 0 || string.IsNullOrWhiteSpace(command.ContentType))
        {
            return Error.Validation(EmployeeNoteErrors.AttachmentInvalid);
        }

        if (!EmployeeNoteUploadPolicy.IsAllowed(safeName, command.ContentType))
        {
            return Error.Validation(EmployeeNoteErrors.AttachmentTypeNotAllowed);
        }

        if (command.FileSizeBytes <= 0 || command.FileSizeBytes > EmployeeNoteLimits.AttachmentMaxBytes)
        {
            return Error.Rule(EmployeeNoteErrors.AttachmentTooLarge);
        }

        var loaded = await access.LoadForManageAsync(notes, command.NoteId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var note = loaded.Value;
        var key = $"{note.EmployeeId:N}/{note.Id:N}/{Guid.NewGuid():N}/{safeName}";
        var begun = note.BeginAttachment(safeName, key, command.ContentType);
        if (begun.IsFailure)
        {
            return begun.Error;
        }

        // The confirmed attachment stays untouched; only a superseded, never-confirmed upload is orphaned.
        access.QueueFileDeletion([begun.Value.Key]);

        var presigned = await fileStorage.InitiateUploadAsync(access.TenantId, EmployeeNote.BucketName, key, command.ContentType, cancellationToken).ConfigureAwait(false);
        return new InitiateNoteAttachmentUploadResponse(presigned.UploadUrl, presigned.RequestId, presigned.ExpiresAt);
    }
}

[RequiresPermission(EmployeeNotePermissions.ManageKey)]
public sealed record ConfirmNoteAttachmentUploadCommand(Guid NoteId, string ETag) : ICommand<EmployeeNoteDto>;

public sealed class ConfirmNoteAttachmentUploadHandler(
    IEmployeeNoteRepository notes, EmployeeNoteAccess access, IFileStorage fileStorage, ILogger<ConfirmNoteAttachmentUploadHandler> logger)
    : ICommandHandler<ConfirmNoteAttachmentUploadCommand, EmployeeNoteDto>
{
    public async Task<Result<EmployeeNoteDto>> Handle(ConfirmNoteAttachmentUploadCommand command, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadForManageAsync(notes, command.NoteId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var note = loaded.Value;
        var pendingKey = note.PendingAttachmentStorageKey;
        if (pendingKey is null || string.IsNullOrWhiteSpace(command.ETag))
        {
            return Error.NotFound(EmployeeNoteErrors.AttachmentNotFound);
        }

        var actualSize = await fileStorage.ConfirmUploadAsync(access.TenantId, EmployeeNote.BucketName, pendingKey, command.ETag, cancellationToken).ConfigureAwait(false);
        var confirmed = note.ConfirmAttachment(actualSize);
        if (confirmed.IsFailure)
        {
            if (actualSize > EmployeeNoteLimits.AttachmentMaxBytes)
            {
                // The declared size was a lie: this object was never referenced by the DB, so remove it right away.
                await DeleteOversizedAsync(access.TenantId, pendingKey, cancellationToken).ConfigureAwait(false);
            }

            return confirmed.Error;
        }

        access.QueueFileDeletion([confirmed.Value.Key]);
        return await access.ToDtoAsync(note, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteOversizedAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteFileAsync(tenantId, EmployeeNote.BucketName, key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Oversized employee note attachment {StorageKey} (tenant {TenantId}) could not be deleted from storage", key, tenantId);
        }
    }
}

[RequiresPermission(EmployeeNotePermissions.ManageKey)]
public sealed record DeleteNoteAttachmentCommand(Guid NoteId) : ICommand;

public sealed class DeleteNoteAttachmentHandler(IEmployeeNoteRepository notes, EmployeeNoteAccess access) : ICommandHandler<DeleteNoteAttachmentCommand>
{
    public async Task<Result> Handle(DeleteNoteAttachmentCommand command, CancellationToken cancellationToken)
    {
        var loaded = await access.LoadForManageAsync(notes, command.NoteId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        access.QueueFileDeletion(loaded.Value.ClearAttachment());
        return Result.Success();
    }
}

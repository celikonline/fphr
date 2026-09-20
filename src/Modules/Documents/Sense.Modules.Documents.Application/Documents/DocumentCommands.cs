using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.Documents;

/// <summary>Initiates a file upload by generating a presigned URL.</summary>
[RequiresPermission("documents.files.create")]
public sealed record InitiateUploadCommand(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string? BucketName = "documents") : ICommand<InitiateUploadResponse>;

public sealed class InitiateUploadValidator : AbstractValidator<InitiateUploadCommand>
{
    public InitiateUploadValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(DocumentsLimits.TitleMaxLength);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(DocumentsLimits.ContentTypeMaxLength);
        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(DocumentsLimits.MaxFileSize);
        RuleFor(x => x.BucketName).MaximumLength(50).When(x => !string.IsNullOrEmpty(x.BucketName));
        RuleFor(x => x.BucketName).Matches("^[a-z0-9][a-z0-9-]*$").When(x => !string.IsNullOrEmpty(x.BucketName));
        UploadFilePolicy.ApplyRules(this, x => x.FileName, x => x.ContentType);
    }
}

public sealed class InitiateUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IDocumentRepository documents,
    ILogger<InitiateUploadHandler> logger) : ICommandHandler<InitiateUploadCommand, InitiateUploadResponse>
{
    public Task<Result<InitiateUploadResponse>> Handle(InitiateUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.InitiateAsync(logger, fileStorage, tenant, currentUser, documents, command.FileName, command.ContentType, command.FileSizeBytes, command.BucketName, cancellationToken);
}

/// <summary>Self-service: initiates a file upload for the caller's own use (e.g. to satisfy their own document requirement).</summary>
[RequiresPermission("documents.my.files")]
public sealed record InitiateMyUploadCommand(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string? BucketName = "documents") : ICommand<InitiateUploadResponse>;

public sealed class InitiateMyUploadValidator : AbstractValidator<InitiateMyUploadCommand>
{
    public InitiateMyUploadValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(DocumentsLimits.TitleMaxLength);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(DocumentsLimits.ContentTypeMaxLength);
        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(DocumentsLimits.MaxFileSize);
        RuleFor(x => x.BucketName).MaximumLength(50).When(x => !string.IsNullOrEmpty(x.BucketName));
        RuleFor(x => x.BucketName).Matches("^[a-z0-9][a-z0-9-]*$").When(x => !string.IsNullOrEmpty(x.BucketName));
        UploadFilePolicy.ApplyRules(this, x => x.FileName, x => x.ContentType);
    }
}

public sealed class InitiateMyUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IDocumentRepository documents,
    ILogger<InitiateMyUploadHandler> logger) : ICommandHandler<InitiateMyUploadCommand, InitiateUploadResponse>
{
    public Task<Result<InitiateUploadResponse>> Handle(InitiateMyUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.InitiateAsync(logger, fileStorage, tenant, currentUser, documents, command.FileName, command.ContentType, command.FileSizeBytes, command.BucketName, cancellationToken);
}

/// <summary>Confirms an upload and validates the ETag (content hash). <paramref name="ExpiryDate"/> is optional
/// (e.g. driver's license, residence permit, health report); a past date is accepted.</summary>
[RequiresPermission("documents.files.create")]
public sealed record ConfirmUploadCommand(
    Guid DocumentId,
    string ETag,
    long ActualFileSizeBytes,
    DateOnly? ExpiryDate = null) : ICommand<ConfirmUploadResponse>;

public sealed class ConfirmUploadValidator : AbstractValidator<ConfirmUploadCommand>
{
    public ConfirmUploadValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.ETag).NotEmpty();
        RuleFor(x => x.ActualFileSizeBytes).GreaterThan(0);
    }
}

public sealed class ConfirmUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    IDocumentRepository documents,
    ILogger<ConfirmUploadHandler> logger) : ICommandHandler<ConfirmUploadCommand, ConfirmUploadResponse>
{
    public Task<Result<ConfirmUploadResponse>> Handle(ConfirmUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.ConfirmAsync(logger, fileStorage, tenant, documents, command.DocumentId, command.ETag, command.ActualFileSizeBytes, command.ExpiryDate, cancellationToken);
}

/// <summary>Self-service: confirms an upload initiated via <see cref="InitiateMyUploadCommand"/>.</summary>
[RequiresPermission("documents.my.files")]
public sealed record ConfirmMyUploadCommand(
    Guid DocumentId,
    string ETag,
    long ActualFileSizeBytes,
    DateOnly? ExpiryDate = null) : ICommand<ConfirmUploadResponse>;

public sealed class ConfirmMyUploadValidator : AbstractValidator<ConfirmMyUploadCommand>
{
    public ConfirmMyUploadValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.ETag).NotEmpty();
        RuleFor(x => x.ActualFileSizeBytes).GreaterThan(0);
    }
}

public sealed class ConfirmMyUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    IDocumentRepository documents,
    ILogger<ConfirmMyUploadHandler> logger) : ICommandHandler<ConfirmMyUploadCommand, ConfirmUploadResponse>
{
    public Task<Result<ConfirmUploadResponse>> Handle(ConfirmMyUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.ConfirmAsync(logger, fileStorage, tenant, documents, command.DocumentId, command.ETag, command.ActualFileSizeBytes, command.ExpiryDate, cancellationToken);
}

/// <summary>Shared by the HR and self-service upload commands; the raw upload isn't employee-scoped (see the self-service commands' remark).</summary>
internal static class UploadCore
{
    public static async Task<Result<InitiateUploadResponse>> InitiateAsync(
        ILogger logger,
        IFileStorage fileStorage,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IDocumentRepository documents,
        string fileName,
        string contentType,
        long fileSizeBytes,
        string? bucketName,
        CancellationToken cancellationToken)
    {
        // Defence in depth behind the validators: nothing user-typed (separators, "..", control chars) reaches the
        // storage key or the stored title, and a disallowed type never gets a presigned URL.
        var safeName = UploadFilePolicy.SanitizeFileName(fileName);
        if (safeName.Length == 0)
        {
            return Error.Validation(DocumentsErrors.FileNameInvalid);
        }

        if (!UploadFilePolicy.IsAllowed(safeName, contentType))
        {
            return Error.Validation(DocumentsErrors.FileTypeNotAllowed);
        }

        try
        {
            var fileKey = $"{Guid.CreateVersion7().ToString("N")}/{safeName}";
            var resolvedBucket = bucketName ?? "documents";

            // Initiate upload in storage
            var presignedRequest = await fileStorage.InitiateUploadAsync(
                tenant.TenantId,
                resolvedBucket,
                fileKey,
                contentType,
                cancellationToken);

            // Create document aggregate in Pending state
            var doc = Document.Create(
                tenant.TenantId,
                safeName,
                fileKey,
                contentType,
                fileSizeBytes,
                currentUser.UserId ?? Guid.Empty,
                resolvedBucket);

            if (doc.IsFailure)
            {
                return doc.Error;
            }

            documents.Add(doc.Value);

            return new InitiateUploadResponse(
                doc.Value.Id,
                presignedRequest.UploadUrl,
                presignedRequest.RequestId,
                presignedRequest.ExpiresAt);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to initiate upload for file {FileName} in bucket {BucketName}.", fileName, bucketName ?? "documents");
            return Error.Failure(DocumentsErrors.UploadInitiationFailed);
        }
    }

    public static async Task<Result<ConfirmUploadResponse>> ConfirmAsync(
        ILogger logger,
        IFileStorage fileStorage,
        ITenantContext tenant,
        IDocumentRepository documents,
        Guid documentId,
        string eTag,
        long actualFileSizeBytes,
        DateOnly? expiryDate,
        CancellationToken cancellationToken)
    {
        var doc = await documents.GetByIdAsync(documentId, cancellationToken);
        if (doc.IsFailure)
        {
            return doc.Error;
        }

        // A second confirm (or confirming a deleted document) is a client error, not a server failure.
        if (doc.Value.Status != DocumentStatus.Pending)
        {
            return Error.Conflict(DocumentsErrors.UploadNotPending);
        }

        try
        {
            // Confirm upload with storage (validates the object exists and the ETag matches) and read the
            // authoritative size; the client-reported actualFileSizeBytes is deliberately ignored.
            var storedSize = await fileStorage.ConfirmUploadAsync(
                tenant.TenantId,
                doc.Value.BucketName,
                doc.Value.FileKey,
                eTag,
                cancellationToken);

            if (storedSize <= 0 || storedSize > DocumentsLimits.MaxFileSize)
            {
                return Error.Rule(DocumentsErrors.UploadedFileInvalid);
            }

            // Update document state
            doc.Value.ConfirmUpload(eTag, storedSize);
            if (expiryDate is not null)
            {
                doc.Value.SetExpiryDate(expiryDate);
            }

            return new ConfirmUploadResponse(
                doc.Value.Id,
                doc.Value.Title,
                doc.Value.UploadedAt,
                doc.Value.ExpiryDate);
        }
        catch (Exception ex) when (ex is StorageObjectNotFoundException or StorageETagMismatchException)
        {
            logger.LogWarning(ex, "Upload confirmation rejected for document {DocumentId}.", documentId);
            return Error.Rule(DocumentsErrors.UploadedFileInvalid);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to confirm upload for document {DocumentId}.", documentId);
            return Error.Failure(DocumentsErrors.ConfirmationFailed);
        }
    }
}

/// <summary>Deletes a document and removes file from storage.</summary>
[RequiresPermission("documents.files.delete")]
public sealed record DeleteDocumentCommand(Guid DocumentId) : ICommand;

public sealed class DeleteDocumentValidator : AbstractValidator<DeleteDocumentCommand>
{
    public DeleteDocumentValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public sealed class DeleteDocumentHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IDocumentRepository documents,
    ILogger<DeleteDocumentHandler> logger) : ICommandHandler<DeleteDocumentCommand>
{
    public async Task<Result> Handle(DeleteDocumentCommand command, CancellationToken cancellationToken)
    {
        var doc = await documents.GetByIdAsync(command.DocumentId, cancellationToken);
        if (doc.IsFailure)
        {
            return doc.Error;
        }

        try
        {
            // Delete from storage
            await fileStorage.DeleteFileAsync(
                tenant.TenantId,
                doc.Value.BucketName,
                doc.Value.FileKey,
                cancellationToken);

            // Mark as deleted in domain
            doc.Value.Delete(currentUser.UserId ?? Guid.Empty);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to delete document {DocumentId}.", command.DocumentId);
            return Error.Failure(DocumentsErrors.DeletionFailed);
        }
    }
}

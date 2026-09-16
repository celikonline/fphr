using FluentValidation;
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
    }
}

public sealed class InitiateUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IDocumentRepository documents) : ICommandHandler<InitiateUploadCommand, InitiateUploadResponse>
{
    public Task<Result<InitiateUploadResponse>> Handle(InitiateUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.InitiateAsync(fileStorage, tenant, currentUser, documents, command.FileName, command.ContentType, command.FileSizeBytes, command.BucketName, cancellationToken);
}

/// <summary>Self-service: initiates a file upload for the caller's own use (e.g. to satisfy their own document requirement).</summary>
[RequiresPermission("documents.my.files.create")]
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
    }
}

public sealed class InitiateMyUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IDocumentRepository documents) : ICommandHandler<InitiateMyUploadCommand, InitiateUploadResponse>
{
    public Task<Result<InitiateUploadResponse>> Handle(InitiateMyUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.InitiateAsync(fileStorage, tenant, currentUser, documents, command.FileName, command.ContentType, command.FileSizeBytes, command.BucketName, cancellationToken);
}

/// <summary>Confirms an upload and validates the ETag (content hash).</summary>
[RequiresPermission("documents.files.create")]
public sealed record ConfirmUploadCommand(
    Guid DocumentId,
    string ETag,
    long ActualFileSizeBytes) : ICommand<ConfirmUploadResponse>;

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
    IDocumentRepository documents) : ICommandHandler<ConfirmUploadCommand, ConfirmUploadResponse>
{
    public Task<Result<ConfirmUploadResponse>> Handle(ConfirmUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.ConfirmAsync(fileStorage, tenant, documents, command.DocumentId, command.ETag, command.ActualFileSizeBytes, cancellationToken);
}

/// <summary>Self-service: confirms an upload initiated via <see cref="InitiateMyUploadCommand"/>.</summary>
[RequiresPermission("documents.my.files.create")]
public sealed record ConfirmMyUploadCommand(
    Guid DocumentId,
    string ETag,
    long ActualFileSizeBytes) : ICommand<ConfirmUploadResponse>;

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
    IDocumentRepository documents) : ICommandHandler<ConfirmMyUploadCommand, ConfirmUploadResponse>
{
    public Task<Result<ConfirmUploadResponse>> Handle(ConfirmMyUploadCommand command, CancellationToken cancellationToken) =>
        UploadCore.ConfirmAsync(fileStorage, tenant, documents, command.DocumentId, command.ETag, command.ActualFileSizeBytes, cancellationToken);
}

/// <summary>Shared by the HR and self-service upload commands; the raw upload isn't employee-scoped (see documents.my.files.create's remark).</summary>
internal static class UploadCore
{
    public static async Task<Result<InitiateUploadResponse>> InitiateAsync(
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
        try
        {
            var fileKey = $"{Guid.CreateVersion7().ToString("N")}/{fileName}";
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
                fileName,
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
        catch (InvalidOperationException)
        {
            return Error.Failure(DocumentsErrors.UploadInitiationFailed);
        }
    }

    public static async Task<Result<ConfirmUploadResponse>> ConfirmAsync(
        IFileStorage fileStorage,
        ITenantContext tenant,
        IDocumentRepository documents,
        Guid documentId,
        string eTag,
        long actualFileSizeBytes,
        CancellationToken cancellationToken)
    {
        var doc = await documents.GetByIdAsync(documentId, cancellationToken);
        if (doc.IsFailure)
        {
            return doc.Error;
        }

        try
        {
            // Confirm upload with storage (validates ETag)
            await fileStorage.ConfirmUploadAsync(
                tenant.TenantId,
                doc.Value.BucketName,
                doc.Value.FileKey,
                eTag,
                cancellationToken);

            // Update document state
            doc.Value.ConfirmUpload(eTag, actualFileSizeBytes);

            return new ConfirmUploadResponse(
                doc.Value.Id,
                doc.Value.Title,
                doc.Value.UploadedAt);
        }
        catch (InvalidOperationException)
        {
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
    IDocumentRepository documents) : ICommandHandler<DeleteDocumentCommand>
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
        catch (InvalidOperationException)
        {
            return Error.Failure(DocumentsErrors.DeletionFailed);
        }
    }
}

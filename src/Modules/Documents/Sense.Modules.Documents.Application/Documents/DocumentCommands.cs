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
        RuleFor(x => x.BucketName).Empty().Or().MaximumLength(50);
    }
}

public sealed class InitiateUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IDocumentRepository documents) : ICommandHandler<InitiateUploadCommand, InitiateUploadResponse>
{
    public async Task<Result<InitiateUploadResponse>> Handle(InitiateUploadCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var fileKey = $"{Guid.CreateVersion7().ToString("N")}/{command.FileName}";
            var bucketName = command.BucketName ?? "documents";

            // Initiate upload in storage
            var presignedRequest = await fileStorage.InitiateUploadAsync(
                tenant.TenantId,
                bucketName,
                fileKey,
                command.ContentType,
                cancellationToken);

            // Create document aggregate in Pending state
            var doc = Document.Create(
                tenant.TenantId,
                command.FileName,
                fileKey,
                command.ContentType,
                command.FileSizeBytes,
                currentUser.UserId ?? Guid.Empty,
                bucketName);

            if (doc.IsFailure)
            {
                return doc.Error;
            }

            documents.Add(doc.Value);

            return new InitiateUploadResponse(
                presignedRequest.UploadUrl,
                presignedRequest.RequestId,
                presignedRequest.ExpiresAt);
        }
        catch (InvalidOperationException ex)
        {
            return Error.Failure(DocumentsErrors.UploadInitiationFailed);
        }
    }
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
    public async Task<Result<ConfirmUploadResponse>> Handle(ConfirmUploadCommand command, CancellationToken cancellationToken)
    {
        var doc = await documents.GetByIdAsync(command.DocumentId, cancellationToken);
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
                command.ETag,
                cancellationToken);

            // Update document state
            doc.Value.ConfirmUpload(command.ETag, command.ActualFileSizeBytes);

            return new ConfirmUploadResponse(
                doc.Value.Id,
                doc.Value.Title,
                doc.Value.UploadedAt);
        }
        catch (InvalidOperationException ex)
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
        catch (InvalidOperationException ex)
        {
            return Error.Failure(DocumentsErrors.DeletionFailed);
        }
    }
}

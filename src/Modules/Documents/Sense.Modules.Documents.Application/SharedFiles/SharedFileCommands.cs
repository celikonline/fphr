using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.SharedFiles;

/// <summary>Initiates a shared company file upload: generates a presigned URL, after checking the tenant's storage
/// quota against the declared file size (senseik-028 acceptance: quota + usage indicator).</summary>
[RequiresPermission("documents.company.manage")]
public sealed record InitiateSharedFileUploadCommand(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    SharedFileVisibility Visibility = SharedFileVisibility.Everyone,
    IReadOnlyList<string>? AllowedRoles = null) : ICommand<InitiateSharedFileUploadResponse>;

public sealed class InitiateSharedFileUploadValidator : AbstractValidator<InitiateSharedFileUploadCommand>
{
    public InitiateSharedFileUploadValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(SharedFilesLimits.FileNameMaxLength);
        RuleFor(x => x.ContentType).NotEmpty();
        UploadFilePolicy.ApplyRules(this, x => x.FileName, x => x.ContentType);
        RuleFor(x => x.FileSizeBytes).GreaterThan(0).LessThanOrEqualTo(SharedFilesLimits.MaxFileSize);
        RuleFor(x => x.AllowedRoles)
            .Must(roles => roles is { Count: > 0 })
            .When(x => x.Visibility == SharedFileVisibility.RestrictedToRoles)
            .WithMessage("Role-restricted visibility requires at least one allowed role.");
    }
}

public sealed class InitiateSharedFileUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ISharedCompanyFileRepository files,
    ILogger<InitiateSharedFileUploadHandler> logger) : ICommandHandler<InitiateSharedFileUploadCommand, InitiateSharedFileUploadResponse>
{
    public async Task<Result<InitiateSharedFileUploadResponse>> Handle(InitiateSharedFileUploadCommand command, CancellationToken cancellationToken)
    {
        var usedBytes = await files.SumActiveSizeByTenantAsync(cancellationToken);
        var quota = SharedFileQuota.Calculate(usedBytes, SharedFilesLimits.DefaultQuotaBytes);
        if (quota.WouldExceed(command.FileSizeBytes))
        {
            return Error.Validation(SharedFilesDomainErrors.QuotaExceeded, ("usedBytes", usedBytes), ("quotaBytes", SharedFilesLimits.DefaultQuotaBytes));
        }

        try
        {
            var safeName = UploadFilePolicy.SanitizeFileName(command.FileName);
            if (safeName.Length == 0)
            {
                return Error.Validation(DocumentsErrors.FileNameInvalid);
            }

            if (!UploadFilePolicy.IsAllowed(safeName, command.ContentType))
            {
                return Error.Validation(DocumentsErrors.FileTypeNotAllowed);
            }

            var fileKey = $"{Guid.CreateVersion7():N}/{safeName}";

            var presignedRequest = await fileStorage.InitiateUploadAsync(
                tenant.TenantId,
                SharedCompanyFile.BucketName,
                fileKey,
                command.ContentType,
                cancellationToken);

            var file = SharedCompanyFile.Create(
                tenant.TenantId,
                safeName,
                fileKey,
                command.ContentType,
                command.FileSizeBytes,
                currentUser.UserId ?? Guid.Empty,
                command.Visibility,
                command.AllowedRoles);

            if (file.IsFailure)
            {
                return file.Error;
            }

            files.Add(file.Value);

            return new InitiateSharedFileUploadResponse(
                file.Value.Id,
                presignedRequest.UploadUrl,
                presignedRequest.RequestId,
                presignedRequest.ExpiresAt);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to initiate shared file upload for {FileName}.", command.FileName);
            return Error.Failure(SharedFilesErrors.UploadInitiationFailed);
        }
    }
}

/// <summary>Confirms a shared company file upload and validates the ETag (content hash).</summary>
[RequiresPermission("documents.company.manage")]
public sealed record ConfirmSharedFileUploadCommand(
    Guid FileId,
    string ETag,
    long ActualFileSizeBytes) : ICommand<ConfirmSharedFileUploadResponse>;

public sealed class ConfirmSharedFileUploadValidator : AbstractValidator<ConfirmSharedFileUploadCommand>
{
    public ConfirmSharedFileUploadValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.ETag).NotEmpty();
        RuleFor(x => x.ActualFileSizeBytes).GreaterThan(0);
    }
}

public sealed class ConfirmSharedFileUploadHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ISharedCompanyFileRepository files,
    ILogger<ConfirmSharedFileUploadHandler> logger) : ICommandHandler<ConfirmSharedFileUploadCommand, ConfirmSharedFileUploadResponse>
{
    public async Task<Result<ConfirmSharedFileUploadResponse>> Handle(ConfirmSharedFileUploadCommand command, CancellationToken cancellationToken)
    {
        var file = await files.GetByIdAsync(command.FileId, cancellationToken);
        if (file.IsFailure)
        {
            return file.Error;
        }

        if (file.Value.Status != SharedFileStatus.Pending)
        {
            return Error.Conflict(DocumentsErrors.UploadNotPending);
        }

        try
        {
            // Authoritative size from storage; the client-reported ActualFileSizeBytes is ignored (it would let a
            // caller under-report and bypass the tenant quota).
            var storedSize = await fileStorage.ConfirmUploadAsync(
                tenant.TenantId,
                SharedCompanyFile.BucketName,
                file.Value.FileKey,
                command.ETag,
                cancellationToken);

            if (storedSize <= 0 || storedSize > SharedFilesLimits.MaxFileSize)
            {
                return Error.Rule(DocumentsErrors.UploadedFileInvalid);
            }

            file.Value.ConfirmUpload(command.ETag, storedSize);

            return new ConfirmSharedFileUploadResponse(
                file.Value.Id,
                file.Value.FileName,
                file.Value.Size,
                file.Value.UploadedAt);
        }
        catch (Exception ex) when (ex is StorageObjectNotFoundException or StorageETagMismatchException)
        {
            logger.LogWarning(ex, "Shared file upload confirmation rejected for {FileId}.", command.FileId);
            return Error.Rule(DocumentsErrors.UploadedFileInvalid);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to confirm shared file upload for {FileId}.", command.FileId);
            return Error.Failure(SharedFilesErrors.ConfirmationFailed);
        }
    }
}

/// <summary>Deletes a shared company file (soft-delete) and removes the object from storage.</summary>
[RequiresPermission("documents.company.manage")]
public sealed record DeleteSharedFileCommand(Guid FileId) : ICommand;

public sealed class DeleteSharedFileValidator : AbstractValidator<DeleteSharedFileCommand>
{
    public DeleteSharedFileValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
    }
}

public sealed class DeleteSharedFileHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ISharedCompanyFileRepository files,
    ILogger<DeleteSharedFileHandler> logger) : ICommandHandler<DeleteSharedFileCommand>
{
    public async Task<Result> Handle(DeleteSharedFileCommand command, CancellationToken cancellationToken)
    {
        var file = await files.GetByIdAsync(command.FileId, cancellationToken);
        if (file.IsFailure)
        {
            return file.Error;
        }

        try
        {
            await fileStorage.DeleteFileAsync(
                tenant.TenantId,
                SharedCompanyFile.BucketName,
                file.Value.FileKey,
                cancellationToken);

            file.Value.Delete(currentUser.UserId ?? Guid.Empty);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to delete shared file {FileId}.", command.FileId);
            return Error.Failure(SharedFilesErrors.DeletionFailed);
        }
    }
}

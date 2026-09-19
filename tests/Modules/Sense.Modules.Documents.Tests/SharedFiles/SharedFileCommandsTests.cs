using Microsoft.Extensions.Logging;
using NSubstitute;
using Sense.Modules.Documents.Application.SharedFiles;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests.SharedFiles;

/// <summary>
/// Application-layer handler tests for shared company files, mocking the repository/storage/context ports directly
/// with NSubstitute (mirrors DocumentTypeHandlerTests' approach - this module has no Testcontainers-backed handler
/// test project to plug into instead, see that file's remark and the senseik-028 handoff doc).
/// </summary>
public sealed class SharedFileCommandsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static ICurrentUser CurrentUser(Guid? userId = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId ?? UserId);
        return user;
    }

    private static IFileStorage FileStorageStub()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.InitiateUploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PresignedUrlRequest("https://storage/upload", "req-1", DateTime.UtcNow.AddMinutes(60), new Dictionary<string, string>())));
        // Storage is the authoritative source of the confirmed size (client-reported size is ignored).
        storage.ConfirmUploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(4096L));
        return storage;
    }

    [Fact]
    public async Task Initiate_UnderQuota_Succeeds_AndAddsFileToRepository()
    {
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.SumActiveSizeByTenantAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0L));
        var handler = new InitiateSharedFileUploadHandler(
            FileStorageStub(), Tenant(), CurrentUser(), files, Substitute.For<ILogger<InitiateSharedFileUploadHandler>>());

        var result = await handler.Handle(
            new InitiateSharedFileUploadCommand("Handbook.pdf", "application/pdf", 1024 * 1024),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PresignedUrl.ShouldBe("https://storage/upload");
        files.Received(1).Add(Arg.Is<SharedCompanyFile>(f => f.FileName == "Handbook.pdf"));
    }

    [Fact]
    public async Task Initiate_WouldExceedQuota_FailsWithQuotaExceeded_AndDoesNotAddOrCallStorage()
    {
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.SumActiveSizeByTenantAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(SharedFilesLimits.DefaultQuotaBytes - 100));
        var storage = FileStorageStub();
        var handler = new InitiateSharedFileUploadHandler(
            storage, Tenant(), CurrentUser(), files, Substitute.For<ILogger<InitiateSharedFileUploadHandler>>());

        var result = await handler.Handle(
            new InitiateSharedFileUploadCommand("BigFile.pdf", "application/pdf", 1000),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SharedFilesDomainErrors.QuotaExceeded);
        files.DidNotReceive().Add(Arg.Any<SharedCompanyFile>());
        await storage.DidNotReceive().InitiateUploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initiate_ExactlyAtRemainingQuota_Succeeds()
    {
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.SumActiveSizeByTenantAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(SharedFilesLimits.DefaultQuotaBytes - 1000));
        var handler = new InitiateSharedFileUploadHandler(
            FileStorageStub(), Tenant(), CurrentUser(), files, Substitute.For<ILogger<InitiateSharedFileUploadHandler>>());

        var result = await handler.Handle(
            new InitiateSharedFileUploadCommand("ExactFit.pdf", "application/pdf", 1000),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Initiate_RestrictedVisibilityWithRoles_PassesRolesThroughToCreatedFile()
    {
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.SumActiveSizeByTenantAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0L));
        var handler = new InitiateSharedFileUploadHandler(
            FileStorageStub(), Tenant(), CurrentUser(), files, Substitute.For<ILogger<InitiateSharedFileUploadHandler>>());

        var result = await handler.Handle(
            new InitiateSharedFileUploadCommand("Payroll.xlsx", "application/vnd.ms-excel", 2048, SharedFileVisibility.RestrictedToRoles, ["HR", "Admin"]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        files.Received(1).Add(Arg.Is<SharedCompanyFile>(f =>
            f.Visibility == SharedFileVisibility.RestrictedToRoles && f.AllowedRoles.Contains("HR") && f.AllowedRoles.Contains("Admin")));
    }

    [Fact]
    public async Task Confirm_ExistingPendingFile_TransitionsToActive()
    {
        var file = SharedCompanyFile.Create(TenantId, "Handbook.pdf", "key/handbook.pdf", "application/pdf", 1024, UserId).Value;
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.GetByIdAsync(file.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(file)));
        var handler = new ConfirmSharedFileUploadHandler(
            FileStorageStub(), Tenant(), files, Substitute.For<ILogger<ConfirmSharedFileUploadHandler>>());

        var result = await handler.Handle(new ConfirmSharedFileUploadCommand(file.Id, "etag-1", 4096), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        file.Status.ShouldBe(SharedFileStatus.Active);
        file.Size.ShouldBe(4096);
    }

    [Fact]
    public async Task Confirm_UnknownFile_ReturnsNotFound()
    {
        var missingId = Guid.NewGuid();
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<SharedCompanyFile>(Error.NotFound(SharedFilesDomainErrors.NotFound))));
        var handler = new ConfirmSharedFileUploadHandler(
            FileStorageStub(), Tenant(), files, Substitute.For<ILogger<ConfirmSharedFileUploadHandler>>());

        var result = await handler.Handle(new ConfirmSharedFileUploadCommand(missingId, "etag-1", 4096), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SharedFilesDomainErrors.NotFound);
    }

    [Fact]
    public async Task Delete_ExistingFile_MarksDeleted_AndCallsStorageDelete()
    {
        var file = SharedCompanyFile.Create(TenantId, "Handbook.pdf", "key/handbook.pdf", "application/pdf", 1024, UserId).Value;
        file.ConfirmUpload("etag-1", 1024);
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.GetByIdAsync(file.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(file)));
        var storage = FileStorageStub();
        var handler = new DeleteSharedFileHandler(storage, Tenant(), CurrentUser(), files, Substitute.For<ILogger<DeleteSharedFileHandler>>());

        var result = await handler.Handle(new DeleteSharedFileCommand(file.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        file.Status.ShouldBe(SharedFileStatus.Deleted);
        await storage.Received(1).DeleteFileAsync(TenantId, SharedCompanyFile.BucketName, file.FileKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_UnknownFile_ReturnsNotFound_AndDoesNotCallStorage()
    {
        var missingId = Guid.NewGuid();
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<SharedCompanyFile>(Error.NotFound(SharedFilesDomainErrors.NotFound))));
        var storage = FileStorageStub();
        var handler = new DeleteSharedFileHandler(storage, Tenant(), CurrentUser(), files, Substitute.For<ILogger<DeleteSharedFileHandler>>());

        var result = await handler.Handle(new DeleteSharedFileCommand(missingId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await storage.DidNotReceive().DeleteFileAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

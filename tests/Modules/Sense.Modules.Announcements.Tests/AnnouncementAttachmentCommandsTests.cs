using NSubstitute;
using Sense.Modules.Announcements.Application.Attachments;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Attachments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Storage;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class AnnouncementAttachmentCommandsTests
{
    [Fact]
    public async Task InitiateUpload_WhenAnnouncementIsPublished_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var announcement = Announcement.Create(tenantId, "Title", "Content", AnnouncementType.General, AnnouncementPriority.Normal).Value;
        announcement.Publish(Guid.NewGuid());

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var announcements = Substitute.For<IAnnouncementRepository>();
        announcements.GetByIdAsync(tenantId, announcement.Id, Arg.Any<CancellationToken>()).Returns(announcement);

        var handler = new InitiateAnnouncementAttachmentUploadHandler(
            announcements, Substitute.For<IAnnouncementAttachmentRepository>(), Substitute.For<IFileStorage>(), tenant, Substitute.For<ICurrentUser>());

        var result = await handler.Handle(
            new InitiateAnnouncementAttachmentUploadCommand(announcement.Id, "logo.png", "image/png", 1024, AnnouncementAttachmentKind.Inline), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementDomainErrors.CannotEditPublished, result.Error.Code);
    }

    [Fact]
    public async Task InitiateUpload_WhenDraft_ShouldReturnPresignedUrlAndTrackAPendingAttachment()
    {
        var tenantId = Guid.NewGuid();
        var announcement = Announcement.Create(tenantId, "Title", "Content", AnnouncementType.General, AnnouncementPriority.Normal).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var announcements = Substitute.For<IAnnouncementRepository>();
        announcements.GetByIdAsync(tenantId, announcement.Id, Arg.Any<CancellationToken>()).Returns(announcement);
        var attachments = Substitute.For<IAnnouncementAttachmentRepository>();
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.InitiateUploadAsync(tenantId, AnnouncementAttachment.BucketName, Arg.Any<string>(), "image/png", Arg.Any<CancellationToken>())
            .Returns(new PresignedUrlRequest("https://upload.example/x", "req-1", DateTime.UtcNow.AddMinutes(15), new Dictionary<string, string>()));

        var handler = new InitiateAnnouncementAttachmentUploadHandler(announcements, attachments, fileStorage, tenant, Substitute.For<ICurrentUser>());
        var result = await handler.Handle(
            new InitiateAnnouncementAttachmentUploadCommand(announcement.Id, "logo.png", "image/png", 1024, AnnouncementAttachmentKind.Inline), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://upload.example/x", result.Value.UploadUrl);
        attachments.Received(1).Add(Arg.Is<AnnouncementAttachment>(a => a.FileName == "logo.png" && a.AnnouncementId == announcement.Id));
    }

    [Theory]
    [InlineData("virus.exe", "application/octet-stream")]
    [InlineData("logo.png.exe", "image/png")]
    public void InitiateUploadValidator_RejectsDisallowedFileTypes(string fileName, string contentType)
    {
        var command = new InitiateAnnouncementAttachmentUploadCommand(Guid.NewGuid(), fileName, contentType, 1024, AnnouncementAttachmentKind.Inline);
        var result = new InitiateAnnouncementAttachmentUploadValidator().Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ConfirmUpload_MarksTheAttachmentConfirmed_WithTheStorageReportedSize()
    {
        var tenantId = Guid.NewGuid();
        var attachment = AnnouncementAttachment.CreatePending(tenantId, Guid.NewGuid(), "logo.png", "key/logo.png", "image/png", AnnouncementAttachmentKind.Inline, Guid.NewGuid());

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var attachments = Substitute.For<IAnnouncementAttachmentRepository>();
        attachments.FindByIdAsync(tenantId, attachment.Id, Arg.Any<CancellationToken>()).Returns(attachment);
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.ConfirmUploadAsync(tenantId, AnnouncementAttachment.BucketName, attachment.StorageKey, "etag-1", Arg.Any<CancellationToken>()).Returns(2048L);
        fileStorage.GetDownloadUrlAsync(tenantId, AnnouncementAttachment.BucketName, attachment.StorageKey, 60, Arg.Any<CancellationToken>())
            .Returns(new PresignedUrlResponse("https://download.example/x", DateTime.UtcNow.AddHours(1)));

        var handler = new ConfirmAnnouncementAttachmentUploadHandler(attachments, fileStorage, tenant);
        var result = await handler.Handle(new ConfirmAnnouncementAttachmentUploadCommand(attachment.Id, "etag-1", 2048), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(attachment.IsConfirmed);
        Assert.Equal(2048, attachment.SizeBytes);
        Assert.Equal("https://download.example/x", result.Value.DownloadUrl);
    }
}

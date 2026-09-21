using NSubstitute;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>Profile photo rules: only real JPEG/PNG/WebP bytes within the size cap are stored, one row per employee.</summary>
public sealed class EmployeePhotoTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x10, 0x00, 0x00, 0x00, .. "WEBP"u8, 0x56];

    private static (EmployeePhotoWriter Writer, IEmployeePhotoRepository Photos) CreateWriter()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var photos = Substitute.For<IEmployeePhotoRepository>();
        return (new EmployeePhotoWriter(tenant, photos), photos);
    }

    [Fact]
    public void DetectContentType_RecognisesTheThreeSupportedFormats()
    {
        EmployeePhotoImage.DetectContentType(Jpeg).ShouldBe("image/jpeg");
        EmployeePhotoImage.DetectContentType(Png).ShouldBe("image/png");
        EmployeePhotoImage.DetectContentType(Webp).ShouldBe("image/webp");
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("GIF89a")]
    [InlineData("%PDF-1.7")]
    [InlineData("")]
    public void DetectContentType_RejectsEverythingElse(string text) =>
        EmployeePhotoImage.DetectContentType(System.Text.Encoding.ASCII.GetBytes(text)).ShouldBeNull();

    [Fact]
    public void ETag_ChangesWithTheContentAndIsQuoted()
    {
        var first = EmployeePhotoImage.ComputeETag(Jpeg);

        first.ShouldStartWith("\"");
        first.ShouldEndWith("\"");
        EmployeePhotoImage.ComputeETag(Png).ShouldNotBe(first);
        EmployeePhotoImage.ComputeETag(Jpeg).ShouldBe(first);
    }

    [Fact]
    public async Task Save_NewPhoto_AddsARowWithTheSniffedType()
    {
        var (writer, photos) = CreateWriter();
        photos.GetTrackedAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns((EmployeePhoto?)null);

        var result = await writer.SaveAsync(EmployeeId, Png, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        photos.Received(1).Add(Arg.Is<EmployeePhoto>(p => p.Id == EmployeeId && p.TenantId == TenantId && p.ContentType == "image/png"));
    }

    [Fact]
    public async Task Save_ExistingPhoto_ReplacesItInsteadOfAddingASecondRow()
    {
        var (writer, photos) = CreateWriter();
        var existing = EmployeePhoto.Create(TenantId, EmployeeId, "image/png", Png);
        photos.GetTrackedAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await writer.SaveAsync(EmployeeId, Jpeg, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        existing.ContentType.ShouldBe("image/jpeg");
        existing.Content.ShouldBe(Jpeg);
        photos.DidNotReceive().Add(Arg.Any<EmployeePhoto>());
    }

    [Fact]
    public async Task Save_EmptyFile_Fails()
    {
        var (writer, photos) = CreateWriter();

        var result = await writer.SaveAsync(EmployeeId, [], CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeDomainErrors.PhotoRequired);
        photos.DidNotReceive().Add(Arg.Any<EmployeePhoto>());
    }

    [Fact]
    public async Task Save_TooLarge_Fails()
    {
        var (writer, photos) = CreateWriter();
        var big = new byte[EmployeeLimits.PhotoMaxBytes + 1];
        Jpeg.CopyTo(big, 0);

        var result = await writer.SaveAsync(EmployeeId, big, CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeDomainErrors.PhotoTooLarge);
        photos.DidNotReceive().Add(Arg.Any<EmployeePhoto>());
    }

    [Fact]
    public async Task Save_NotAnImage_Fails()
    {
        var (writer, photos) = CreateWriter();

        var result = await writer.SaveAsync(EmployeeId, "<script>alert(1)</script>"u8.ToArray(), CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeDomainErrors.PhotoTypeUnsupported);
        photos.DidNotReceive().Add(Arg.Any<EmployeePhoto>());
    }

    [Fact]
    public async Task Delete_RemovesTheRow_AndIsIdempotentWhenThereIsNone()
    {
        var (writer, photos) = CreateWriter();
        var existing = EmployeePhoto.Create(TenantId, EmployeeId, "image/png", Png);
        photos.GetTrackedAsync(EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeePhoto?>(existing), Task.FromResult<EmployeePhoto?>(null));

        (await writer.DeleteAsync(EmployeeId, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await writer.DeleteAsync(EmployeeId, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        photos.Received(1).Remove(existing);
    }
}

using Sense.Modules.Documents.Domain.SharedFiles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests.SharedFiles;

public sealed class SharedCompanyFileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UploadedBy = Guid.NewGuid();

    private static SharedCompanyFile ValidEveryoneFile() =>
        SharedCompanyFile.Create(TenantId, "Personel Yönetmeliği.pdf", "abc/handbook.pdf", "application/pdf", 1024, UploadedBy).Value;

    [Fact]
    public void Create_ValidEveryoneFile_Succeeds_InPendingStatus()
    {
        var result = SharedCompanyFile.Create(TenantId, "Handbook.pdf", "key/handbook.pdf", "application/pdf", 2048, UploadedBy);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("Handbook.pdf");
        result.Value.Status.ShouldBe(SharedFileStatus.Pending);
        result.Value.Visibility.ShouldBe(SharedFileVisibility.Everyone);
        result.Value.AllowedRoles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_NameRequired_Fails(string? name) =>
        SharedCompanyFile.Create(TenantId, name!, "key", "application/pdf", 1024, UploadedBy)
            .Error.Code.ShouldBe(SharedFilesDomainErrors.NameRequired);

    [Fact]
    public void Create_NameTooLong_Fails() =>
        SharedCompanyFile.Create(TenantId, new string('a', SharedFilesLimits.FileNameMaxLength + 1), "key", "application/pdf", 1024, UploadedBy)
            .Error.Code.ShouldBe(SharedFilesDomainErrors.NameTooLong);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_FileKeyRequired_Fails(string? key) =>
        SharedCompanyFile.Create(TenantId, "File.pdf", key!, "application/pdf", 1024, UploadedBy)
            .Error.Code.ShouldBe(SharedFilesDomainErrors.FileKeyRequired);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_InvalidSize_Fails(long size) =>
        SharedCompanyFile.Create(TenantId, "File.pdf", "key", "application/pdf", size, UploadedBy)
            .Error.Code.ShouldBe(SharedFilesDomainErrors.InvalidSize);

    [Fact]
    public void Create_SizeAboveMax_Fails() =>
        SharedCompanyFile.Create(TenantId, "File.pdf", "key", "application/pdf", SharedFilesLimits.MaxFileSize + 1, UploadedBy)
            .Error.Code.ShouldBe(SharedFilesDomainErrors.InvalidSize);

    [Fact]
    public void Create_UploaderRequired_Fails() =>
        SharedCompanyFile.Create(TenantId, "File.pdf", "key", "application/pdf", 1024, Guid.Empty)
            .Error.Code.ShouldBe(SharedFilesDomainErrors.UploaderRequired);

    [Fact]
    public void Create_RestrictedVisibilityWithoutRoles_Fails() =>
        SharedCompanyFile.Create(TenantId, "File.pdf", "key", "application/pdf", 1024, UploadedBy, SharedFileVisibility.RestrictedToRoles)
            .Error.Code.ShouldBe(SharedFilesDomainErrors.RolesRequiredForRestrictedVisibility);

    [Fact]
    public void Create_RestrictedVisibilityWithRoles_Succeeds_AndStoresDistinctRoles()
    {
        var result = SharedCompanyFile.Create(
            TenantId, "Payroll.xlsx", "key", "application/pdf", 1024, UploadedBy,
            SharedFileVisibility.RestrictedToRoles, ["HR", "Admin", "HR"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AllowedRoles.Count.ShouldBe(2);
        result.Value.AllowedRoles.ShouldContain("HR");
        result.Value.AllowedRoles.ShouldContain("Admin");
    }

    [Fact]
    public void ConfirmUpload_FromPending_TransitionsToActive_AndSetsETagAndSize()
    {
        var file = ValidEveryoneFile();

        file.ConfirmUpload("etag-123", 4096);

        file.Status.ShouldBe(SharedFileStatus.Active);
        file.ETag.ShouldBe("etag-123");
        file.Size.ShouldBe(4096);
    }

    [Fact]
    public void ConfirmUpload_WhenNotPending_Throws()
    {
        var file = ValidEveryoneFile();
        file.ConfirmUpload("etag-123", 4096);

        Should.Throw<InvalidOperationException>(() => file.ConfirmUpload("etag-456", 8192));
    }

    [Fact]
    public void Delete_FromActive_MarksDeleted_AndRecordsWhoAndWhen()
    {
        var file = ValidEveryoneFile();
        file.ConfirmUpload("etag-123", 4096);
        var deletedBy = Guid.NewGuid();

        file.Delete(deletedBy);

        file.Status.ShouldBe(SharedFileStatus.Deleted);
        file.DeletedBy.ShouldBe(deletedBy);
        file.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Delete_AlreadyDeleted_Throws()
    {
        var file = ValidEveryoneFile();
        file.Delete(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() => file.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void IsVisibleTo_Everyone_IsVisibleRegardlessOfCallerRoles()
    {
        var file = ValidEveryoneFile();

        file.IsVisibleTo(new HashSet<string>()).ShouldBeTrue();
        file.IsVisibleTo(new HashSet<string> { "SomeRole" }).ShouldBeTrue();
    }

    [Fact]
    public void IsVisibleTo_RestrictedToRoles_VisibleOnlyWhenCallerHasAnAllowedRole()
    {
        var file = SharedCompanyFile.Create(
            TenantId, "Payroll.xlsx", "key", "application/pdf", 1024, UploadedBy,
            SharedFileVisibility.RestrictedToRoles, ["HR", "Admin"]).Value;

        file.IsVisibleTo(new HashSet<string> { "HR" }).ShouldBeTrue();
        file.IsVisibleTo(new HashSet<string> { "hr" }).ShouldBeTrue(); // case-insensitive
        file.IsVisibleTo(new HashSet<string> { "Employee" }).ShouldBeFalse();
        file.IsVisibleTo(new HashSet<string>()).ShouldBeFalse();
    }
}

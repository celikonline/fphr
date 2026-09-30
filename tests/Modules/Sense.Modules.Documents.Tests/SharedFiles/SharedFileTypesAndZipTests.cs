using System.IO.Compression;
using NSubstitute;
using Sense.Modules.Documents.Application.SharedFiles;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests.SharedFiles;

public sealed class SharedFileTypesAndZipTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ICurrentUser Caller(params string[] roles)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Roles.Returns(new HashSet<string>(roles));
        return user;
    }

    private static SharedCompanyFile Active(string name, bool mobile = false, SharedFileVisibility vis = SharedFileVisibility.Everyone, params string[] roles)
    {
        var f = SharedCompanyFile.Create(TenantId, name, $"k/{name}", "application/pdf", 10, UserId, vis, roles).Value;
        f.ConfirmUpload("etag", 10);
        f.Classify(null, mobile);
        return f;
    }

    private static ISharedCompanyFileRepository Repo(params SharedCompanyFile[] files)
    {
        var repo = Substitute.For<ISharedCompanyFileRepository>();
        repo.ListByTenantAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SharedCompanyFile>>(files));
        return repo;
    }

    [Fact]
    public async Task Shared_file_mobile_flag_filters_mobile_list()
    {
        var repo = Repo(Active("web-only.pdf"), Active("mobile.pdf", mobile: true));
        var handler = new ListSharedFilesHandler(repo, Caller("Employee"));

        var mobile = await handler.Handle(new ListSharedFilesQuery(new PagedQuery(), MobileOnly: true), CancellationToken.None);
        var all = await handler.Handle(new ListSharedFilesQuery(new PagedQuery()), CancellationToken.None);

        mobile.Value.Items.Select(i => i.FileName).ShouldBe(["mobile.pdf"]);
        mobile.Value.Items[0].VisibleOnMobile.ShouldBeTrue();
        all.Value.Items.Count.ShouldBe(2);
    }

    private static DownloadSharedFilesZipHandler ZipHandler(ISharedCompanyFileRepository repo, params string[] roles)
    {
        var reader = Substitute.For<ISharedFileContentReader>();
        reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(ci.ArgAt<string>(1))));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return new DownloadSharedFilesZipHandler(repo, reader, tenant, Caller(roles), TimeProvider.System);
    }

    private static List<string> Entries(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    [Fact]
    public async Task Zip_download_respects_visibility_and_roles()
    {
        var everyone = Active("handbook.pdf");
        var hrOnly = Active("payroll.pdf", vis: SharedFileVisibility.RestrictedToRoles, roles: "HR");
        var pending = SharedCompanyFile.Create(TenantId, "pending.pdf", "k/pending", "application/pdf", 10, UserId).Value;
        var repo = Repo(everyone, hrOnly, pending);

        var asEmployee = await ZipHandler(repo, "Employee").Handle(new DownloadSharedFilesZipQuery(), CancellationToken.None);
        var asHr = await ZipHandler(repo, "HR").Handle(new DownloadSharedFilesZipQuery(), CancellationToken.None);
        // Explicitly asking for a restricted id must not leak it either.
        var explicitAsEmployee = await ZipHandler(repo, "Employee").Handle(
            new DownloadSharedFilesZipQuery(FileIds: [hrOnly.Id]), CancellationToken.None);

        Entries(asEmployee.Value.Content).ShouldBe(["handbook.pdf"]);
        Entries(asHr.Value.Content).ShouldBe(["handbook.pdf", "payroll.pdf"], ignoreOrder: true);
        explicitAsEmployee.IsFailure.ShouldBeTrue();
        explicitAsEmployee.Error.Code.ShouldBe(SharedFilesDomainErrors.NothingToDownload);
    }

    [Fact]
    public async Task Zip_download_dedupes_equal_file_names()
    {
        var repo = Repo(Active("a.pdf"), SharedCompanyFile.Create(TenantId, "a.pdf", "k/other", "application/pdf", 10, UserId).Value.Also(f => f.ConfirmUpload("e", 10)));

        var result = await ZipHandler(repo, "Employee").Handle(new DownloadSharedFilesZipQuery(), CancellationToken.None);

        Entries(result.Value.Content).ShouldBe(["a.pdf", "a (2).pdf"], ignoreOrder: true);
    }

    [Fact]
    public async Task Type_catalog_create_rejects_duplicate_name()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var types = Substitute.For<ISharedFileTypeRepository>();
        types.NameExistsAsync("Form", null, Arg.Any<CancellationToken>()).Returns(true);

        var dup = await new CreateSharedFileTypeHandler(tenant, types)
            .Handle(new CreateSharedFileTypeCommand("Form", true), CancellationToken.None);

        dup.Error.Code.ShouldBe(SharedFilesDomainErrors.TypeNameTaken);
    }

    [Fact]
    public async Task Upload_inherits_mobile_default_from_type_unless_overridden()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var type = SharedFileType.Create(TenantId, "Yönetmelik", visibleOnMobile: true, 0).Value;
        var types = Substitute.For<ISharedFileTypeRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));
        var files = Substitute.For<ISharedCompanyFileRepository>();
        var storage = Substitute.For<Sense.Shared.Contracts.Storage.IFileStorage>();
        storage.InitiateUploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Sense.Shared.Contracts.Storage.PresignedUrlRequest("u", "r", DateTime.UtcNow, new Dictionary<string, string>()));
        var user = Caller();
        user.UserId.Returns(UserId);
        var handler = new InitiateSharedFileUploadHandler(
            storage, tenant, user, files,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InitiateSharedFileUploadHandler>.Instance, types);

        var inherited = await handler.Handle(new InitiateSharedFileUploadCommand("a.pdf", "application/pdf", 10, TypeId: type.Id), CancellationToken.None);
        var overridden = await handler.Handle(new InitiateSharedFileUploadCommand("b.pdf", "application/pdf", 10, TypeId: type.Id, VisibleOnMobile: false), CancellationToken.None);

        inherited.IsSuccess.ShouldBeTrue();
        overridden.IsSuccess.ShouldBeTrue();
        files.Received(1).Add(Arg.Is<SharedCompanyFile>(f => f.FileName == "a.pdf" && f.VisibleOnMobile && f.TypeId == type.Id));
        files.Received(1).Add(Arg.Is<SharedCompanyFile>(f => f.FileName == "b.pdf" && !f.VisibleOnMobile));
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}

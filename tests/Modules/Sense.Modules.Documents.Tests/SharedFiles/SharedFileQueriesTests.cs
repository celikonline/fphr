using NSubstitute;
using Sense.Modules.Documents.Application.SharedFiles;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests.SharedFiles;

public sealed class SharedFileQueriesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static ICurrentUser CurrentUserWithRoles(params string[] roles)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Roles.Returns(new HashSet<string>(roles));
        return user;
    }

    private static SharedCompanyFile Everyone(string name) =>
        SharedCompanyFile.Create(TenantId, name, $"key/{name}", "application/pdf", 1024, UserId).Value;

    private static SharedCompanyFile RestrictedTo(string name, params string[] roles) =>
        SharedCompanyFile.Create(TenantId, name, $"key/{name}", "application/pdf", 1024, UserId, SharedFileVisibility.RestrictedToRoles, roles).Value;

    [Fact]
    public async Task List_MixOfEveryoneAndRestrictedFiles_OnlyReturnsFilesVisibleToCaller()
    {
        var everyone = Everyone("Handbook.pdf");
        var hrOnly = RestrictedTo("Payroll.xlsx", "HR");
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.ListByTenantAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SharedCompanyFile>>([everyone, hrOnly]));
        var handler = new ListSharedFilesHandler(files, CurrentUserWithRoles("Employee"));

        var result = await handler.Handle(new ListSharedFilesQuery(new PagedQuery()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(1);
        result.Value.Items[0].FileName.ShouldBe("Handbook.pdf");
        result.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task List_CallerWithAllowedRole_SeesRestrictedFileToo()
    {
        var everyone = Everyone("Handbook.pdf");
        var hrOnly = RestrictedTo("Payroll.xlsx", "HR");
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.ListByTenantAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SharedCompanyFile>>([everyone, hrOnly]));
        var handler = new ListSharedFilesHandler(files, CurrentUserWithRoles("HR"));

        var result = await handler.Handle(new ListSharedFilesQuery(new PagedQuery()), CancellationToken.None);

        result.Value.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Get_RestrictedFile_CallerWithoutRole_ReturnsNotFound()
    {
        var hrOnly = RestrictedTo("Payroll.xlsx", "HR");
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.GetByIdAsync(hrOnly.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(hrOnly)));
        var storage = Substitute.For<IFileStorage>();
        var handler = new GetSharedFileHandler(storage, Tenant(), CurrentUserWithRoles("Employee"), files);

        var result = await handler.Handle(new GetSharedFileQuery(hrOnly.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SharedFilesDomainErrors.NotFound);
        await storage.DidNotReceive().GetDownloadUrlAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_VisibleFile_ReturnsDownloadUrl()
    {
        var everyone = Everyone("Handbook.pdf");
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.GetByIdAsync(everyone.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(everyone)));
        var storage = Substitute.For<IFileStorage>();
        storage.GetDownloadUrlAsync(TenantId, SharedCompanyFile.BucketName, everyone.FileKey, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PresignedUrlResponse("https://storage/download", DateTime.UtcNow.AddMinutes(15))));
        var handler = new GetSharedFileHandler(storage, Tenant(), CurrentUserWithRoles("Employee"), files);

        var result = await handler.Handle(new GetSharedFileQuery(everyone.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DownloadUrl.ShouldBe("https://storage/download");
    }

    [Fact]
    public async Task Quota_ReturnsUsageAgainstDefaultQuota()
    {
        var files = Substitute.For<ISharedCompanyFileRepository>();
        files.SumActiveSizeByTenantAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(10_000_000L));
        var handler = new GetSharedFileQuotaHandler(files);

        var result = await handler.Handle(new GetSharedFileQuotaQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UsedBytes.ShouldBe(10_000_000L);
        result.Value.QuotaBytes.ShouldBe(SharedFilesLimits.DefaultQuotaBytes);
        result.Value.IsExceeded.ShouldBeFalse();
    }
}

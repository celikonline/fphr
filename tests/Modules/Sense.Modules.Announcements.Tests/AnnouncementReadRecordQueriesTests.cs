using NSubstitute;
using Sense.Modules.Announcements.Application.ReadRecords;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class AnnouncementReadRecordQueriesTests
{
    [Fact]
    public async Task ListAnnouncementReadRecordsQuery_WithValidQuery_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.GetByAnnouncementIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>())
            .Returns(new List<AnnouncementReadRecord>());

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new ListAnnouncementReadRecordsHandler(repository, tenantContext);
        var result = await handler.Handle(new ListAnnouncementReadRecordsQuery(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task CountAnnouncementReadersQuery_WithValidQuery_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.CountByAnnouncementIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>()).Returns(5);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new CountAnnouncementReadersHandler(repository, tenantContext);
        var result = await handler.Handle(new CountAnnouncementReadersQuery(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public async Task MarkAnnouncementRead_WhenNotYetRead_AddsRecord()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.ExistsAsync(tenantId, announcementId, employeeId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new MarkAnnouncementReadHandler(tenant, currentUser, repository);
        var result = await handler.Handle(new MarkAnnouncementReadCommand(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Received(1).Add(Arg.Is<AnnouncementReadRecord>(r => r.AnnouncementId == announcementId && r.EmployeeId == employeeId));
    }

    [Fact]
    public async Task MarkAnnouncementRead_WhenAlreadyRead_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.ExistsAsync(tenantId, announcementId, employeeId, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new MarkAnnouncementReadHandler(tenant, currentUser, repository);
        var result = await handler.Handle(new MarkAnnouncementReadCommand(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.DidNotReceive().Add(Arg.Any<AnnouncementReadRecord>());
    }
}

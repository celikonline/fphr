using NSubstitute;
using Sense.Shared.Contracts.Tenants;
using Sense.Modules.Announcements.Application.ReadRecords;
using Sense.Modules.Announcements.Domain;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

[Trait("Category", "Integration")]
public class AnnouncementReadRecordQueriesTests
{
    [Fact]
    public async Task ListAnnouncementReadRecordsQuery_WithValidQuery_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();
        var query = new ListAnnouncementReadRecordsQuery(tenantId, announcementId);

        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.GetByAnnouncementIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>())
            .Returns(new List<AnnouncementReadRecord>());

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new ListAnnouncementReadRecordsHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.IsType<List<AnnouncementReadRecordDto>>(result.Value);
    }

    [Fact]
    public async Task CountAnnouncementReadersQuery_WithValidQuery_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();
        var query = new CountAnnouncementReadersQuery(tenantId, announcementId);

        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.CountByAnnouncementIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>())
            .Returns(5);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new CountAnnouncementReadersHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void ListAnnouncementReadRecordsValidator_WithEmptyTenantId_ShouldFail()
    {
        // Arrange
        var query = new ListAnnouncementReadRecordsQuery(Guid.Empty, Guid.NewGuid());
        var validator = new ListAnnouncementReadRecordsValidator();

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CountAnnouncementReadersValidator_WithEmptyAnnouncementId_ShouldFail()
    {
        // Arrange
        var query = new CountAnnouncementReadersQuery(Guid.NewGuid(), Guid.Empty);
        var validator = new CountAnnouncementReadersValidator();

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }
}

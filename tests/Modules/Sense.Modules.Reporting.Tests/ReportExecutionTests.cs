using Sense.Modules.Reporting.Domain;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

public sealed class ReportExecutionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyReportKey_Fails()
    {
        var result = ReportExecution.Create(TenantId, " ", null, UserId, DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingDomainErrors.ReportKeyRequired);
    }

    [Fact]
    public void Create_WithValidReportKey_StartsQueued()
    {
        var result = ReportExecution.Create(TenantId, "documents.missingDocuments", null, UserId, DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Queued);
        result.Value.TenantId.ShouldBe(TenantId);
    }

    [Fact]
    public void Start_MovesToRunningAndStampsStartedAt()
    {
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, UserId, DateTime.UtcNow).Value;
        var now = DateTime.UtcNow;

        execution.Start(now);

        execution.Status.ShouldBe(ReportExecutionStatus.Running);
        execution.StartedAt.ShouldBe(now);
    }

    [Fact]
    public void Complete_StoresResultAndMovesToSucceeded()
    {
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        execution.Complete("report.csv", "text/csv", [1, 2, 3], now);

        execution.Status.ShouldBe(ReportExecutionStatus.Succeeded);
        execution.ResultFileName.ShouldBe("report.csv");
        execution.ResultContentType.ShouldBe("text/csv");
        execution.ResultContent.ShouldBe(new byte[] { 1, 2, 3 });
        execution.CompletedAt.ShouldBe(now);
    }

    [Fact]
    public void Fail_StoresErrorAndMovesToFailed()
    {
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        execution.Fail("boom", now);

        execution.Status.ShouldBe(ReportExecutionStatus.Failed);
        execution.ErrorMessage.ShouldBe("boom");
        execution.CompletedAt.ShouldBe(now);
    }
}

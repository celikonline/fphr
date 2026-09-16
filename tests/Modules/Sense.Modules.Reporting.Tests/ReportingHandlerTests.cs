using NSubstitute;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

public sealed class ReportingHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ITenantContext TenantContext()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static ICurrentUser CurrentUser(string? email = "user@example.com")
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        currentUser.Email.Returns(email);
        currentUser.DisplayName.Returns("Test User");
        return currentUser;
    }

    private static IReportProvider FakeProvider(string key, string? requiredPermission = null, ReportFileResult? result = null) =>
        new FakeReportProvider(key, requiredPermission, result ?? new ReportFileResult("report.csv", "text/csv", [1, 2, 3]));

    [Fact]
    public async Task RequestReportHandler_WithKnownProvider_ResolvesAndCompletesSynchronously()
    {
        var provider = FakeProvider("documents.missingDocuments");
        var executions = Substitute.For<IReportExecutionRepository>();
        var mailer = Substitute.For<IReportMailer>();
        var permissions = Substitute.For<IPermissionService>();
        var handler = new RequestReportHandler([provider], executions, mailer, TenantContext(), CurrentUser(), permissions, TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("documents.missingDocuments", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Succeeded);
        result.Value.ResultFileName.ShouldBe("report.csv");
        executions.Received(1).Add(Arg.Is<ReportExecution>(e => e.ReportKey == "documents.missingDocuments"));
        await mailer.Received(1).SendReportReadyAsync("user@example.com", "Test User", Arg.Any<Guid>(), "documents.missingDocuments", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestReportHandler_WithUnknownReportKey_FailsWithoutTouchingRepository()
    {
        var executions = Substitute.For<IReportExecutionRepository>();
        var mailer = Substitute.For<IReportMailer>();
        var permissions = Substitute.For<IPermissionService>();
        var handler = new RequestReportHandler([], executions, mailer, TenantContext(), CurrentUser(), permissions, TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("unknown.report", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.UnknownReportKey);
        executions.DidNotReceive().Add(Arg.Any<ReportExecution>());
    }

    [Fact]
    public async Task RequestReportHandler_WhenCallerLacksProviderPermission_FailsForbidden()
    {
        var provider = FakeProvider("payroll.costReport", requiredPermission: "payroll.reports.view");
        var executions = Substitute.For<IReportExecutionRepository>();
        var mailer = Substitute.For<IReportMailer>();
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(UserId, "payroll.reports.view", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new RequestReportHandler([provider], executions, mailer, TenantContext(), CurrentUser(), permissions, TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("payroll.costReport", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ReportPermissionDenied);
        executions.DidNotReceive().Add(Arg.Any<ReportExecution>());
    }

    [Fact]
    public async Task RequestReportHandler_WhenProviderThrows_CompletesExecutionAsFailedInsteadOfThrowing()
    {
        var provider = Substitute.For<IReportProvider>();
        provider.Key.Returns("broken.report");
        provider.RequiredPermission.Returns((string?)null);
        provider.GenerateAsync(TenantId, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReportFileResult>(new InvalidOperationException("boom")));
        var executions = Substitute.For<IReportExecutionRepository>();
        var mailer = Substitute.For<IReportMailer>();
        var permissions = Substitute.For<IPermissionService>();
        var handler = new RequestReportHandler([provider], executions, mailer, TenantContext(), CurrentUser(), permissions, TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("broken.report", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Failed);
        result.Value.ErrorMessage.ShouldBe("boom");
        await mailer.DidNotReceive().SendReportReadyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListReportCatalogHandler_OnlyReturnsProvidersTheCallerIsPermittedToSee()
    {
        var openProvider = FakeProvider("documents.missingDocuments");
        var gatedProvider = FakeProvider("payroll.costReport", requiredPermission: "payroll.reports.view");
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(UserId, "payroll.reports.view", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new ListReportCatalogHandler([openProvider, gatedProvider], CurrentUser(), permissions);

        var result = await handler.Handle(new ListReportCatalogQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value[0].Key.ShouldBe("documents.missingDocuments");
    }

    [Fact]
    public async Task GetReportExecutionHandler_WhenNotOwner_FailsForbidden()
    {
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, Guid.NewGuid(), DateTime.UtcNow).Value;
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(execution));
        var handler = new GetReportExecutionHandler(executions, CurrentUser());

        var result = await handler.Handle(new GetReportExecutionQuery(execution.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.NotOwner);
    }

    [Fact]
    public async Task ListMyReportExecutionsHandler_ReturnsRequesterOwnHistoryPaged()
    {
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, UserId, DateTime.UtcNow).Value;
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.ListByRequesterAsync(UserId, 0, 20, Arg.Any<CancellationToken>())
            .Returns((new List<ReportExecution> { execution }, 1));
        var handler = new ListMyReportExecutionsHandler(executions, CurrentUser());

        var result = await handler.Handle(new ListMyReportExecutionsQuery(new PagedQuery()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items[0].Id.ShouldBe(execution.Id);
    }

    private sealed class FakeReportProvider(string key, string? requiredPermission, ReportFileResult result) : IReportProvider
    {
        public string Key { get; } = key;

        public string? RequiredPermission { get; } = requiredPermission;

        public Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}

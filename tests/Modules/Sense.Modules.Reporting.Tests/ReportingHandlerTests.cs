using NSubstitute;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Events;
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
    public async Task RequestReportHandler_WhenNotifyIsFalse_CompletesWithoutSendingTheReadyEmail()
    {
        var provider = FakeProvider("documents.missingDocuments");
        var mailer = Substitute.For<IReportMailer>();
        var handler = new RequestReportHandler([provider], Substitute.For<IReportExecutionRepository>(), mailer, TenantContext(), CurrentUser(), Substitute.For<IPermissionService>(), TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("documents.missingDocuments", null, Notify: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Succeeded);
        await mailer.DidNotReceive().SendReportReadyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
    public async Task GetReportExecutionFileHandler_WhenNotOwner_FailsForbiddenWithoutLeakingContent()
    {
        // Tenant/user izolasyonu: repository tenant filtresinden geçmiş olsa bile (aynı tenant, başka
        // kullanıcı) execution'ın bytes'ı döndürülmemeli - bkz. ReportExecutionRepository.GetByIdAsync'in
        // XML doc'u: "Query is automatically filtered by tenant via global filter" - burada ek bir
        // kullanıcı sahiplik kontrolü olmadan tenant filtresi tek başına yeterli değil.
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, Guid.NewGuid(), DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);
        execution.Complete("report.csv", "text/csv", [1, 2, 3], DateTime.UtcNow);
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(execution));
        var handler = new GetReportExecutionFileHandler(executions, CurrentUser());

        var result = await handler.Handle(new GetReportExecutionFileQuery(execution.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.NotOwner);
    }

    [Fact]
    public async Task GetReportExecutionFileHandler_WhenNotYetSucceeded_FailsNotReady()
    {
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow); // still Running, no ResultContent yet
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(execution));
        var handler = new GetReportExecutionFileHandler(executions, CurrentUser());

        var result = await handler.Handle(new GetReportExecutionFileQuery(execution.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ReportNotReady);
    }

    [Fact]
    public async Task GetReportExecutionFileHandler_WhenOwnerAndSucceeded_ReturnsFile()
    {
        var execution = ReportExecution.Create(TenantId, "documents.missingDocuments", null, UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);
        execution.Complete("report.csv", "text/csv", [1, 2, 3], DateTime.UtcNow);
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(execution));
        var handler = new GetReportExecutionFileHandler(executions, CurrentUser());

        var result = await handler.Handle(new GetReportExecutionFileQuery(execution.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("report.csv");
        result.Value.ContentType.ShouldBe("text/csv");
        result.Value.Content.ShouldBe(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task GetReportExecutionFileHandler_PdfRendersTheSavedExecutionSnapshot()
    {
        var savedWorkbook = new byte[] { 7, 8, 9 };
        var execution = ReportExecution.Create(TenantId, "leave.balance.detail", null, UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);
        execution.Complete("leave-balances.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", savedWorkbook, DateTime.UtcNow);
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(execution));
        var reader = Substitute.For<IReportTableReader>();
        reader.Read(savedWorkbook, Arg.Any<CancellationToken>()).Returns([
            new ExportTable("Balances", [new ExportColumn("Employee"), new ExportColumn("Balance", ExportColumnType.Number)],
                [["Ayşe", 12m]])]);
        var pdfWriter = Substitute.For<ISimplePdfWriter>();
        pdfWriter.Write(Arg.Any<SimplePdfDocument>()).Returns([1, 2, 3]);
        var handler = new GetReportExecutionFileHandler(executions, CurrentUser(), reader, pdfWriter);

        var result = await handler.Handle(new GetReportExecutionFileQuery(execution.Id, "pdf"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("leave-balances.pdf");
        result.Value.ContentType.ShouldBe("application/pdf");
        result.Value.Content.ShouldBe([1, 2, 3]);
        reader.Received(1).Read(savedWorkbook, Arg.Any<CancellationToken>());
        var document = pdfWriter.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<SimplePdfDocument>();
        document.Blocks.OfType<SimplePdfTable>().Single().Rows.Single().ShouldBe(["Ayşe", "12"]);
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

    [Theory]
    [InlineData("late", true, null)]
    [InlineData("nonsense", false, ReportingErrors.ParameterInvalid)]
    [InlineData(" ", false, ReportingErrors.ParameterRequired)]
    public void ReportParameterValidation_SelectParameter_MustBeOneOfOptions(string value, bool ok, string? code)
    {
        var descriptors = new[] { new ReportParameterDescriptor("status", ReportParameterKind.Select, IsRequired: true, Options: ["late", "absent"]) };

        var result = ReportParameterValidation.Validate(descriptors, new Dictionary<string, string> { ["status"] = value });

        result.IsSuccess.ShouldBe(ok);
        if (!ok)
        {
            result.Error.Code.ShouldBe(code);
        }
    }

    [Fact]
    public void ReportParameterValidation_OptionalSelectParameter_MayBeOmitted()
    {
        var descriptors = new[] { new ReportParameterDescriptor("format", ReportParameterKind.Select, Options: ["xlsx", "pdf"]) };

        ReportParameterValidation.Validate(descriptors, new Dictionary<string, string>()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task RequestReportHandler_WhenRequiredParameterMissing_FailsBeforeCreatingExecution()
    {
        var provider = new FakeReportProvider(
            "leave.requests", null, new ReportFileResult("r.xlsx", "x", [1]), ReportCategories.Leave,
            [new ReportParameterDescriptor(ReportParameterKeys.DateRange, ReportParameterKind.DateRange, IsRequired: true)]);
        var executions = Substitute.For<IReportExecutionRepository>();
        var handler = new RequestReportHandler([provider], executions, Substitute.For<IReportMailer>(), TenantContext(), CurrentUser(), Substitute.For<IPermissionService>(), TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("leave.requests", new Dictionary<string, string> { ["from"] = " " }), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ParameterRequired);
        executions.DidNotReceive().Add(Arg.Any<ReportExecution>());
    }

    [Theory]
    [InlineData("orgUnitIds", "not-a-guid")]
    [InlineData("year", "20x6")]
    [InlineData("month", "13")]
    [InlineData("includeTerminated", "evet")]
    public async Task RequestReportHandler_WhenParameterMalformed_FailsInvalid(string key, string value)
    {
        var provider = new FakeReportProvider(
            "employee.list", null, new ReportFileResult("r.xlsx", "x", [1]), ReportCategories.Employee,
            [
                new ReportParameterDescriptor(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
                new ReportParameterDescriptor(ReportParameterKeys.Year, ReportParameterKind.Year),
                new ReportParameterDescriptor(ReportParameterKeys.Month, ReportParameterKind.Month),
                new ReportParameterDescriptor(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
            ]);
        var handler = new RequestReportHandler([provider], Substitute.For<IReportExecutionRepository>(), Substitute.For<IReportMailer>(), TenantContext(), CurrentUser(), Substitute.For<IPermissionService>(), TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("employee.list", new Dictionary<string, string> { [key] = value }), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ParameterInvalid);
    }

    [Fact]
    public async Task RequestReportHandler_WhenDateRangeInverted_FailsInvalid()
    {
        var provider = new FakeReportProvider(
            "leave.requests", null, new ReportFileResult("r.xlsx", "x", [1]), ReportCategories.Leave,
            [new ReportParameterDescriptor(ReportParameterKeys.DateRange, ReportParameterKind.DateRange, IsRequired: true)]);
        var handler = new RequestReportHandler([provider], Substitute.For<IReportExecutionRepository>(), Substitute.For<IReportMailer>(), TenantContext(), CurrentUser(), Substitute.For<IPermissionService>(), TimeProvider.System);

        var result = await handler.Handle(
            new RequestReportCommand("leave.requests", new Dictionary<string, string> { ["from"] = "2026-02-01", ["to"] = "2026-01-01" }), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ParameterInvalid);
    }

    [Fact]
    public async Task CustomReportRequest_WithIncompatibleDetails_FailsBeforeQueueing()
    {
        var provider = new FakeReportProvider(
            "report.custom", ReportingPermissions.CustomBuild.Key, new ReportFileResult("r.xlsx", "x", [1]), ReportCategories.Employee,
            [new ReportParameterDescriptor("fieldKeys", ReportParameterKind.Select, IsRequired: true), new ReportParameterDescriptor(ReportParameterKeys.DateRange, ReportParameterKind.DateRange)]);
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(UserId, ReportingPermissions.CustomBuild.Key, Arg.Any<CancellationToken>()).Returns(true);
        permissions.GetPermissionsAsync(UserId, Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        var contributor = Substitute.For<ICustomReportFieldContributor>();
        contributor.GetFieldsAsync(TenantId, Arg.Any<CancellationToken>()).Returns([
            new CustomReportFieldDescriptor("leave.request.days", CustomReportKeys.LeaveRequests, "label", CustomReportValueType.Number, CustomReportGrain.Detail, 1),
            new CustomReportFieldDescriptor("leave.overtime.workDate", CustomReportKeys.LeaveOvertime, "label", CustomReportValueType.Date, CustomReportGrain.Detail, 1),
        ]);
        var executions = Substitute.For<IReportExecutionRepository>();
        var handler = new RequestReportHandler([provider], executions, Substitute.For<IReportMailer>(), TenantContext(), CurrentUser(), permissions,
            TimeProvider.System, customContributors: [contributor]);

        var result = await handler.Handle(new RequestReportCommand("report.custom", new Dictionary<string, string>
        {
            ["fieldKeys"] = "[\"leave.request.days\",\"leave.overtime.workDate\"]",
            ["from"] = "2026-01-01",
            ["to"] = "2026-12-31",
        }), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CustomReportErrors.IncompatibleDetails);
        executions.DidNotReceive().Add(Arg.Any<ReportExecution>());
    }

    [Fact]
    public async Task RequestReportHandler_TrimsAndDropsBlankParametersBeforeCallingProvider()
    {
        var provider = new FakeReportProvider(
            "employee.list", null, new ReportFileResult("r.xlsx", "x", [1]), ReportCategories.Employee,
            [new ReportParameterDescriptor(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits)]);
        var unitId = Guid.NewGuid();
        var handler = new RequestReportHandler([provider], Substitute.For<IReportExecutionRepository>(), Substitute.For<IReportMailer>(), TenantContext(), CurrentUser(), Substitute.For<IPermissionService>(), TimeProvider.System);

        var result = await handler.Handle(
            new RequestReportCommand("employee.list", new Dictionary<string, string> { ["orgUnitIds"] = $" {unitId} ", ["includeTerminated"] = "" }), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Succeeded);
        provider.ReceivedParameters.ShouldNotBeNull();
        provider.ReceivedParameters.Keys.ShouldBe(["orgUnitIds"]);
        provider.ReceivedParameters["orgUnitIds"].ShouldBe(unitId.ToString());
    }

    [Fact]
    public async Task ListReportCatalogHandler_OrdersByMenuCategoryThenKey_AndCarriesParameters()
    {
        var finance = new FakeReportProvider("finance.salaries", null, new ReportFileResult("r", "x", [1]), ReportCategories.Finance);
        var leave = new FakeReportProvider(
            "leave.requests", null, new ReportFileResult("r", "x", [1]), ReportCategories.Leave,
            [new ReportParameterDescriptor(ReportParameterKeys.DateRange, ReportParameterKind.DateRange, IsRequired: true)]);
        var adminB = new FakeReportProvider("leave.balance.summary", null, new ReportFileResult("r", "x", [1]), ReportCategories.Administrative);
        var adminA = new FakeReportProvider("documents.missingDocuments", null, new ReportFileResult("r", "x", [1]), ReportCategories.Administrative);
        var handler = new ListReportCatalogHandler([finance, leave, adminB, adminA], CurrentUser(), Substitute.For<IPermissionService>());

        var result = await handler.Handle(new ListReportCatalogQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(v => v.Key).ShouldBe(["documents.missingDocuments", "leave.balance.summary", "leave.requests", "finance.salaries"]);
        result.Value[2].CategoryKey.ShouldBe(ReportCategories.Leave);
        result.Value[2].Parameters.Single().Kind.ShouldBe(ReportParameterKind.DateRange);
        result.Value[2].Parameters.Single().IsRequired.ShouldBeTrue();
    }


    [Fact]
    public async Task BackgroundReport_ReturnsQueuedWithoutGeneratingOrMailingOnTheRequest()
    {
        var provider = new BackgroundProvider();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new RequestReportHandler([provider], Substitute.For<IReportExecutionRepository>(), Substitute.For<IReportMailer>(), TenantContext(), CurrentUser(), Substitute.For<IPermissionService>(), TimeProvider.System, outbox, Substitute.For<IDataScopeService>());
        var result = await handler.Handle(new RequestReportCommand(provider.Key, null), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Queued);
        provider.Generated.ShouldBeFalse();
        outbox.Received(1).Enqueue(Arg.Is<ReportGenerationRequested>(e => e.ExecutionId == result.Value.Id && e.RequestedByUserId == UserId && e.TenantId == TenantId && e.Notify));
    }

    private sealed class BackgroundProvider : IReportProvider
    {
        public string Key => "leave.balance.detail";
        public string CategoryKey => ReportCategories.Administrative;
        public string? RequiredPermission => null;
        public string ExecutionMode => "async";
        public bool Generated { get; private set; }
        public Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string,string> parameters, CancellationToken cancellationToken)
        {
            Generated = true;
            return Task.FromResult(new ReportFileResult("detail.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1]));
        }
    }

    private sealed class FakeReportProvider(
        string key, string? requiredPermission, ReportFileResult result, string categoryKey = ReportCategories.Administrative,
        IReadOnlyList<ReportParameterDescriptor>? parameters = null) : IReportProvider
    {
        public string Key { get; } = key;

        public string CategoryKey { get; } = categoryKey;

        public string? RequiredPermission { get; } = requiredPermission;

        public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } = parameters ?? [];

        public IReadOnlyDictionary<string, string>? ReceivedParameters { get; private set; }

        public Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            ReceivedParameters = parameters;
            return Task.FromResult(result);
        }
    }
}

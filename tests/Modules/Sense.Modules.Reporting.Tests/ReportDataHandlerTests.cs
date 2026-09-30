using NSubstitute;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

public sealed class ReportDataHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static ICurrentUser CurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        return currentUser;
    }

    private static byte[] Workbook(params ExportTable[] sheets) =>
        new ExcelReportExporter().ExportToExcel("rapor.xlsx", sheets, Ct).Content;

    private static ExportTable People() => new(
        "Kişiler",
        [new ExportColumn("Ad"), new ExportColumn("Maaş", ExportColumnType.Number)],
        [["Ayşe", 30.0], ["Mehmet", 10.0], ["Zeynep", 20.0]]);

    private static ReportExecution Succeeded(byte[] content, string contentType = ExcelReportExporter.ExcelContentType, Guid? owner = null)
    {
        var execution = ReportExecution.Create(TenantId, "employee.list", null, owner ?? UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);
        execution.Complete("rapor.xlsx", contentType, content, DateTime.UtcNow);
        return execution;
    }

    private static GetReportExecutionDataHandler HandlerFor(ReportExecution execution)
    {
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(execution));
        return new GetReportExecutionDataHandler(executions, CurrentUser(), new ExcelReportTableReader());
    }

    private static Task<Result<ReportDataDto>> Run(GetReportExecutionDataHandler handler, ReportExecution execution, int sheet = 0, PagedQuery? paging = null) =>
        handler.Handle(new GetReportExecutionDataQuery(execution.Id, sheet, paging ?? new PagedQuery()), Ct);

    [Fact]
    public async Task Owner_GetsSheetsColumnsAndFirstPageOfRows()
    {
        var execution = Succeeded(Workbook(People()));

        var result = await Run(HandlerFor(execution), execution);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Sheets.Select(s => s.Name).ShouldBe(["Kişiler"]);
        result.Value.SheetIndex.ShouldBe(0);
        result.Value.Columns.Select(c => (c.Header, c.Type)).ShouldBe([("Ad", ExportColumnType.Text), ("Maaş", ExportColumnType.Number)]);
        result.Value.Rows.TotalCount.ShouldBe(3);
        result.Value.Rows.Items.Select(r => r[0]).ShouldBe(["Ayşe", "Mehmet", "Zeynep"]);
    }

    [Fact]
    public async Task SearchSortAndPaging_AreAppliedTogether()
    {
        var execution = Succeeded(Workbook(People()));
        var paging = new PagedQuery { Page = 1, PageSize = 1, Sort = "-1", Q = "e" };

        var result = await Run(HandlerFor(execution), execution, paging: paging);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.TotalCount.ShouldBe(3);
        result.Value.Rows.Items.Single()[0].ShouldBe("Ayşe");
    }

    [Fact]
    public async Task SecondSheet_IsReadWhenRequested()
    {
        var other = new ExportTable("Diğer", [new ExportColumn("X")], [["1"]]);
        var execution = Succeeded(Workbook(People(), other));

        var result = await Run(HandlerFor(execution), execution, sheet: 1);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SheetIndex.ShouldBe(1);
        result.Value.Columns.Single().Header.ShouldBe("X");
    }

    [Fact]
    public async Task HeaderOnlyReport_ReturnsColumnsAndNoRows()
    {
        var execution = Succeeded(Workbook(new ExportTable("Boş", [new ExportColumn("A")], [])));

        var result = await Run(HandlerFor(execution), execution);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Columns.Count.ShouldBe(1);
        result.Value.Rows.Items.ShouldBeEmpty();
        result.Value.Rows.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task LargeReport_IsSlicedToThePageSize()
    {
        var rows = Enumerable.Range(1, 5000).Select(i => (IReadOnlyList<object?>)new object?[] { $"kişi {i}", (double)i }).ToList();
        var execution = Succeeded(Workbook(new ExportTable("Büyük", [new ExportColumn("Ad"), new ExportColumn("No", ExportColumnType.Number)], rows)));

        var result = await Run(HandlerFor(execution), execution, paging: new PagedQuery { Page = 3, PageSize = 50 });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.TotalCount.ShouldBe(5000);
        result.Value.Rows.Items.Count.ShouldBe(50);
        result.Value.Rows.Items[0][0].ShouldBe("kişi 101");
    }

    [Fact]
    public async Task WhenNotOwner_FailsForbidden()
    {
        var execution = Succeeded(Workbook(People()), owner: Guid.NewGuid());

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.NotOwner);
    }

    [Fact]
    public async Task WhenNotYetSucceeded_FailsNotReady()
    {
        var execution = ReportExecution.Create(TenantId, "employee.list", null, UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ReportNotReady);
    }

    [Fact]
    public async Task WhenFileIsNotXlsx_FailsPreviewNotAvailable()
    {
        var execution = Succeeded([1, 2, 3], contentType: "text/csv");

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.PreviewNotAvailable);
    }

    [Fact]
    public async Task WhenXlsxIsCorrupt_FailsPreviewNotAvailable()
    {
        var execution = Succeeded([1, 2, 3]);

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.PreviewNotAvailable);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task WhenSheetOutOfRange_FailsSheetInvalid(int sheet)
    {
        var execution = Succeeded(Workbook(People()));

        var result = await Run(HandlerFor(execution), execution, sheet);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.SheetInvalid);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("-9")]
    [InlineData("ad")]
    public async Task WhenSortColumnInvalid_FailsSortColumnInvalid(string sort)
    {
        var execution = Succeeded(Workbook(People()));

        var result = await Run(HandlerFor(execution), execution, paging: new PagedQuery { Sort = sort });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.SortColumnInvalid);
    }
}

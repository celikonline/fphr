using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Application;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

public sealed class CustomReportContractTests
{
    [Fact]
    public void CatalogFiltersFieldsByPermissionAndOrdersStableKeys()
    {
        var fields = new[]
        {
            Field("employee.name", "employee.general", 20),
            Field("employee.nationalId", "employee.general", 10, "employee.sensitive.view"),
        };

        var visible = CustomReportFieldCatalog.Filter(fields, new HashSet<string>());

        visible.Select(x => x.Key).ShouldBe(["employee.name"]);
    }

    [Fact]
    public void SelectionRejectsUnknownAndDuplicateKeys()
    {
        var catalog = new[] { Field("employee.name", "employee.general", 10) };

        CustomReportSelectionValidator.Validate(catalog, ["unknown"], new HashSet<string>(), null, null).ErrorCode
            .ShouldBe(CustomReportErrors.UnknownField);
        CustomReportSelectionValidator.Validate(catalog, ["employee.name", "employee.name"], new HashSet<string>(), null, null).ErrorCode
            .ShouldBe(CustomReportErrors.DuplicateField);
    }

    [Fact]
    public void SelectionAllowsOneDetailGrainAndEmployeeGrainFields()
    {
        var catalog = new[]
        {
            Field("employee.name", "employee.general", 10),
            Field("leave.request.days", "leave.requests", 10, grain: CustomReportGrain.Detail),
        };

        var result = CustomReportSelectionValidator.Validate(catalog,
            ["employee.name", "leave.request.days"], new HashSet<string>(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        result.ErrorCode.ShouldBeNull();
        result.Fields.Select(x => x.Key).ShouldBe(["employee.name", "leave.request.days"]);
    }

    [Fact]
    public void SelectionRejectsIncompatibleDetailsAndDateRangesOverTwelveMonths()
    {
        var catalog = new[]
        {
            Field("leave.request.days", "leave.requests", 10, grain: CustomReportGrain.Detail),
            Field("expense.payment.amount", "expense.payments", 20, grain: CustomReportGrain.Detail),
        };

        CustomReportSelectionValidator.Validate(catalog,
            ["leave.request.days", "expense.payment.amount"], new HashSet<string>(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ErrorCode
            .ShouldBe(CustomReportErrors.IncompatibleDetails);

        CustomReportSelectionValidator.Validate(catalog,
            ["leave.request.days"], new HashSet<string>(), new DateOnly(2025, 12, 1), new DateOnly(2026, 12, 1)).ErrorCode
            .ShouldBe(CustomReportErrors.DateRangeInvalid);
    }

    [Fact]
    public void SelectionRejectsFieldWithoutItsCurrentPermission()
    {
        var catalog = new[] { Field("employee.nationalId", "employee.general", 10, "employee.sensitive.view") };

        CustomReportSelectionValidator.Validate(catalog, ["employee.nationalId"], new HashSet<string>(), null, null).ErrorCode
            .ShouldBe(CustomReportErrors.FieldPermissionDenied);
    }

    [Fact]
    public void CatalogAndSelectionAlsoRequireTheFieldGroupDataScopePermission()
    {
        var scopedField = Field("leave.request.days", "leave.requests", 10, scopePermission: "leave.requests.read", grain: CustomReportGrain.Detail);

        CustomReportFieldCatalog.Filter([scopedField], new HashSet<string>()).ShouldBeEmpty();
        CustomReportSelectionValidator.Validate([scopedField], [scopedField.Key], new HashSet<string>(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)).ErrorCode
            .ShouldBe(CustomReportErrors.FieldPermissionDenied);
    }

    [Fact]
    public void ComposerRepeatsEmployeeFieldsForOneSelectedDetailGroupOnly()
    {
        var employeeId = Guid.NewGuid();
        var fields = new[]
        {
            Field("employee.name", "employee.general", 10),
            Field("leave.request.days", "leave.requests", 10, grain: CustomReportGrain.Detail),
        };
        var rows = new CustomReportDataRow[]
        {
            new("employee", employeeId, null, null, new Dictionary<string, object?> { ["employee.name"] = "Ada" }),
            new(CustomReportKeys.LeaveRequests, employeeId, "leave-1", null, new Dictionary<string, object?> { ["leave.request.days"] = 2m }),
            new(CustomReportKeys.LeaveRequests, employeeId, "leave-2", null, new Dictionary<string, object?> { ["leave.request.days"] = 3m }),
        };

        var composed = CustomReportComposer.Compose(fields, rows, CustomReportKeys.LeaveRequests);

        composed.Count.ShouldBe(2);
        composed.Select(row => row.Values["employee.name"]).ShouldBe(new object?[] { "Ada", "Ada" });
        composed.Select(row => row.Values["leave.request.days"]).ShouldBe([2m, 3m]);
    }

    [Fact]
    public void ComposerRejectsResultsOverConfiguredRowLimit()
    {
        var employeeId = Guid.NewGuid();
        var fields = new[] { Field("employee.name", "employee.general", 10) };
        var rows = new[]
        {
            new CustomReportDataRow("employee", employeeId, null, null, new Dictionary<string, object?>()),
            new CustomReportDataRow("employee", Guid.NewGuid(), null, null, new Dictionary<string, object?>()),
        };

        Should.Throw<InvalidOperationException>(() => CustomReportComposer.Compose(fields, rows, null, maximumRows: 1))
            .Message.ShouldBe(CustomReportErrors.RowLimitExceeded);
    }

    private static CustomReportFieldDescriptor Field(
        string key,
        string group,
        int order,
        string? permission = null,
        CustomReportGrain grain = CustomReportGrain.Employee,
        string? scopePermission = null) =>
        new(key, group, $"reports:fields.{key}", CustomReportValueType.Text, grain, order, permission, scopePermission);
}

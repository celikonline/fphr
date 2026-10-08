using NSubstitute;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Infrastructure.Reporting;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Infrastructure;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests.Reporting;

public sealed class EmployeeCustomReportContributorTests
{
    [Fact]
    public async Task GetRows_ProjectsOnlySelectedFieldsAndPassesTheEffectiveEmployeeScope()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var visibleEmployees = new HashSet<Guid> { employeeId };
        var employees = Substitute.For<IEmployeeReportReader>();
        employees.ListAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new EmployeeReportRow(employeeId, "Ada", "Lovelace", "sensitive-id", new DateOnly(2020, 1, 1), null,
                "Active", null, null, "ada@example.com", "555", null, null, "Female", new DateOnly(1990, 1, 1), new Dictionary<Guid, Guid>())]);
        var contributor = new EmployeeCustomReportContributor(employees, Substitute.For<IOrgUnitLookup>(), Substitute.For<IStringLocalizer<SharedResource>>());

        var rows = await contributor.GetRowsAsync(new CustomReportDataRequest(
            tenantId, visibleEmployees, null, new HashSet<string> { "employee.firstName" }, null, null), CancellationToken.None);

        rows.Count.ShouldBe(1);
        rows[0].Values.ShouldBe(new Dictionary<string, object?> { ["employee.firstName"] = "Ada" });
        var employeeRead = employees.ReceivedCalls().Single().GetArguments();
        employeeRead[0].ShouldBe(tenantId);
        employeeRead[1].ShouldBe(new[] { employeeId });
        employeeRead[2].ShouldBeNull();
        employeeRead[3].ShouldBe(true);
    }

    [Fact]
    public async Task GetFields_GatesSensitivePersonalFieldsAndScopesTheyToEmployeeRead()
    {
        var contributor = new EmployeeCustomReportContributor(
            Substitute.For<IEmployeeReportReader>(), Substitute.For<IOrgUnitLookup>(), Substitute.For<IStringLocalizer<SharedResource>>());

        var fields = await contributor.GetFieldsAsync(Guid.NewGuid(), CancellationToken.None);
        var nationalId = fields.Single(field => field.Key == "employee.nationalId");
        var birthDate = fields.Single(field => field.Key == "employee.birthDate");

        nationalId.RequiredPermission.ShouldBe("employee.sensitive.view");
        nationalId.ScopePermission.ShouldBe("employee.employees.read");
        birthDate.RequiredPermission.ShouldBe("employee.sensitive.view");
    }
}

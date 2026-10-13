using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>
/// PAY-INCENTIVE-3 read models: the APHB teşvik kanunu kodu column and the pre-fill data (assignment's declaration code wins, even for an
/// unapproved definition; otherwise the profile code), and the two-source incentive summary without double counting.
/// </summary>
public sealed class IncentiveReadModelTests
{
    private static readonly DateOnly March = new(2026, 3, 1);
    private static readonly Guid TenantId = IncentiveServiceHarness.TenantId;

    private static IncentiveDefinition Define(FakeIncentiveDefinitionRepository definitions, string code, IncentiveKind kind, string? declarationCode, bool approved)
    {
        var parameters = kind switch
        {
            IncentiveKind.DeclarationOnly => IncentiveParameters.Empty,
            IncentiveKind.EmployerShareCoverage => new IncentiveParameters
            {
                Rate = IncentiveRate.Literal(1m),
                CoverageBase = IncentiveCoverageBase.ActualSgkBase,
                EmployerSgkRate = IncentiveRate.Literal(0.155m),
                CoversEmployerSgk = true,
            },
            _ => new IncentiveParameters { Rate = IncentiveRate.Literal(0.1m) },
        };
        var definition = IncentiveDefinition.Create(
            TenantId, code, new IncentiveDefinitionData(code + " adı", null, declarationCode, kind, parameters, null, new DateOnly(2026, 1, 1), null)).Value;
        if (approved)
        {
            definition.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow, null).IsSuccess.ShouldBeTrue();
        }

        definitions.Add(definition);
        return definition;
    }

    private static void Assign(FakeIncentiveAssignmentRepository assignments, IncentiveDefinition definition, Guid employeeId) =>
        assignments.Add(EmployeeIncentiveAssignment.Create(
            TenantId, employeeId, definition, March, null, null, new HashSet<DateOnly>(), [], _ => definition.Category).Value);

    // ---- APHB ----

    [Fact]
    public async Task TheAphbIncentiveCodeColumn_IsTheAssignmentsDeclarationCode_EvenForAnUnapprovedDefinition_ElseTheProfileCode()
    {
        var harness = IncentiveServiceHarness.Create();
        var assignedUnapproved = harness.AddEmployee("05510");
        var profileOnly = harness.AddEmployee("05510");
        var none = harness.AddEmployee();
        var taxOnly = harness.AddEmployee("05510");
        Assign(harness.Assignments, Define(harness.Definitions, "SGK_6111_TASLAK", IncentiveKind.EmployerShareCoverage, "06111", approved: false), assignedUnapproved);
        Assign(harness.Assignments, Define(harness.Definitions, "ARGE_5746_GV", IncentiveKind.IncomeTaxWithholdingRelief, null, approved: true), taxOnly);
        await harness.RunAsync(assignedUnapproved, profileOnly, none, taxOnly);

        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(harness.Period.Id, Arg.Any<CancellationToken>()).Returns(harness.Period);
        var employees = new[] { assignedUnapproved, profileOnly, none, taxOnly };
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByPeriodAsync(harness.Period.Id, Arg.Any<CancellationToken>()).Returns(employees.Select(harness.PayslipOf).ToList());
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        traces.FindLatestByPayslipIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => employees.Select(harness.TraceOf).Single(t => t.PayslipId == call.Arg<Guid>()));
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<Guid>>(1).Select(id => new EmployeeContact(id, "Ad", id.ToString(), null, null, null, "Active")).ToList());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var handler = new ExportAphbFileHandler(periods, payslips, traces, harness.Profiles, directory, tenant, harness.Assignments, harness.Definitions);

        var result = await handler.Handle(new ExportAphbFileQuery(harness.Period.Id), CancellationToken.None);

        var lines = System.Text.Encoding.UTF8.GetString(result.Value.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[1].ShouldBe(AphbFileBuilder.Header); // the header is unchanged
        string CodeOf(Guid id) => lines.Single(l => l.Contains(id.ToString())).Split(';')[^1];
        CodeOf(assignedUnapproved).ShouldBe("06111");
        CodeOf(profileOnly).ShouldBe("05510");
        CodeOf(none).ShouldBe(string.Empty);
        CodeOf(taxOnly).ShouldBe("05510"); // a tax-category assignment carries no SGK code: the profile code stays
    }

    [Fact]
    public async Task TheAphbPrefillCode_FollowsTheSameResolution()
    {
        var employee = Guid.NewGuid();
        var other = Guid.NewGuid();
        var definitions = new FakeIncentiveDefinitionRepository();
        var assignments = new FakeIncentiveAssignmentRepository();
        Assign(assignments, Define(definitions, "SGK_17103", IncentiveKind.EmployerShareCoverage, "17103", approved: false), employee);
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.FindCurrentAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => IncentiveServiceHarness.Profile(call.Arg<Guid>(), "05510"));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var handler = new GetAphbPrefillDataHandler(tenant, profiles, Substitute.For<ILocationLookup>(), Substitute.For<IEmployeeLookup>(), assignments, definitions);

        var assigned = await handler.Handle(new GetAphbPrefillDataQuery(employee, March), CancellationToken.None);
        var profileOnly = await handler.Handle(new GetAphbPrefillDataQuery(other, March), CancellationToken.None);

        assigned.Value.IncentiveLawCode.ShouldBe("17103");
        profileOnly.Value.IncentiveLawCode.ShouldBe("05510");
    }

    // ---- summary ----

    [Fact]
    public async Task TheSummary_CountsProfileCodesOnlyForEmployeesWithoutAnSgkAssignment_AndAssignmentsPerDefinition()
    {
        var definitions = new FakeIncentiveDefinitionRepository();
        var assignments = new FakeIncentiveAssignmentRepository();
        var sgkApproved = Define(definitions, "SGK_6111", IncentiveKind.EmployerShareCoverage, "06111", approved: true);
        var sgkDraft = Define(definitions, "SGK_17103_TASLAK", IncentiveKind.EmployerShareCoverage, "17103", approved: false);
        var tax = Define(definitions, "ARGE_5746_GV", IncentiveKind.IncomeTaxWithholdingRelief, null, approved: true);
        var profileOnly = Guid.NewGuid();
        var profileAndAssignment = Guid.NewGuid(); // counted once, under the assignment (the profile code is ignored)
        var assignmentOnly = Guid.NewGuid();
        var draftAssigned = Guid.NewGuid();
        var taxAndProfile = Guid.NewGuid(); // a tax assignment does not replace the profile code
        Assign(assignments, sgkApproved, profileAndAssignment);
        Assign(assignments, sgkApproved, assignmentOnly);
        Assign(assignments, sgkDraft, draftAssigned);
        Assign(assignments, tax, taxAndProfile);
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.ListActiveAsOfAsync(March, Arg.Any<CancellationToken>()).Returns(new List<Domain.Profiles.EmployeePayrollProfile>
        {
            IncentiveServiceHarness.Profile(profileOnly, "05510"),
            IncentiveServiceHarness.Profile(profileAndAssignment, "05510"),
            IncentiveServiceHarness.Profile(taxAndProfile, "05510"),
            IncentiveServiceHarness.Profile(assignmentOnly),
        });
        var handler = new GetActiveIncentiveSummaryHandler(profiles, assignments, definitions);

        var result = await handler.Handle(new GetActiveIncentiveSummaryQuery(March), CancellationToken.None);

        result.Value.ShouldBe(
        [
            new IncentiveSummaryItemDto("05510", 2), // profileOnly + taxAndProfile; profileAndAssignment is counted under the assignment
            new IncentiveSummaryItemDto("06111", 2, "SGK_6111 adı", "assignment", true),
            new IncentiveSummaryItemDto("17103", 1, "SGK_17103_TASLAK adı", "assignment", false),
            new IncentiveSummaryItemDto("ARGE_5746_GV", 1, "ARGE_5746_GV adı", "assignment", true),
        ]);
        result.Value[0].Source.ShouldBe("profile");
        result.Value[0].Name.ShouldBeNull();
        result.Value[0].IsSmmmApproved.ShouldBeNull();
    }

    [Fact]
    public void TheSummaryItem_OmitsTheNullFieldsWhenSerialized_AndKeepsTheSourceForProfileItems()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        System.Text.Json.JsonSerializer.Serialize(new IncentiveSummaryItemDto("05510", 2), options).ShouldBe("""{"code":"05510","employeeCount":2,"source":"profile"}""");
        System.Text.Json.JsonSerializer.Serialize(new IncentiveSummaryItemDto("06111", 1, "Ad", "assignment", true), options)
            .ShouldBe("""{"code":"06111","employeeCount":1,"name":"Ad","source":"assignment","isSmmmApproved":true}""");
    }
}

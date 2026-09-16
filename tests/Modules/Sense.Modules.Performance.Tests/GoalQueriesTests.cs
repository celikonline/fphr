using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Application.Goals;
using Sense.Modules.Performance.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

public sealed class GoalQueriesTests
{
    private static readonly DateOnly TargetDate = new(2026, 12, 31);

    [Fact]
    public async Task ListTeamGoals_ResolvesEmployeeName()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var goal = Goal.Create(tenantId, employeeId, "Satışları %10 artır", null, TargetDate, null).Value;

        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.GetVisibleEmployeeIdsAsync(PerformancePermissions.TeamGoals.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { employeeId });

        var goals = Substitute.For<IGoalRepository>();
        goals.ListByEmployeeIdsAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns([goal]);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var employees = Substitute.For<IEmployeeDirectory>();
        employees.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(employeeId, "Ayşe", "Yılmaz", null, null, null, "Active")]);

        var handler = new ListTeamGoalsHandler(dataScope, goals, tenant, employees);

        var result = await handler.Handle(new ListTeamGoalsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem();
        result.Value[0].EmployeeName.ShouldBe("Ayşe Yılmaz");
    }

    [Fact]
    public async Task ListTeamGoals_NoVisibleEmployees_ReturnsEmptyWithoutCallingDirectory()
    {
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.GetVisibleEmployeeIdsAsync(PerformancePermissions.TeamGoals.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var goals = Substitute.For<IGoalRepository>();
        var tenant = Substitute.For<ITenantContext>();
        var employees = Substitute.For<IEmployeeDirectory>();

        var handler = new ListTeamGoalsHandler(dataScope, goals, tenant, employees);

        var result = await handler.Handle(new ListTeamGoalsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        await employees.DidNotReceive().GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMyGoals_DoesNotPopulateEmployeeName()
    {
        var employeeId = Guid.NewGuid();
        var goal = Goal.Create(Guid.NewGuid(), employeeId, "Satışları %10 artır", null, TargetDate, null).Value;

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var goals = Substitute.For<IGoalRepository>();
        goals.ListByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns([goal]);

        var handler = new ListMyGoalsHandler(currentUser, goals);

        var result = await handler.Handle(new ListMyGoalsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value[0].EmployeeName.ShouldBeNull();
    }
}

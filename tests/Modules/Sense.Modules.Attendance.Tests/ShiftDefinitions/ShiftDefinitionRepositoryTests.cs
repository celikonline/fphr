using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.ShiftDefinitions.ShiftDefinitionFixtures;

namespace Sense.Modules.Attendance.Tests.ShiftDefinitions;

/// <summary>
/// Gerçek PostgreSQL: "geçmişe uygula" etki alanı sorgusu ve FK'sız kullanım denetimi (atama sütunu + rotasyon adımları / plan
/// şablonu hücreleri JSON koleksiyonları).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ShiftDefinitionRepositoryTests : AttendancePostgresFixture
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    private async Task SeedAsync(Guid tenant, params object[] entities)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private IShiftDefinitionRepository Repository(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IShiftDefinitionRepository>();

    private static ShiftAssignment Assignment(Guid tenant, Guid employee, DateOnly date, Guid? definitionId)
    {
        var assignment = ShiftAssignment.Create(tenant, employee, date, new TimeOnly(9, 0), new TimeOnly(18, 0), 60, false, null).Value;
        assignment.SetPlan(definitionId, ShiftDayType.Work);
        return assignment;
    }

    [Fact]
    public async Task Assignments_using_definitions_are_filtered_by_definition_and_date_range()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenant);
        var used = ShiftDefinition.Create(tenant, FixedValues("SAB", Minute(9), Minute(18))).Value;
        var other = ShiftDefinition.Create(tenant, FixedValues("AKS", Minute(14), Minute(22))).Value;
        await SeedAsync(
            tenant, used, other,
            Assignment(tenant, employee, Day, used.Id),
            Assignment(tenant, employee, Day.AddDays(1), other.Id),
            Assignment(tenant, employee, Day.AddDays(2), null),
            Assignment(tenant, employee, Day.AddDays(10), used.Id));

        using var scope = ServiceProvider!.CreateScope();
        var found = await Repository(scope).ListAssignmentsUsingAsync([used.Id], Day, Day.AddDays(5), ct);

        found.ShouldHaveSingleItem().Date.ShouldBe(Day);
        (await Repository(scope).ListAssignmentsUsingAsync([], Day, Day.AddDays(30), ct)).ShouldBeEmpty();
        (await Repository(scope).ListAssignmentsUsingAsync([used.Id, other.Id], Day, Day.AddDays(30), ct)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Definition_is_in_use_when_referenced_by_assignment_rotation_step_or_template_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenant);
        var byAssignment = ShiftDefinition.Create(tenant, FixedValues("A1", Minute(9), Minute(18))).Value;
        var byRotation = ShiftDefinition.Create(tenant, FixedValues("R1", Minute(6), Minute(14))).Value;
        var byTemplate = ShiftDefinition.Create(tenant, FixedValues("T1", Minute(14), Minute(22))).Value;
        var unused = ShiftDefinition.Create(tenant, FixedValues("U1", Minute(22), Minute(6))).Value;
        var pattern = RotationPattern.Create(
            tenant, "2X12", "İki vardiya", RotationPreset.Custom, 2,
            [RotationStep.Of(0, byRotation.Id, ShiftDayType.Work), RotationStep.Of(1, null, ShiftDayType.WeeklyRest)]).Value;
        var template = ShiftPlanTemplate.Create(
            tenant, "Hafta", 7, [ShiftPlanTemplateEntry.Of(employee, 2, byTemplate.Id, ShiftDayType.Work)], Guid.NewGuid()).Value;
        await SeedAsync(tenant, byAssignment, byRotation, byTemplate, unused, pattern, template, Assignment(tenant, employee, Day, byAssignment.Id));

        using var scope = ServiceProvider!.CreateScope();
        var repository = Repository(scope);

        (await repository.IsInUseAsync(byAssignment.Id, ct)).ShouldBeTrue();
        (await repository.IsInUseAsync(byRotation.Id, ct)).ShouldBeTrue();
        (await repository.IsInUseAsync(byTemplate.Id, ct)).ShouldBeTrue();
        (await repository.IsInUseAsync(unused.Id, ct)).ShouldBeFalse();
    }
}

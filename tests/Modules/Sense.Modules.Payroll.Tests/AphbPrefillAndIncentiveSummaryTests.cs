using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// senseik-010/F-03: <see cref="GetAphbPrefillDataHandler"/> proves the three APHB/e-Bildirge pre-fill fields (SGK
/// işyeri sicil no from Organization, SGK meslek kodu from Employee, teşvik kodu from this module's own profile) are
/// queryable together for one employee; <see cref="GetActiveIncentiveSummaryHandler"/> proves the tenant-wide,
/// read-only "aktif teşvik türleri" summary the Organizasyon/Şirket profil ekranı displays (handoff §3: no writable
/// field added there, the data stays employee-scoped).
/// </summary>
public sealed class AphbPrefillAndIncentiveSummaryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly Guid SgkWorkplaceCompanyId = Guid.NewGuid();
    private static readonly DateOnly AsOf = new(2026, 9, 19);

    private static EmployeePayrollProfileData Data(Guid? locationId, string? incentiveLawCode) =>
        new(
            SgkWorkplaceCompanyId, locationId,
            new DateOnly(2026, 1, 1), null,
            WageBasis.Monthly, SgkStatus.Normal, incentiveLawCode,
            MealBenefitVariant.None, 0m,
            TransportVariant.None, PayrollPaymentMethod.Bank, null,
            SickDaysReducePay: false, ExternalPayrollCode: null, Notes: null);

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static IEmployeeIncentiveAssignmentRepository NoAssignments() => Substitute.For<IEmployeeIncentiveAssignmentRepository>();

    private static IIncentiveDefinitionRepository NoDefinitions() => Substitute.For<IIncentiveDefinitionRepository>();

    [Fact]
    public async Task GetAphbPrefillDataHandler_ComposesAllThreeFields_WhenProfileHasWorkplaceAndIncentive()
    {
        var profile = EmployeePayrollProfile.Create(TenantId, EmployeeId, Data(LocationId, "5510")).Value;
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.FindCurrentAsync(EmployeeId, AsOf, Arg.Any<CancellationToken>()).Returns(profile);

        var locations = Substitute.For<ILocationLookup>();
        locations.GetSgkIsyeriSicilNoAsync(TenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(LocationId)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [LocationId] = "1071234.567-8.9-0" });

        var employees = Substitute.For<IEmployeeLookup>();
        employees.GetSgkOccupationCodeAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns("3121.03");

        var handler = new GetAphbPrefillDataHandler(Tenant(), profiles, locations, employees, NoAssignments(), NoDefinitions());
        var result = await handler.Handle(new GetAphbPrefillDataQuery(EmployeeId, AsOf), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SgkIsyeriSicilNo.ShouldBe("1071234.567-8.9-0");
        result.Value.SgkOccupationCode.ShouldBe("3121.03");
        result.Value.IncentiveLawCode.ShouldBe("5510");
    }

    [Fact]
    public async Task GetAphbPrefillDataHandler_NoProfileOrWorkplace_ReturnsNullsWithoutFailing()
    {
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.FindCurrentAsync(EmployeeId, AsOf, Arg.Any<CancellationToken>()).Returns((EmployeePayrollProfile?)null);

        var locations = Substitute.For<ILocationLookup>();
        var employees = Substitute.For<IEmployeeLookup>();
        employees.GetSgkOccupationCodeAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var handler = new GetAphbPrefillDataHandler(Tenant(), profiles, locations, employees, NoAssignments(), NoDefinitions());
        var result = await handler.Handle(new GetAphbPrefillDataQuery(EmployeeId, AsOf), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SgkIsyeriSicilNo.ShouldBeNull();
        result.Value.SgkOccupationCode.ShouldBeNull();
        result.Value.IncentiveLawCode.ShouldBeNull();
        await locations.DidNotReceive().GetSgkIsyeriSicilNoAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveIncentiveSummaryHandler_GroupsByCode_AndSkipsProfilesWithoutIncentive()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var employeeC = Guid.NewGuid();
        var withIncentiveA = EmployeePayrollProfile.Create(TenantId, employeeA, Data(null, "5510")).Value;
        var withIncentiveB = EmployeePayrollProfile.Create(TenantId, employeeB, Data(null, "5510")).Value;
        var withIncentiveC = EmployeePayrollProfile.Create(TenantId, employeeC, Data(null, "6111")).Value;
        var withoutIncentive = EmployeePayrollProfile.Create(TenantId, Guid.NewGuid(), Data(null, null)).Value;

        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.ListActiveAsOfAsync(AsOf, Arg.Any<CancellationToken>())
            .Returns(new List<EmployeePayrollProfile> { withIncentiveA, withIncentiveB, withIncentiveC, withoutIncentive });

        var handler = new GetActiveIncentiveSummaryHandler(profiles, NoAssignments(), NoDefinitions());
        var result = await handler.Handle(new GetActiveIncentiveSummaryQuery(AsOf), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([new IncentiveSummaryItemDto("5510", 2), new IncentiveSummaryItemDto("6111", 1)]);
    }
}

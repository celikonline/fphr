using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// OrgChartBuilder.Build resolving the new DepartmentName field (H-04: two same-titled employees at Headquarters
/// were indistinguishable in the org chart tree; a Department-level line under the title fixes that without
/// touching the underlying data model - see EmployeeQueries.cs BuildNode/OrgChartNodeDto).
/// </summary>
public sealed class OrgChartBuilderTests
{
    private static readonly Guid CompanyTypeId = Guid.NewGuid();
    private static readonly Guid DepartmentTypeId = Guid.NewGuid();
    private static readonly Guid TitleTypeId = Guid.NewGuid();

    private static OrgPlacement BuildPlacement(IReadOnlyList<OrgUnitInfo> units) =>
        new(
            [
                new OrgUnitTypeInfo(CompanyTypeId, "Company", "Şirket", 0),
                new OrgUnitTypeInfo(DepartmentTypeId, "Department", "Departman", 1),
                new OrgUnitTypeInfo(TitleTypeId, "Title", "Ünvan", 2),
            ],
            units);

    private static EmployeeRow Row(Guid id, string displayName, Guid? orgUnitId, Guid? managerId = null) =>
        new(
            Id: id,
            FirstName: displayName,
            LastName: string.Empty,
            DisplayName: displayName,
            NationalId: null,
            WorkEmail: null,
            PersonalEmail: null,
            WorkPhone: null,
            HireDate: null,
            BirthDate: null,
            Status: EmployeeStatus.Active,
            OrgUnitId: orgUnitId,
            ManagerEmployeeId: managerId,
            UserId: null);

    [Fact]
    public void Build_ResolvesDepartmentName_ForAnEmployeePlacedUnderADepartment()
    {
        var headquartersId = Guid.NewGuid();
        var hrDepartmentId = Guid.NewGuid();
        var hrTitleId = Guid.NewGuid();
        var placement = BuildPlacement(
        [
            new OrgUnitInfo(headquartersId, CompanyTypeId, null, "Headquarters"),
            new OrgUnitInfo(hrDepartmentId, DepartmentTypeId, headquartersId, "HR"),
            new OrgUnitInfo(hrTitleId, TitleTypeId, hrDepartmentId, "İK Uzmanı"),
        ]);

        var rows = new[] { Row(Guid.NewGuid(), "Deniz Balcı", hrTitleId) };

        var chart = OrgChartBuilder.Build(rows, placement);

        chart.Count.ShouldBe(1);
        chart[0].TitleName.ShouldBe("İK Uzmanı");
        chart[0].DepartmentName.ShouldBe("HR");
        chart[0].CompanyName.ShouldBe("Headquarters");
    }

    [Fact]
    public void Build_LeavesDepartmentNameNull_ForAnExecutivePlacedDirectlyUnderHeadquarters()
    {
        // Owner/Admin/first-HR demo employees sit on a Title unit directly under the Company-level Headquarters
        // unit - there is no Department level in their chain, so the new field must stay null rather than fall
        // back to the company name (that would duplicate the existing company chip already shown for the node).
        var headquartersId = Guid.NewGuid();
        var ownerTitleId = Guid.NewGuid();
        var placement = BuildPlacement(
        [
            new OrgUnitInfo(headquartersId, CompanyTypeId, null, "Headquarters"),
            new OrgUnitInfo(ownerTitleId, TitleTypeId, headquartersId, "Genel Müdür"),
        ]);

        var rows = new[] { Row(Guid.NewGuid(), "Ahmet Yılmaz", ownerTitleId) };

        var chart = OrgChartBuilder.Build(rows, placement);

        chart[0].TitleName.ShouldBe("Genel Müdür");
        chart[0].DepartmentName.ShouldBeNull();
        chart[0].CompanyName.ShouldBe("Headquarters");
    }

    [Fact]
    public void Build_DifferentiatesTwoEmployeesWithTheSameTitle_ByDepartmentName()
    {
        // Regression for H-04: two employees who end up with the same title text are still distinguishable once
        // the tree shows which department each one actually belongs to.
        var headquartersId = Guid.NewGuid();
        var hrDepartmentId = Guid.NewGuid();
        var salesDepartmentId = Guid.NewGuid();
        var hrTitleId = Guid.NewGuid();
        var salesTitleId = Guid.NewGuid();
        var placement = BuildPlacement(
        [
            new OrgUnitInfo(headquartersId, CompanyTypeId, null, "Headquarters"),
            new OrgUnitInfo(hrDepartmentId, DepartmentTypeId, headquartersId, "HR"),
            new OrgUnitInfo(salesDepartmentId, DepartmentTypeId, headquartersId, "Sales"),
            new OrgUnitInfo(hrTitleId, TitleTypeId, hrDepartmentId, "Uzman"),
            new OrgUnitInfo(salesTitleId, TitleTypeId, salesDepartmentId, "Uzman"),
        ]);

        var rows = new[]
        {
            Row(Guid.NewGuid(), "Ayşe Demir", hrTitleId),
            Row(Guid.NewGuid(), "Deniz Balcı", salesTitleId),
        };

        var chart = OrgChartBuilder.Build(rows, placement);

        chart.Count.ShouldBe(2);
        chart.ShouldAllBe(n => n.TitleName == "Uzman");
        chart.Select(n => n.DepartmentName).ShouldBe(["HR", "Sales"], ignoreOrder: true);
    }
}

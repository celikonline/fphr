using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

public sealed class OrgUnitTypeHierarchyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void AddCustom_InsertsAfterGivenLevel_AndRenumbersActiveThenInactive()
    {
        var types = Catalog();
        var hierarchy = new OrgUnitTypeHierarchy(types);

        var first = hierarchy.AddCustom(TenantId, "  Proje Grubu ", ByCode(types, OrgUnitTypeCodes.Department).Id);
        first.IsSuccess.ShouldBeTrue();
        first.Value.Code.ShouldBe("Custom_1");
        first.Value.Name.ShouldBe("Proje Grubu");
        first.Value.IsSystem.ShouldBeFalse();

        var second = hierarchy.AddCustom(TenantId, "Ekip", insertAfterTypeId: null);
        second.Value.Code.ShouldBe("Custom_2");

        Codes(hierarchy).ShouldBe(["Company:1", "Branch:2", "Department:3", "Custom_1:4", "Title:5", "Custom_2:6", "Team:7", "Project:8", "ApprovalUnit:9"]);
        hierarchy.Active.Select(t => t.SortOrder).ShouldBe([1, 2, 3, 4, 5, 6]);
    }

    [Fact]
    public void AddCustom_RejectsInactivePositionInvalidNameAndLevelCap()
    {
        var types = Catalog();
        var hierarchy = new OrgUnitTypeHierarchy(types);

        hierarchy.AddCustom(TenantId, "X", ByCode(types, OrgUnitTypeCodes.Project).Id).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeInsertPositionInvalid);
        hierarchy.AddCustom(TenantId, " ", null).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeNameRequired);
        hierarchy.AddCustom(TenantId, new string('a', OrganizationLimits.NameMaxLength + 1), null).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeNameTooLong);

        for (var i = types.Count; i < OrganizationLimits.MaxUnitTypesDefault; i++)
        {
            hierarchy.AddCustom(TenantId, $"Level {i}", null).IsSuccess.ShouldBeTrue();
        }

        var capped = hierarchy.AddCustom(TenantId, "One too many", null);
        capped.Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeLimitReached);
        capped.Error.Type.ShouldBe(Sense.Shared.Kernel.Results.ErrorType.Rule);
        hierarchy.Types.Count.ShouldBe(OrganizationLimits.MaxUnitTypesDefault);
    }

    [Fact]
    public void Activate_PlacesInactiveLevelAtPosition_AndRejectsActiveLevel()
    {
        var types = Catalog();
        var hierarchy = new OrgUnitTypeHierarchy(types);

        hierarchy.Activate(ByCode(types, OrgUnitTypeCodes.Project).Id, ByCode(types, OrgUnitTypeCodes.Company).Id).IsSuccess.ShouldBeTrue();

        Codes(hierarchy).ShouldBe(["Company:1", "Project:2", "Branch:3", "Department:4", "Title:5", "Team:6", "ApprovalUnit:7"]);
        hierarchy.Activate(ByCode(types, OrgUnitTypeCodes.Branch).Id, null).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeAlreadyActive);
        hierarchy.Activate(Guid.NewGuid(), null).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeNotFound);
    }

    [Fact]
    public void Reorder_RequiresExactActiveSetWithCompanyFirst()
    {
        var types = Catalog();
        var hierarchy = new OrgUnitTypeHierarchy(types);
        var company = ByCode(types, OrgUnitTypeCodes.Company).Id;
        var branch = ByCode(types, OrgUnitTypeCodes.Branch).Id;
        var department = ByCode(types, OrgUnitTypeCodes.Department).Id;
        var title = ByCode(types, OrgUnitTypeCodes.Title).Id;

        hierarchy.Reorder([branch, company, department, title]).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeCompanyMustBeFirst);
        hierarchy.Reorder([company, department, title]).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeOrderInvalid);
        hierarchy.Reorder([company, department, department, title]).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeOrderInvalid);
        hierarchy.Reorder([company, department, title, ByCode(types, OrgUnitTypeCodes.Team).Id]).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeOrderInvalid);

        hierarchy.Reorder([company, title, department, branch]).IsSuccess.ShouldBeTrue();
        Codes(hierarchy).ShouldBe(["Company:1", "Title:2", "Department:3", "Branch:4", "Team:5", "Project:6", "ApprovalUnit:7"]);
    }

    [Fact]
    public void Remove_KeepsCompanyImmutable_AndClosesGap()
    {
        var types = Catalog();
        var hierarchy = new OrgUnitTypeHierarchy(types);

        var company = ByCode(types, OrgUnitTypeCodes.Company);
        hierarchy.Remove(company.Id).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeSystemImmutable);
        company.IsActive.ShouldBeTrue();

        var branch = ByCode(types, OrgUnitTypeCodes.Branch);
        hierarchy.Remove(branch.Id).IsSuccess.ShouldBeTrue();
        branch.IsActive.ShouldBeFalse();
        Codes(hierarchy).ShouldBe(["Company:1", "Department:2", "Title:3", "Branch:4", "Team:5", "Project:6", "ApprovalUnit:7"]);

        hierarchy.Remove(branch.Id).Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeAlreadyInactive);
    }

    [Fact]
    public void Rename_ValidatesName_AndAllowsSystemLevels()
    {
        var company = ByCode(Catalog(), OrgUnitTypeCodes.Company);

        company.Rename(" Holding ").IsSuccess.ShouldBeTrue();
        company.Name.ShouldBe("Holding");
        company.Rename("").Error.Code.ShouldBe(OrganizationDomainErrors.OrgUnitTypeNameRequired);
        company.Name.ShouldBe("Holding");
    }

    [Fact]
    public void OccurrencesBetween_ProjectsRecurringEntriesIntoEveryYearOfTheWindow()
    {
        var calendarId = Guid.NewGuid();
        var newYear = HolidayCalendarEntry.Create(TenantId, calendarId, new DateOnly(2000, 1, 1), "Yılbaşı", isRecurringAnnually: true).Value;
        var leapDay = HolidayCalendarEntry.Create(TenantId, calendarId, new DateOnly(2000, 2, 29), "Artık gün", isRecurringAnnually: true).Value;
        var oneOff = HolidayCalendarEntry.Create(TenantId, calendarId, new DateOnly(2027, 3, 20), "Bayram", isRecurringAnnually: false).Value;

        newYear.OccurrencesBetween(new DateOnly(2026, 12, 1), new DateOnly(2028, 1, 1)).ShouldBe([new DateOnly(2027, 1, 1), new DateOnly(2028, 1, 1)]);
        newYear.OccurrencesBetween(new DateOnly(2026, 1, 2), new DateOnly(2026, 12, 31)).ShouldBeEmpty();
        leapDay.OccurrencesBetween(new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31)).ShouldBe([new DateOnly(2028, 2, 29)]);
        oneOff.OccurrencesBetween(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 20)).ShouldBe([new DateOnly(2027, 3, 20)]);
        oneOff.OccurrencesBetween(new DateOnly(2027, 3, 21), new DateOnly(2028, 3, 20)).ShouldBeEmpty();
    }

    private static List<OrgUnitType> Catalog() => OrgUnitTypeCatalog.CreateAll(TenantId).Select(r => r.Value).ToList();

    private static OrgUnitType ByCode(IEnumerable<OrgUnitType> types, string code) => types.Single(t => t.Code == code);

    private static List<string> Codes(OrgUnitTypeHierarchy hierarchy) => hierarchy.Types.Select(t => $"{t.Code}:{t.SortOrder}").ToList();
}

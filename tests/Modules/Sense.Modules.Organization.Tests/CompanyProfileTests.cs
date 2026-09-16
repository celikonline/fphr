using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Companies;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Unit tests for the Company aggregate's Faz 2 profile additions (TB-10 "Şirket profili"): legal/registry
/// fields, working hours/days and logo reference. See <see cref="Company"/> remarks for why these live on
/// their own small aggregate rather than on OrgUnit or Identity.Tenant.
/// </summary>
public sealed class CompanyProfileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrgUnitId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidOrgUnit_Succeeds()
    {
        var result = Company.Create(TenantId, OrgUnitId);

        result.IsSuccess.ShouldBeTrue();
        var company = result.Value;
        company.OrgUnitId.ShouldBe(OrgUnitId);
        company.TenantId.ShouldBe(TenantId);
        company.HolidayCalendarId.ShouldBeNull();
        company.WorkingDays.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithEmptyOrgUnitId_Fails()
    {
        var result = Company.Create(TenantId, Guid.Empty);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CompanyOrgUnitRequired);
    }

    [Fact]
    public void UpdateProfile_WithValidData_TrimsAndSetsFields()
    {
        var company = Company.Create(TenantId, OrgUnitId).Value;

        company.UpdateProfile(
            "  1234567  ",
            "  9876543210  ",
            "  Maslak, İstanbul  ",
            "  İstanbul  ",
            "  Türkiye  ",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            ["mon", "tue", "wed", "thu", "fri"]);

        company.RegistrationNumber.ShouldBe("1234567");
        company.TaxNumber.ShouldBe("9876543210");
        company.Address.ShouldBe("Maslak, İstanbul");
        company.City.ShouldBe("İstanbul");
        company.Country.ShouldBe("Türkiye");
        company.OperatingHoursStart.ShouldBe(new TimeOnly(9, 0));
        company.OperatingHoursEnd.ShouldBe(new TimeOnly(18, 0));
        company.WorkingDays.ShouldBe(["mon", "tue", "wed", "thu", "fri"]);
        company.WorkingDaysCsv.ShouldBe("mon,tue,wed,thu,fri");
    }

    [Fact]
    public void UpdateProfile_WithBlankOptionalFields_StoresNulls()
    {
        var company = Company.Create(TenantId, OrgUnitId).Value;
        company.UpdateProfile("123", "456", "addr", "city", "country", null, null, ["mon"]);

        company.UpdateProfile("   ", "   ", "   ", "   ", "   ", null, null, null);

        company.RegistrationNumber.ShouldBeNull();
        company.TaxNumber.ShouldBeNull();
        company.Address.ShouldBeNull();
        company.City.ShouldBeNull();
        company.Country.ShouldBeNull();
        company.WorkingDaysCsv.ShouldBeNull();
        company.WorkingDays.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateProfile_WithEmptyWorkingDaysList_StoresNullCsv()
    {
        var company = Company.Create(TenantId, OrgUnitId).Value;

        company.UpdateProfile(null, null, null, null, null, null, null, []);

        company.WorkingDaysCsv.ShouldBeNull();
        company.WorkingDays.ShouldBeEmpty();
    }

    [Fact]
    public void SetLogo_WithDocumentAndUrl_SetsBothFields()
    {
        var company = Company.Create(TenantId, OrgUnitId).Value;
        var documentId = Guid.NewGuid();

        company.SetLogo(documentId, "  blob:https://example/logo.png  ");

        company.LogoDocumentId.ShouldBe(documentId);
        company.LogoUrl.ShouldBe("blob:https://example/logo.png");
    }

    [Fact]
    public void SetLogo_WithNullDocumentId_ClearsLogo()
    {
        var company = Company.Create(TenantId, OrgUnitId).Value;
        company.SetLogo(Guid.NewGuid(), "blob:https://example/logo.png");

        company.SetLogo(null, null);

        company.LogoDocumentId.ShouldBeNull();
        company.LogoUrl.ShouldBeNull();
    }

    [Fact]
    public void SetJointEmploymentGroupCode_TrimsValue()
    {
        var company = Company.Create(TenantId, OrgUnitId).Value;

        company.SetJointEmploymentGroupCode("  GROUP-A  ");

        company.JointEmploymentGroupCode.ShouldBe("GROUP-A");
    }

    [Fact]
    public void SetJointEmploymentGroupCode_WithBlankValue_ClearsIt()
    {
        var company = Company.Create(TenantId, OrgUnitId).Value;
        company.SetJointEmploymentGroupCode("GROUP-A");

        company.SetJointEmploymentGroupCode("   ");

        company.JointEmploymentGroupCode.ShouldBeNull();
    }
}

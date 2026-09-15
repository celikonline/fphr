using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Locations;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>Unit tests for the Location aggregate (roadmap Faz 0 item #6: office/store/warehouse/factory/remote + country).</summary>
public sealed class LocationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = Location.Create(TenantId, "İstanbul HQ", LocationType.Office, "TR", "Maslak, İstanbul");

        result.IsSuccess.ShouldBeTrue();
        var location = result.Value;
        location.Name.ShouldBe("İstanbul HQ");
        location.Type.ShouldBe(LocationType.Office);
        location.CountryCode.ShouldBe("TR");
        location.Address.ShouldBe("Maslak, İstanbul");
        location.IsActive.ShouldBeTrue();
        location.TenantId.ShouldBe(TenantId);
    }

    [Fact]
    public void Create_WithoutAddress_SucceedsWithNullAddress()
    {
        var result = Location.Create(TenantId, "Remote", LocationType.Remote, "TR", address: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Address.ShouldBeNull();
    }

    [Fact]
    public void Create_TrimsNameAndUppercasesCountryCode()
    {
        var result = Location.Create(TenantId, "  Ankara Office  ", LocationType.Office, "tr", "  Çankaya  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Ankara Office");
        result.Value.CountryCode.ShouldBe("TR");
        result.Value.Address.ShouldBe("Çankaya");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_Fails(string name)
    {
        var result = Location.Create(TenantId, name, LocationType.Office, "TR", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.LocationNameRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("TUR")]
    [InlineData("T1")]
    [InlineData(null)]
    public void Create_WithInvalidCountryCode_Fails(string? countryCode)
    {
        var result = Location.Create(TenantId, "Some Office", LocationType.Office, countryCode!, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.LocationCountryCodeInvalid);
    }

    [Fact]
    public void Create_WithUndefinedType_Fails()
    {
        var result = Location.Create(TenantId, "Some Office", (LocationType)999, "TR", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.LocationTypeInvalid);
    }

    [Fact]
    public void Update_WithValidData_ChangesFields()
    {
        var location = Location.Create(TenantId, "Ankara Office", LocationType.Office, "TR", "Çankaya").Value;

        var result = location.Update("Ankara Warehouse", LocationType.Warehouse, "TR", "Sincan");

        result.IsSuccess.ShouldBeTrue();
        location.Name.ShouldBe("Ankara Warehouse");
        location.Type.ShouldBe(LocationType.Warehouse);
        location.Address.ShouldBe("Sincan");
    }

    [Fact]
    public void Update_WithBlankName_Fails()
    {
        var location = Location.Create(TenantId, "Ankara Office", LocationType.Office, "TR", null).Value;

        var result = location.Update("", LocationType.Office, "TR", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.LocationNameRequired);
        location.Name.ShouldBe("Ankara Office");
    }

    [Fact]
    public void Deactivate_WhenActive_Succeeds()
    {
        var location = Location.Create(TenantId, "Ankara Office", LocationType.Office, "TR", null).Value;

        var result = location.Deactivate();

        result.IsSuccess.ShouldBeTrue();
        location.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_Fails()
    {
        var location = Location.Create(TenantId, "Ankara Office", LocationType.Office, "TR", null).Value;
        location.Deactivate();

        var result = location.Deactivate();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.LocationAlreadyInactive);
    }

    [Fact]
    public void Activate_AfterDeactivate_RestoresIsActive()
    {
        var location = Location.Create(TenantId, "Ankara Office", LocationType.Office, "TR", null).Value;
        location.Deactivate();

        location.Activate();

        location.IsActive.ShouldBeTrue();
    }
}

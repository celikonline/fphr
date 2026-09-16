using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.CostCenters;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>Unit tests for the CostCenter aggregate (Faz 2 roadmap item #2, catalog-only dar tasarım; no DB/EF - see class summary in CostCenter.cs for why EF wiring is deferred).</summary>
public sealed class CostCenterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları");

        result.IsSuccess.ShouldBeTrue();
        var costCenter = result.Value;
        costCenter.Code.ShouldBe("IK-01");
        costCenter.Name.ShouldBe("İnsan Kaynakları");
        costCenter.IsActive.ShouldBeTrue();
        costCenter.TenantId.ShouldBe(TenantId);
    }

    [Fact]
    public void Create_TrimsNameAndUppercasesCode()
    {
        var result = CostCenter.Create(TenantId, "  satis-01  ", "  Satış  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("SATIS-01");
        result.Value.Name.ShouldBe("Satış");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithBlankCode_Fails(string? code)
    {
        var result = CostCenter.Create(TenantId, code!, "İnsan Kaynakları");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterCodeRequired);
    }

    [Fact]
    public void Create_WithCodeOverMaxLength_Fails()
    {
        var result = CostCenter.Create(TenantId, new string('A', 31), "İnsan Kaynakları");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterCodeTooLong);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_Fails(string name)
    {
        var result = CostCenter.Create(TenantId, "IK-01", name);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterNameRequired);
    }

    [Fact]
    public void Rename_WithValidName_ChangesName_ButNotCode()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;

        var result = costCenter.Rename("İK ve İdari İşler");

        result.IsSuccess.ShouldBeTrue();
        costCenter.Name.ShouldBe("İK ve İdari İşler");
        costCenter.Code.ShouldBe("IK-01");
    }

    [Fact]
    public void Rename_WithBlankName_Fails()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;

        var result = costCenter.Rename("");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterNameRequired);
        costCenter.Name.ShouldBe("İnsan Kaynakları");
    }

    [Fact]
    public void SetActive_False_WhenActive_Succeeds()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;

        var result = costCenter.SetActive(false);

        result.IsSuccess.ShouldBeTrue();
        costCenter.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void SetActive_False_WhenAlreadyInactive_Fails()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;
        costCenter.SetActive(false);

        var result = costCenter.SetActive(false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterAlreadyInactive);
    }

    [Fact]
    public void SetActive_True_WhenAlreadyActive_Fails()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;

        var result = costCenter.SetActive(true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.CostCenterAlreadyActive);
    }

    [Fact]
    public void SetActive_True_AfterDeactivate_RestoresIsActive()
    {
        var costCenter = CostCenter.Create(TenantId, "IK-01", "İnsan Kaynakları").Value;
        costCenter.SetActive(false);

        var result = costCenter.SetActive(true);

        result.IsSuccess.ShouldBeTrue();
        costCenter.IsActive.ShouldBeTrue();
    }
}

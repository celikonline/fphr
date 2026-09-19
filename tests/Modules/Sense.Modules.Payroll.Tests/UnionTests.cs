using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Unions;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Domain-only tests for <see cref="Union"/> (Faz 3 #58, 11.3.07-bordro.md "Sendika & TİS" list).</summary>
public sealed class UnionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = Union.Create(TenantId, "Öz Büro-İş", "10.01");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Öz Büro-İş");
        result.Value.IndustryBranchCode.ShouldBe("10.01");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithoutName_Fails(string? name)
    {
        var result = Union.Create(TenantId, name!, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionNameRequired);
    }

    [Fact]
    public void Update_ChangesNameAndIndustryCode()
    {
        var union = Union.Create(TenantId, "Eski Ad", "10.01").Value;

        var result = union.Update("Yeni Ad", "20.02");

        result.IsSuccess.ShouldBeTrue();
        union.Name.ShouldBe("Yeni Ad");
        union.IndustryBranchCode.ShouldBe("20.02");
    }

    [Fact]
    public void Deactivate_ThenActivate_TogglesIsActive()
    {
        var union = Union.Create(TenantId, "Sendika", null).Value;

        union.Deactivate();
        union.IsActive.ShouldBeFalse();

        union.Activate();
        union.IsActive.ShouldBeTrue();
    }
}

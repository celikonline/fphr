using Sense.Modules.Payroll.Domain.Incentives;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>The template catalog carries no rates: only the 5510 template (a legislation key reference) is pre-approved (PAY-INCENTIVE-1).</summary>
public sealed class IncentiveTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_HasUniqueKeysAndCodes_AndEveryTemplateCreatesAValidDefinition()
    {
        IncentiveTemplates.All.Select(t => t.TemplateKey).Distinct().Count().ShouldBe(IncentiveTemplates.All.Count);
        IncentiveTemplates.All.Select(t => t.Code).Distinct().Count().ShouldBe(IncentiveTemplates.All.Count);

        foreach (var template in IncentiveTemplates.All)
        {
            IncentiveDefinition.CreateFromTemplate(IncentiveDefinitionTests.TenantId, template, IncentiveDefinitionTests.Jan, Now).IsSuccess.ShouldBeTrue(template.TemplateKey);
        }
    }

    [Fact]
    public void Template5510_UsesOnlyTheLegislationKey_AndIsPreApproved()
    {
        var template = IncentiveTemplates.Find(IncentiveTemplates.Tr5510Md81)!;

        template.Code.ShouldBe("SGK_5510_MD81");
        template.Kind.ShouldBe(IncentiveKind.EmployerSgkRateDiscount);
        template.DeclarationCode.ShouldBe("05510");
        template.PreApproved.ShouldBeTrue();
        template.Parameters.Rate!.Value.ShouldBeNull();
        template.Parameters.Rate.LegislationKey.ShouldBe("Payroll.Sgk.EmployerDiscountRate");
        template.Parameters.EmployerSgkRate.ShouldBeNull();
        template.Parameters.CoverageBase.ShouldBeNull();
        template.Parameters.CapMultiplier.ShouldBeNull();
    }

    [Fact]
    public void Template5510_CreatesAnApprovedCompleteDefinition_WithTheSystemNote()
    {
        var template = IncentiveTemplates.Find(IncentiveTemplates.Tr5510Md81)!;

        var definition = IncentiveDefinition.CreateFromTemplate(IncentiveDefinitionTests.TenantId, template, IncentiveDefinitionTests.Jan, Now).Value;

        definition.IsSmmmApproved.ShouldBeTrue();
        definition.IsComplete.ShouldBeTrue();
        definition.ApprovedAt.ShouldBe(Now);
        definition.ApprovedByUserId.ShouldBeNull();
        definition.ApprovalNote.ShouldBe("Sistem: mevcut 5 puan davranışı");
        definition.TemplateKey.ShouldBe(IncentiveTemplates.Tr5510Md81);
        definition.IsActive.ShouldBeTrue();
    }

    public static TheoryData<string> DraftTemplateKeys() => new()
    {
        IncentiveTemplates.Tr6111,
        IncentiveTemplates.Tr7103Md17,
        IncentiveTemplates.Tr7103Md27,
        IncentiveTemplates.Tr5746Gv,
    };

    [Theory]
    [MemberData(nameof(DraftTemplateKeys))]
    public void OtherTemplates_HaveNoRatesOrDeclarationCode_AndAreNotPreApproved(string templateKey)
    {
        var template = IncentiveTemplates.Find(templateKey)!;

        template.PreApproved.ShouldBeFalse();
        template.DeclarationCode.ShouldBeNull();
        template.Parameters.Rate.ShouldBeNull();
        template.Parameters.EmployerSgkRate.ShouldBeNull();
        template.Parameters.ShouldBe(IncentiveParameters.Empty);
    }

    [Theory]
    [MemberData(nameof(DraftTemplateKeys))]
    public void OtherTemplates_CreateUnapprovedIncompleteDrafts(string templateKey)
    {
        var template = IncentiveTemplates.Find(templateKey)!;

        var definition = IncentiveDefinition.CreateFromTemplate(IncentiveDefinitionTests.TenantId, template, IncentiveDefinitionTests.Jan, Now).Value;

        definition.IsSmmmApproved.ShouldBeFalse();
        definition.ApprovedAt.ShouldBeNull();
        definition.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void Catalog_ContainsTheDocumentedKindsAndCodes()
    {
        IncentiveTemplates.Find(IncentiveTemplates.Tr6111)!.Code.ShouldBe("SGK_6111_TASLAK");
        IncentiveTemplates.Find(IncentiveTemplates.Tr6111)!.LawReference.ShouldBe("6111 / 4447 geç. md.10");
        IncentiveTemplates.Find(IncentiveTemplates.Tr6111)!.Kind.ShouldBe(IncentiveKind.EmployerShareCoverage);
        IncentiveTemplates.Find(IncentiveTemplates.Tr7103Md17)!.Code.ShouldBe("SGK_17103_TASLAK");
        IncentiveTemplates.Find(IncentiveTemplates.Tr7103Md27)!.Code.ShouldBe("SGK_27103_TASLAK");
        IncentiveTemplates.Find(IncentiveTemplates.Tr5746Gv)!.Code.ShouldBe("ARGE_5746_GV_TASLAK");
        IncentiveTemplates.Find(IncentiveTemplates.Tr5746Gv)!.Kind.ShouldBe(IncentiveKind.IncomeTaxWithholdingRelief);
        IncentiveTemplates.All.Count.ShouldBe(5);
    }

    [Fact]
    public void Find_UnknownKey_ReturnsNull()
    {
        IncentiveTemplates.Find("NOPE").ShouldBeNull();
        IncentiveTemplates.Find(null).ShouldBeNull();
    }
}

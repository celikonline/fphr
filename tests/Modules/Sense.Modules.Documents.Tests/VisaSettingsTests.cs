using Sense.Modules.Documents.Domain.VisaDocuments;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaSettingsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void CreateDefault_HasNoApproverAndNoTemplates()
    {
        var settings = VisaSettings.CreateDefault(TenantId);

        settings.TenantId.ShouldBe(TenantId);
        settings.ApproverEmployeeId.ShouldBeNull();
        settings.TemplateDocumentIds.ShouldBeEmpty();
    }

    [Fact]
    public void SetApprover_WithRealEmployeeId_SetsApprover()
    {
        var settings = VisaSettings.CreateDefault(TenantId);
        var approverId = Guid.NewGuid();

        settings.SetApprover(approverId);

        settings.ApproverEmployeeId.ShouldBe(approverId);
    }

    [Theory]
    [MemberData(nameof(NotConfiguredValues))]
    public void SetApprover_WithNullOrEmptyGuid_NormalizesToNotConfigured(Guid? approverId)
    {
        var settings = VisaSettings.CreateDefault(TenantId);
        settings.SetApprover(Guid.NewGuid());

        settings.SetApprover(approverId);

        settings.ApproverEmployeeId.ShouldBeNull();
    }

    public static IEnumerable<object?[]> NotConfiguredValues()
    {
        yield return [null];
        yield return [Guid.Empty];
    }

    [Fact]
    public void SetTemplateDocumentIds_ReplacesPreviousSetAndDropsDuplicatesAndEmpty()
    {
        var settings = VisaSettings.CreateDefault(TenantId);
        var first = Guid.NewGuid();
        settings.SetTemplateDocumentIds([first]);

        var second = Guid.NewGuid();
        settings.SetTemplateDocumentIds([second, second, Guid.Empty]);

        settings.TemplateDocumentIds.ShouldBe([second]);
    }

    [Fact]
    public void SetTemplateDocumentIds_WithEmptyList_ClearsTemplates()
    {
        var settings = VisaSettings.CreateDefault(TenantId);
        settings.SetTemplateDocumentIds([Guid.NewGuid()]);

        settings.SetTemplateDocumentIds([]);

        settings.TemplateDocumentIds.ShouldBeEmpty();
    }
}

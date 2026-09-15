using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// E4 domain rules: CustomFieldDefinition creation/validation/immutability/lifecycle, Employee's bulk-set custom field
/// value semantics, and the self-service contact info edit.
/// </summary>
public sealed class CustomFieldDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static CustomFieldDefinitionData Data(
        string name = "Yabancı Dil",
        CustomFieldSection section = CustomFieldSection.Personal,
        IReadOnlyList<string>? options = null,
        bool isRequired = false,
        bool isEmployeeEditable = false,
        bool employeeEditRequiresApproval = false,
        bool isReportable = true,
        bool isSensitive = false,
        int sortOrder = 0) =>
        new(name, section, options, isRequired, isEmployeeEditable, employeeEditRequiresApproval, isReportable, isSensitive, sortOrder);

    [Fact]
    public void Create_WithValidKeyAndText_Succeeds_AndNormalizesKeyToLowercase()
    {
        var result = CustomFieldDefinition.Create(TenantId, " Foreign_Language ", CustomFieldDataType.Text, Data());

        result.IsSuccess.ShouldBeTrue();
        var def = result.Value;
        def.Key.ShouldBe("foreign_language");
        def.Name.ShouldBe("Yabancı Dil");
        def.DataType.ShouldBe(CustomFieldDataType.Text);
        def.IsActive.ShouldBeTrue();
        def.Options.ShouldBeEmpty();
        def.IsReportable.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, CustomFieldDomainErrors.KeyRequired)]
    [InlineData("  ", CustomFieldDomainErrors.KeyRequired)]
    [InlineData("Foreign Language", CustomFieldDomainErrors.KeyInvalid)]
    [InlineData("1language", CustomFieldDomainErrors.KeyInvalid)]
    [InlineData("dil-yabancı", CustomFieldDomainErrors.KeyInvalid)]
    public void Create_WithInvalidKey_Fails(string? key, string code) =>
        CustomFieldDefinition.Create(TenantId, key, CustomFieldDataType.Text, Data()).Error.Code.ShouldBe(code);

    [Fact]
    public void Create_KeyTooLong_Fails() =>
        CustomFieldDefinition.Create(TenantId, new string('a', CustomFieldLimits.KeyMaxLength + 1), CustomFieldDataType.Text, Data())
            .Error.Code.ShouldBe(CustomFieldDomainErrors.KeyTooLong);

    [Theory]
    [InlineData(CustomFieldDataType.SingleSelect)]
    [InlineData(CustomFieldDataType.MultiSelect)]
    public void Create_SelectTypeWithoutOptions_Fails(CustomFieldDataType dataType) =>
        CustomFieldDefinition.Create(TenantId, "renk", dataType, Data(options: []))
            .Error.Code.ShouldBe(CustomFieldDomainErrors.OptionsRequired);

    [Fact]
    public void Create_SelectType_NormalizesTrimsAndDeduplicatesOptionsCaseInsensitively()
    {
        var result = CustomFieldDefinition.Create(TenantId, "renk", CustomFieldDataType.SingleSelect, Data(options: [" Kırmızı ", "Mavi", "kırmızı", ""]));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Options.ShouldBe(["Kırmızı", "Mavi"]);
    }

    [Fact]
    public void Create_NonSelectType_IgnoresOptionsEvenIfProvided()
    {
        var result = CustomFieldDefinition.Create(TenantId, "not", CustomFieldDataType.Text, Data(options: ["a", "b"]));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Options.ShouldBeEmpty();
    }

    [Fact]
    public void Create_NameRequiredAndTooLong_Fail()
    {
        CustomFieldDefinition.Create(TenantId, "k1", CustomFieldDataType.Text, Data(name: " ")).Error.Code.ShouldBe(CustomFieldDomainErrors.NameRequired);
        CustomFieldDefinition.Create(TenantId, "k2", CustomFieldDataType.Text, Data(name: new string('a', CustomFieldLimits.NameMaxLength + 1)))
            .Error.Code.ShouldBe(CustomFieldDomainErrors.NameTooLong);
    }

    [Fact]
    public void KeyAndDataType_AreImmutable_UpdateOnlyChangesEverythingElse()
    {
        var def = CustomFieldDefinition.Create(TenantId, "renk", CustomFieldDataType.SingleSelect, Data(options: ["Kırmızı", "Mavi"])).Value;

        var updated = def.Update(Data(name: "Renk Tercihi", section: CustomFieldSection.Other, options: ["Yeşil"], isRequired: true, sortOrder: 5));

        updated.IsSuccess.ShouldBeTrue();
        def.Key.ShouldBe("renk");
        def.DataType.ShouldBe(CustomFieldDataType.SingleSelect);
        def.Name.ShouldBe("Renk Tercihi");
        def.Section.ShouldBe(CustomFieldSection.Other);
        def.Options.ShouldBe(["Yeşil"]);
        def.IsRequired.ShouldBeTrue();
        def.SortOrder.ShouldBe(5);
    }

    [Fact]
    public void DeactivateReactivate_ToggleIsActive_AndRejectRedundantCalls()
    {
        var def = CustomFieldDefinition.Create(TenantId, "k", CustomFieldDataType.Text, Data()).Value;

        def.Deactivate().IsSuccess.ShouldBeTrue();
        def.IsActive.ShouldBeFalse();
        def.Deactivate().Error.Code.ShouldBe(CustomFieldDomainErrors.AlreadyInactive);

        def.Reactivate().IsSuccess.ShouldBeTrue();
        def.IsActive.ShouldBeTrue();
        def.Reactivate().Error.Code.ShouldBe(CustomFieldDomainErrors.AlreadyActive);
    }

    [Fact]
    public void SetCustomFieldValues_BulkSetSemantics_OnlySubmittedDefinitionsChange()
    {
        var employee = EmployeeEntity.Create(TenantId, new EmployeeInfo("Ali", "Kaya")).Value;
        var defA = Guid.NewGuid();
        var defB = Guid.NewGuid();

        employee.SetCustomFieldValues(new Dictionary<Guid, string?> { [defA] = "10", [defB] = "yes" }).ShouldBeTrue();
        employee.CustomFieldValues.Select(v => (v.DefinitionId, v.Value)).ShouldBe([(defA, "10"), (defB, "yes")], ignoreOrder: true);

        // Only defA is present this time: defB keeps its stored value untouched.
        employee.SetCustomFieldValues(new Dictionary<Guid, string?> { [defA] = "20" }).ShouldBeTrue();
        employee.CustomFieldValues.First(v => v.DefinitionId == defA).Value.ShouldBe("20");
        employee.CustomFieldValues.First(v => v.DefinitionId == defB).Value.ShouldBe("yes");

        // Re-sending the same value is a no-op.
        employee.SetCustomFieldValues(new Dictionary<Guid, string?> { [defA] = "20" }).ShouldBeFalse();

        // Null clears a stored value.
        employee.SetCustomFieldValues(new Dictionary<Guid, string?> { [defB] = null }).ShouldBeTrue();
        employee.CustomFieldValues.First(v => v.DefinitionId == defB).Value.ShouldBeNull();

        // Null for a never-set definition is also a no-op (nothing to clear).
        var defC = Guid.NewGuid();
        employee.SetCustomFieldValues(new Dictionary<Guid, string?> { [defC] = null }).ShouldBeFalse();
        employee.CustomFieldValues.Any(v => v.DefinitionId == defC).ShouldBeFalse();
    }

    [Fact]
    public void UpdateContactInfo_ValidatesAndNormalizesFields_AndReportsWhetherAnythingChanged()
    {
        var employee = EmployeeEntity.Create(TenantId, new EmployeeInfo("Ali", "Kaya")).Value;

        var first = employee.UpdateContactInfo(new EmployeeContactInfo(" Ali.K@Example.COM ", " 0555 111 22 33 ", null, " Atatürk Cad. ", " İstanbul ", " 34000 "));
        first.IsSuccess.ShouldBeTrue();
        first.Value.ShouldBeTrue();
        employee.PersonalEmail.ShouldBe("ali.k@example.com");
        employee.City.ShouldBe("İstanbul");

        // Resending the exact same values is not a change.
        var second = employee.UpdateContactInfo(new EmployeeContactInfo("ali.k@example.com", "0555 111 22 33", null, "Atatürk Cad.", "İstanbul", "34000"));
        second.Value.ShouldBeFalse();

        ShouldFail(employee.UpdateContactInfo(new EmployeeContactInfo("not-an-email", null, null, null, null, null)), EmployeeDomainErrors.PersonalEmailInvalid);
        ShouldFail(employee.UpdateContactInfo(new EmployeeContactInfo(null, null, null, new string('a', EmployeeLimits.AddressLineMaxLength + 1), null, null)), EmployeeDomainErrors.AddressLineTooLong);

        // WorkEmail/NationalId/HireDate are untouched by this narrower self-service path.
        employee.WorkEmail.ShouldBeNull();
        employee.NationalId.ShouldBeNull();
    }

    private static void ShouldFail<T>(Result<T> result, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }
}

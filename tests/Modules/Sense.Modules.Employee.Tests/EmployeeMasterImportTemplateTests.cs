using NSubstitute;
using Sense.Modules.Employee.Application.Import;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Kernel.ValueObjects;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// senseik-009/F-02: the "employee.master" template's sample rows are the manual-QA/documentation surface for the
/// new Sicil Numarası/Uyruk/Yabancı Kimlik/Pasaport/SGK meslek kodu columns - this proves the foreign-employee
/// sample row is both well-formed (correct column keys/values) and actually passes domain validation, not just
/// cosmetically present in the generated workbook (TemplateWorkbookTests covers the workbook's own structure).
/// </summary>
public sealed class EmployeeMasterImportTemplateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetSampleRows_IncludesAValidForeignEmployeeRow_ThatPassesDomainValidation()
    {
        var template = BuildTemplate();
        var columns = await template.GetColumnsAsync();
        var rows = template.GetSampleRows(columns);

        var foreignRow = rows.Single(r => r.NoteKey.EndsWith("sample.valid_foreign", StringComparison.Ordinal));
        foreignRow.IsValid.ShouldBeTrue();
        foreignRow.Values[EmployeeImportColumns.FirstName].ShouldBe("Olena");
        foreignRow.Values.ContainsKey(EmployeeImportColumns.NationalId).ShouldBeFalse();
        foreignRow.Values[EmployeeImportColumns.Nationality].ShouldBe("Yabancı");
        foreignRow.Values[EmployeeImportColumns.ForeignIdNumber].ShouldBe("99123456740");

        EmployeeImportOptions.TryParse<Nationality>(foreignRow.Values[EmployeeImportColumns.Nationality], out var nationality).ShouldBeTrue();
        nationality.ShouldBe(Nationality.Foreign);
        TcKimlik.Of(foreignRow.Values[EmployeeImportColumns.ForeignIdNumber]).IsForeign.ShouldBeTrue();

        var info = new EmployeeInfo(
            foreignRow.Values[EmployeeImportColumns.FirstName],
            foreignRow.Values[EmployeeImportColumns.LastName],
            Nationality: nationality,
            ForeignIdNumber: foreignRow.Values[EmployeeImportColumns.ForeignIdNumber]);
        EmployeeEntity.Validate(info).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task GetColumnsAsync_OffersTheFiveNewSenseik009And010Columns()
    {
        var template = BuildTemplate();
        var columns = await template.GetColumnsAsync();
        var keys = columns.Select(c => c.Key).ToList();

        keys.ShouldContain(EmployeeImportColumns.SicilNumarasi);
        keys.ShouldContain(EmployeeImportColumns.Nationality);
        keys.ShouldContain(EmployeeImportColumns.ForeignIdNumber);
        keys.ShouldContain(EmployeeImportColumns.PassportNumber);
        keys.ShouldContain(EmployeeImportColumns.SgkOccupationCode);

        // NationalId stays the only match key for now (dual-key transition, senseik-009/F-02 §5): SicilNumarasi is
        // offered but not yet a match key.
        columns.Single(c => c.Key == EmployeeImportColumns.SicilNumarasi).IsMatchKey.ShouldBeFalse();
    }

    private static EmployeeMasterImportTemplate BuildTemplate()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ListActiveTypesAsync(TenantId, Arg.Any<CancellationToken>()).Returns([]);
        orgUnits.ListActiveUnitsAsync(TenantId, Arg.Any<CancellationToken>()).Returns([]);
        var customFields = Substitute.For<ICustomFieldDefinitionRepository>();
        customFields.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        return new EmployeeMasterImportTemplate(tenant, orgUnits, customFields);
    }
}

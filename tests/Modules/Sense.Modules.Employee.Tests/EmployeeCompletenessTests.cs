using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Domain.Employees;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// senseik-045: the "Eksik bilgi" badge used to be a flat yes/no that flagged every seeded demo employee (56/56),
/// making it meaningless. <see cref="EmployeeCompleteness.MissingFieldCount"/> backs the new "Eksik bilgi (N)" badge
/// text; <see cref="EmployeeCompleteness.IsMissingInfo"/> must stay in lockstep with it (count > 0).
/// </summary>
public sealed class EmployeeCompletenessTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EmployeeRow RowWith(
        EmployeeStatus status = EmployeeStatus.Active,
        string? nationalId = "12345678901",
        DateOnly? hireDate = null,
        DateOnly? birthDate = null,
        Guid? orgUnitId = null,
        ContractType? contractType = ContractType.Indefinite,
        EmploymentType? employmentType = EmploymentType.FullTime) =>
        new(
            Id: Guid.NewGuid(),
            FirstName: "Test",
            LastName: "Employee",
            DisplayName: "Test Employee",
            NationalId: nationalId,
            WorkEmail: "test@example.com",
            PersonalEmail: null,
            WorkPhone: null,
            HireDate: hireDate ?? new DateOnly(2020, 1, 1),
            BirthDate: birthDate ?? new DateOnly(1990, 1, 1),
            Status: status,
            OrgUnitId: orgUnitId ?? Guid.NewGuid(),
            ManagerEmployeeId: null,
            UserId: null,
            ContractType: contractType,
            EmploymentType: employmentType);

    [Fact]
    public void MissingFieldCount_IsZero_WhenEveryExpectedFieldIsFilled()
    {
        var row = RowWith();

        EmployeeCompleteness.MissingFieldCount(row).ShouldBe(0);
        EmployeeCompleteness.IsMissingInfo(row).ShouldBeFalse();
    }

    [Fact]
    public void MissingFieldCount_CountsEachUnfilledField_Independently()
    {
        var row = RowWith(nationalId: null, contractType: null, employmentType: null);

        // NationalId, ContractType, EmploymentType missing -> 3.
        EmployeeCompleteness.MissingFieldCount(row).ShouldBe(3);
        EmployeeCompleteness.IsMissingInfo(row).ShouldBeTrue();
    }

    [Fact]
    public void MissingFieldCount_CountsAllSixFields_WhenNoneAreFilled()
    {
        var row = RowWith(nationalId: null, orgUnitId: null, contractType: null, employmentType: null) with
        {
            HireDate = null,
            BirthDate = null,
            OrgUnitId = null,
        };

        EmployeeCompleteness.MissingFieldCount(row).ShouldBe(6);
        EmployeeCompleteness.IsMissingInfo(row).ShouldBeTrue();
    }

    [Fact]
    public void MissingFieldCount_IsZero_ForDraftEmployees_EvenWithNoFieldsFilled()
    {
        // A Draft row (quick-add, no hire date yet) is knowingly incomplete until someone hires them - it must
        // never show "Eksik bilgi", regardless of how many fields are unset.
        var row = RowWith(status: EmployeeStatus.Draft, nationalId: null, orgUnitId: null, contractType: null, employmentType: null) with
        {
            HireDate = null,
            BirthDate = null,
            OrgUnitId = null,
        };

        EmployeeCompleteness.MissingFieldCount(row).ShouldBe(0);
        EmployeeCompleteness.IsMissingInfo(row).ShouldBeFalse();
    }
}

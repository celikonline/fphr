using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>Editable personal/contact/employment fields of an employee, validated together by <see cref="Employee.Create"/> and <see cref="Employee.UpdateInfo"/>.</summary>
public sealed record EmployeeInfo(
    string FirstName,
    string LastName,
    string? NationalId = null,
    string? WorkEmail = null,
    string? PersonalEmail = null,
    string? WorkPhone = null,
    DateOnly? HireDate = null,
    DateOnly? BirthDate = null,
    Gender Gender = Gender.Unspecified,
    MaritalStatus MaritalStatus = MaritalStatus.Unspecified,
    int? ChildCount = null,
    EducationStatus? EducationStatus = null,
    HighestEducationLevel? HighestEducationLevel = null,
    MilitaryStatus? MilitaryStatus = null,
    DisabilityDegree? DisabilityDegree = null,
    BloodType? BloodType = null,
    string? MobilePhone = null,
    string? HomePhone = null,
    string? AddressLine = null,
    string? City = null,
    string? PostalCode = null,
    ContractType? ContractType = null,
    EmploymentType? EmploymentType = null,
    DateOnly? LeaveAccrualStartDate = null,
    string? SicilNumarasi = null,
    Nationality Nationality = Nationality.Turkish,
    string? ForeignIdNumber = null,
    string? PassportNumber = null,
    string? SgkOccupationCode = null);

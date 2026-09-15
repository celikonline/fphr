using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Contact/address fields an employee may edit on their own card ("Self-servis düzenleme", 11.3.03: varsayılan
/// editable sections = iletişim/adres). Deliberately narrower than <see cref="EmployeeInfo"/> - it never touches
/// WorkEmail/NationalId/HireDate/status/career, unlike the full <see cref="Employee.UpdateInfo"/> path.
/// </summary>
public sealed record EmployeeContactInfo(
    string? PersonalEmail,
    string? MobilePhone,
    string? HomePhone,
    string? AddressLine,
    string? City,
    string? PostalCode);

public sealed partial class Employee
{
    /// <summary>Self-service edit of contact/address fields only ("Self-servis düzenleme"); returns true when something changed.</summary>
    public Result<bool> UpdateContactInfo(EmployeeContactInfo info)
    {
        var validated = ValidateContactInfo(info);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var personalEmail = Normalize(info.PersonalEmail)?.ToLowerInvariant();
        var mobilePhone = PhoneNormalizer.Normalize(info.MobilePhone);
        var homePhone = PhoneNormalizer.Normalize(info.HomePhone);
        var addressLine = Normalize(info.AddressLine);
        var city = Normalize(info.City);
        var postalCode = Normalize(info.PostalCode);

        var changed = !string.Equals(PersonalEmail, personalEmail, StringComparison.Ordinal) ||
                      !string.Equals(MobilePhone, mobilePhone, StringComparison.Ordinal) ||
                      !string.Equals(HomePhone, homePhone, StringComparison.Ordinal) ||
                      !string.Equals(AddressLine, addressLine, StringComparison.Ordinal) ||
                      !string.Equals(City, city, StringComparison.Ordinal) ||
                      !string.Equals(PostalCode, postalCode, StringComparison.Ordinal);

        PersonalEmail = personalEmail;
        MobilePhone = mobilePhone;
        HomePhone = homePhone;
        AddressLine = addressLine;
        City = city;
        PostalCode = postalCode;

        if (changed)
        {
            Raise(new EmployeeUpdated(Id, TenantId));
        }

        return Result.Success(changed);
    }

    public static Result ValidateContactInfo(EmployeeContactInfo info)
    {
        if (Normalize(info.PersonalEmail) is { } personalEmail && !EmailAddress.IsValid(personalEmail))
        {
            return Error.Validation(EmployeeDomainErrors.PersonalEmailInvalid);
        }

        if (PhoneNormalizer.Normalize(info.MobilePhone) is { Length: > EmployeeLimits.PhoneMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.MobilePhoneTooLong);
        }

        if (PhoneNormalizer.Normalize(info.HomePhone) is { Length: > EmployeeLimits.PhoneMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.HomePhoneTooLong);
        }

        if (Normalize(info.AddressLine) is { Length: > EmployeeLimits.AddressLineMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.AddressLineTooLong);
        }

        if (Normalize(info.City) is { Length: > EmployeeLimits.CityMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.CityTooLong);
        }

        if (Normalize(info.PostalCode) is { Length: > EmployeeLimits.PostalCodeMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.PostalCodeTooLong);
        }

        return Result.Success();
    }
}

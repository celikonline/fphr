using NSubstitute;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Regression for QA findings: an 11-year-old, a 1850-born, a 1900-hired and a 2035-hired employee were all
/// accepted. Domain guard (<see cref="EmployeeEntity.Validate"/>, also used by import/provisioning) and the
/// FluentValidation mirror must both reject them; 15-17 year-olds stay allowed.
/// </summary>
public sealed class EmployeeDateBoundsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string? DomainError(DateOnly? birth, DateOnly? hire)
    {
        var result = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", HireDate: hire, BirthDate: birth));
        return result.IsFailure ? result.Error.Code : null;
    }

    [Fact]
    public void Domain_ElevenYearOldAtHire_Fails() =>
        DomainError(new DateOnly(2015, 5, 5), new DateOnly(2026, 1, 1)).ShouldBe(EmployeeDomainErrors.MinimumAge);

    [Fact]
    public void Domain_FourteenYearsElevenMonthsAtHire_Fails() =>
        DomainError(new DateOnly(2000, 6, 2), new DateOnly(2015, 6, 1)).ShouldBe(EmployeeDomainErrors.MinimumAge);

    [Fact]
    public void Domain_ExactlyFifteenOnHireDate_Succeeds() =>
        DomainError(new DateOnly(2000, 6, 1), new DateOnly(2015, 6, 1)).ShouldBeNull();

    [Fact]
    public void Domain_SeventeenYearOld_IsAllowed() =>
        DomainError(new DateOnly(2008, 3, 1), new DateOnly(2026, 1, 1)).ShouldBeNull();

    [Fact]
    public void Domain_BirthBefore1900_Fails() =>
        DomainError(new DateOnly(1850, 1, 1), new DateOnly(2020, 1, 1)).ShouldBe(EmployeeDomainErrors.BirthDateTooEarly);

    [Fact]
    public void Domain_HireBefore1900_Fails() =>
        DomainError(null, new DateOnly(1899, 12, 31)).ShouldBe(EmployeeDomainErrors.HireDateTooEarly);

    [Fact]
    public void Domain_HireOn1900_1_1_Succeeds() =>
        DomainError(null, new DateOnly(1900, 1, 1)).ShouldBeNull();

    [Fact]
    public void Domain_HireTwoYearsAhead_Fails() =>
        DomainError(null, Today.AddYears(2)).ShouldBe(EmployeeDomainErrors.HireDateTooFarInFuture);

    [Fact]
    public void Domain_HireSixMonthsAhead_Succeeds() =>
        DomainError(null, Today.AddMonths(6)).ShouldBeNull();

    [Fact]
    public void Domain_NoBirthDate_SkipsMinimumAge() =>
        DomainError(null, new DateOnly(2020, 1, 1)).ShouldBeNull();

    [Fact]
    public void Domain_UpdateInfo_ToUnderageBirthDate_FailsWithoutChangingStoredValues()
    {
        var e = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", HireDate: new DateOnly(2024, 1, 1), BirthDate: new DateOnly(1990, 1, 1))).Value;

        var result = e.UpdateInfo(new EmployeeInfo("A", "B", HireDate: new DateOnly(2024, 1, 1), BirthDate: new DateOnly(2015, 5, 5)));

        result.Error.Code.ShouldBe(EmployeeDomainErrors.MinimumAge);
        e.BirthDate.ShouldBe(new DateOnly(1990, 1, 1));
    }

    private static CreateEmployeeCommand Command(DateOnly? birth, DateOnly? hire) =>
        new("A", "B", null, null, null, null, hire, birth, null, null);

    private static CreateEmployeeValidator Validator()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return new CreateEmployeeValidator(new EmployeeSensitiveAccess(user, permissions));
    }

    [Theory]
    [InlineData("2015-05-05", "2026-01-01", EmployeeDomainErrors.MinimumAge)]
    [InlineData("1850-01-01", "2020-01-01", EmployeeDomainErrors.BirthDateTooEarly)]
    [InlineData(null, "1900-01-01", null)]
    [InlineData(null, "1899-01-01", EmployeeDomainErrors.HireDateTooEarly)]
    [InlineData("1990-01-01", "2035-01-01", EmployeeDomainErrors.HireDateTooFarInFuture)]
    [InlineData("2009-01-01", "2026-01-01", null)]
    public async Task Validator_AppliesDateBounds(string? birth, string? hire, string? expectedCode)
    {
        var command = Command(birth is null ? null : DateOnly.Parse(birth), hire is null ? null : DateOnly.Parse(hire));

        var result = await Validator().ValidateAsync(command, TestContext.Current.CancellationToken);

        if (expectedCode is null)
        {
            result.IsValid.ShouldBeTrue(string.Join(";", result.Errors.Select(e => e.ErrorMessage)));
        }
        else
        {
            result.Errors.Select(e => e.ErrorMessage).ShouldContain(expectedCode);
        }
    }
}

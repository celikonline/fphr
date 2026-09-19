using System.Text.Json;
using System.Text.Json.Serialization;
using NSubstitute;
using Sense.Modules.Employee.Api.Controllers;
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
/// senseik-058: PUT /employees/{id} used to rebuild EmployeeInfo without SicilNumarasi / Nationality / ForeignIdNumber /
/// PassportNumber / SgkOccupationCode, so every save (web section edit, mobile, integrations) silently wiped them
/// (Nationality fell back to Turkish). Pins the "keep when absent, persist when sent, empty string clears" contract
/// through the same path the handler uses (command.ToInfo(existing) -> Employee.UpdateInfo) - no database needed.
/// </summary>
public sealed class EmployeeUpdateFieldPreservationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EmployeeEntity ForeignEmployee() =>
        EmployeeEntity.Create(TenantId, new EmployeeInfo(
            "Olena", "Kovalenko", HireDate: new DateOnly(2024, 1, 1),
            SicilNumarasi: "SCL-001", Nationality: Nationality.Foreign, ForeignIdNumber: "99123456740",
            PassportNumber: "P1234567", SgkOccupationCode: "2511.01")).Value;

    private static UpdateEmployeeCommand BlindUpdate(EmployeeEntity e) =>
        new(e.Id, e.FirstName, e.LastName, null, null, null, null, e.HireDate, null, null, null);

    private static EmployeeEntity Apply(EmployeeEntity e, UpdateEmployeeCommand command)
    {
        e.UpdateInfo(command.ToInfo(e)).IsSuccess.ShouldBeTrue();
        return e;
    }

    [Fact]
    public void Update_WithoutTheNewFields_KeepsAllOfThem()
    {
        var e = ForeignEmployee();

        Apply(e, BlindUpdate(e) with { FirstName = "Olena Maria" });

        e.FirstName.ShouldBe("Olena Maria");
        e.SicilNumarasi.ShouldBe("SCL-001");
        e.Nationality.ShouldBe(Nationality.Foreign);
        e.ForeignIdNumber.ShouldBe("99123456740");
        e.PassportNumber.ShouldBe("P1234567");
        e.SgkOccupationCode.ShouldBe("2511.01");
    }

    [Fact]
    public void Update_WithoutNationality_DoesNotResetForeignEmployeeToTurkish()
    {
        var e = ForeignEmployee();

        Apply(e, BlindUpdate(e));

        e.Nationality.ShouldBe(Nationality.Foreign);
    }

    [Fact]
    public void Update_WithTheNewFields_PersistsEachOfThem()
    {
        var e = ForeignEmployee();

        Apply(e, BlindUpdate(e) with
        {
            SicilNumarasi = " SCL-002 ",
            Nationality = Nationality.Turkish,
            ForeignIdNumber = "99123456741",
            PassportNumber = "P7654321",
            SgkOccupationCode = "7212.03",
        });

        e.SicilNumarasi.ShouldBe("SCL-002");
        e.Nationality.ShouldBe(Nationality.Turkish);
        e.ForeignIdNumber.ShouldBe("99123456741");
        e.PassportNumber.ShouldBe("P7654321");
        e.SgkOccupationCode.ShouldBe("7212.03");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithEmptyString_ClearsTheTextField(string empty)
    {
        var e = ForeignEmployee();

        Apply(e, BlindUpdate(e) with { SicilNumarasi = empty, PassportNumber = empty, SgkOccupationCode = empty });

        e.SicilNumarasi.ShouldBeNull();
        e.PassportNumber.ShouldBeNull();
        e.SgkOccupationCode.ShouldBeNull();
        e.ForeignIdNumber.ShouldBe("99123456740");
        e.Nationality.ShouldBe(Nationality.Foreign);
    }

    [Fact]
    public void Update_ClearingForeignIdOfAForeignEmployeeWithoutPassport_IsStillRejectedByTheDomain()
    {
        var e = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", Nationality: Nationality.Foreign, ForeignIdNumber: "99123456740")).Value;
        var command = new UpdateEmployeeCommand(e.Id, "A", "B", null, null, null, null, null, null, null, null, ForeignIdNumber: "");

        e.UpdateInfo(command.ToInfo(e)).Error.Code.ShouldBe(EmployeeDomainErrors.ForeignIdentificationRequired);
        e.ForeignIdNumber.ShouldBe("99123456740");
    }

    [Fact]
    public void ToInfo_MergesOnlyTheKeepWhenAbsentFields_AndLeavesTheRestAsSent()
    {
        var e = ForeignEmployee();
        var command = BlindUpdate(e) with { WorkPhone = "5551112233", SicilNumarasi = "NEW" };

        var info = command.ToInfo(e);

        info.SicilNumarasi.ShouldBe("NEW");
        info.PassportNumber.ShouldBe("P1234567");
        info.WorkPhone.ShouldBe("5551112233");
        info.MobilePhone.ShouldBeNull();
    }

    [Fact]
    public async Task Validator_ForeignEmployeeWithForeignPrefixedNationalIdAndUnsentNationality_Passes()
    {
        // Without nationalityProvided the request looked Turkish and "99..." was rejected before the handler could merge.
        var command = new UpdateEmployeeCommand(Guid.NewGuid(), "Olena", "Kovalenko", "99123456740", null, null, null, null, null, null, null);

        (await Validator().ValidateAsync(command, TestContext.Current.CancellationToken)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Validator_ExplicitTurkishWithForeignPrefixedNationalId_StillFails()
    {
        var command = new UpdateEmployeeCommand(
            Guid.NewGuid(), "A", "B", "99123456740", null, null, null, null, null, null, null, Nationality: Nationality.Turkish);

        (await Validator().ValidateAsync(command, TestContext.Current.CancellationToken)).Errors.ShouldContain(x => x.ErrorMessage == EmployeeDomainErrors.NationalIdForeignPrefixForTurkish);
    }

    [Fact]
    public async Task Validator_ExplicitForeignWithoutAnyForeignIdentification_StillFails()
    {
        var command = new UpdateEmployeeCommand(Guid.NewGuid(), "A", "B", null, null, null, null, null, null, null, null, Nationality: Nationality.Foreign);

        (await Validator().ValidateAsync(command, TestContext.Current.CancellationToken)).Errors.ShouldContain(x => x.ErrorMessage == EmployeeDomainErrors.ForeignIdentificationRequired);
    }

    [Fact]
    public async Task Validator_TooLongSicilNumarasi_Fails()
    {
        var command = new UpdateEmployeeCommand(
            Guid.NewGuid(), "A", "B", null, null, null, null, null, null, null, null, SicilNumarasi: new string('9', EmployeeLimits.SicilNumarasiMaxLength + 1));

        (await Validator().ValidateAsync(command, TestContext.Current.CancellationToken)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Create_ToInfo_CarriesTheNewFields_AndDefaultsNationalityToTurkish()
    {
        var blank = new CreateEmployeeCommand("A", "B", null, null, null, null, null, null, null, null).ToInfo();
        blank.Nationality.ShouldBe(Nationality.Turkish);

        var sent = new CreateEmployeeCommand(
            "A", "B", null, null, null, null, null, null, null, null,
            SicilNumarasi: "S1", Nationality: Nationality.Foreign, ForeignIdNumber: "99123456740", PassportNumber: "P1", SgkOccupationCode: "1234").ToInfo();
        sent.SicilNumarasi.ShouldBe("S1");
        sent.Nationality.ShouldBe(Nationality.Foreign);
        sent.ForeignIdNumber.ShouldBe("99123456740");
        sent.PassportNumber.ShouldBe("P1");
        sent.SgkOccupationCode.ShouldBe("1234");
    }

    [Fact]
    public void RequestBody_WithoutTheNewFields_DeserializesToNulls_AndWithThemToTheSentValues()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

        var legacy = JsonSerializer.Deserialize<EmployeeRequest>("""{"firstName":"A","lastName":"B"}""", options)!;
        legacy.SicilNumarasi.ShouldBeNull();
        legacy.Nationality.ShouldBeNull();
        legacy.ForeignIdNumber.ShouldBeNull();
        legacy.PassportNumber.ShouldBeNull();
        legacy.SgkOccupationCode.ShouldBeNull();

        var current = JsonSerializer.Deserialize<EmployeeRequest>(
            """{"firstName":"A","lastName":"B","sicilNumarasi":"S1","nationality":"foreign","foreignIdNumber":"99123456740","passportNumber":"P1","sgkOccupationCode":"1234"}""",
            options)!;
        current.SicilNumarasi.ShouldBe("S1");
        current.Nationality.ShouldBe(Nationality.Foreign);
        current.ForeignIdNumber.ShouldBe("99123456740");
        current.PassportNumber.ShouldBe("P1");
        current.SgkOccupationCode.ShouldBe("1234");
    }

    private static UpdateEmployeeValidator Validator()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return new UpdateEmployeeValidator(new EmployeeSensitiveAccess(user, permissions));
    }
}

using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

public sealed class EmployeeDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidInfo_TrimsNormalizesAndComposesDisplayName()
    {
        var result = EmployeeEntity.Create(TenantId, new EmployeeInfo(
            "  Ayşe ", " Yılmaz ", " 10000000146 ", " Ayse.Yilmaz@Acme.COM ", "ayse@example.com", " +90 555 ", new DateOnly(2020, 1, 1), new DateOnly(1990, 5, 5)));

        result.IsSuccess.ShouldBeTrue();
        var e = result.Value;
        e.FirstName.ShouldBe("Ayşe");
        e.LastName.ShouldBe("Yılmaz");
        e.DisplayName.ShouldBe("Ayşe Yılmaz");
        e.NationalId.ShouldBe("10000000146");
        e.WorkEmail.ShouldBe("ayse.yilmaz@acme.com");
        e.WorkPhone.ShouldBe("+90 555");
        e.Status.ShouldBe(EmployeeStatus.Active);
    }

    [Theory]
    [InlineData("", "Yılmaz", EmployeeDomainErrors.FirstNameRequired)]
    [InlineData("Ayşe", "  ", EmployeeDomainErrors.LastNameRequired)]
    public void Create_WithoutFirstOrLastName_Fails(string first, string last, string code)
    {
        var result = EmployeeEntity.Create(TenantId, new EmployeeInfo(first, last));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public void Create_WithTooLongName_Fails() =>
        EmployeeEntity.Create(TenantId, new EmployeeInfo(new string('a', EmployeeLimits.NameMaxLength + 1), "B"))
            .Error.Code.ShouldBe(EmployeeDomainErrors.NameTooLong);

    [Theory]
    [InlineData("12345678901")]
    [InlineData("01234567890")]
    [InlineData("1000000014")]
    [InlineData("1000000014a")]
    public void Create_WithInvalidNationalId_Fails(string nationalId) =>
        EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", NationalId: nationalId))
            .Error.Code.ShouldBe(EmployeeDomainErrors.NationalIdInvalid);

    [Fact]
    public void Create_WithBlankOptionalFields_StoresNulls()
    {
        var e = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", " ", "", null, "  ")).Value;

        e.NationalId.ShouldBeNull();
        e.WorkEmail.ShouldBeNull();
        e.PersonalEmail.ShouldBeNull();
        e.WorkPhone.ShouldBeNull();
    }

    [Theory]
    [InlineData("not-an-email", null, EmployeeDomainErrors.WorkEmailInvalid)]
    [InlineData(null, "a@@b", EmployeeDomainErrors.PersonalEmailInvalid)]
    public void Create_WithInvalidEmail_Fails(string? work, string? personal, string code) =>
        EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", WorkEmail: work, PersonalEmail: personal))
            .Error.Code.ShouldBe(code);

    [Fact]
    public void Create_WithTooLongPhone_Fails() =>
        EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", WorkPhone: new string('1', EmployeeLimits.PhoneMaxLength + 1)))
            .Error.Code.ShouldBe(EmployeeDomainErrors.WorkPhoneTooLong);

    [Fact]
    public void UpdateInfo_RecomputesDisplayName_AndRejectsInvalidWithoutChanges()
    {
        var e = EmployeeEntity.Create(TenantId, new EmployeeInfo("Ali", "Kaya")).Value;

        e.UpdateInfo(new EmployeeInfo("Veli", "Demir")).IsSuccess.ShouldBeTrue();
        e.DisplayName.ShouldBe("Veli Demir");

        e.UpdateInfo(new EmployeeInfo("X", "Y", NationalId: "123")).IsFailure.ShouldBeTrue();
        e.DisplayName.ShouldBe("Veli Demir");
    }

    [Fact]
    public void Create_WithSelfAsManager_Fails()
    {
        var id = Guid.NewGuid();
        EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B"), managerEmployeeId: id, id: id)
            .Error.Code.ShouldBe(EmployeeDomainErrors.ManagerSelf);
    }

    [Fact]
    public void AssignManager_Self_Fails()
    {
        var e = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B")).Value;

        e.AssignManager(e.Id, new Dictionary<Guid, Guid?>()).Error.Code.ShouldBe(EmployeeDomainErrors.ManagerSelf);
    }

    [Fact]
    public void AssignManager_ThatClosesALoop_FailsWithCycle()
    {
        // top <- middle <- bottom; making bottom the manager of top closes the loop.
        var top = EmployeeEntity.Create(TenantId, new EmployeeInfo("Top", "T")).Value;
        var middle = Guid.NewGuid();
        var bottom = Guid.NewGuid();
        var map = new Dictionary<Guid, Guid?> { [top.Id] = null, [middle] = top.Id, [bottom] = middle };

        var result = top.AssignManager(bottom, map);

        result.Error.Code.ShouldBe(EmployeeDomainErrors.ManagerCycle);
        top.ManagerEmployeeId.ShouldBeNull();
    }

    [Fact]
    public void AssignManager_ToUnrelatedEmployee_Succeeds_AndClearingWorks()
    {
        var e = EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B")).Value;
        var boss = Guid.NewGuid();

        e.AssignManager(boss, new Dictionary<Guid, Guid?> { [e.Id] = null, [boss] = null }).IsSuccess.ShouldBeTrue();
        e.ManagerEmployeeId.ShouldBe(boss);

        e.AssignManager(null, new Dictionary<Guid, Guid?>()).IsSuccess.ShouldBeTrue();
        e.ManagerEmployeeId.ShouldBeNull();
    }

    [Fact]
    public void WouldCreateCycle_TerminatesOnPreExistingLoopElsewhere()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var map = new Dictionary<Guid, Guid?> { [a] = b, [b] = a, [employee] = null };

        ManagerHierarchy.WouldCreateCycle(employee, a, map).ShouldBeFalse();
    }

    [Fact]
    public void ReportsOf_ReturnsDirectAndIndirectReports()
    {
        var boss = Guid.NewGuid();
        var lead = Guid.NewGuid();
        var dev = Guid.NewGuid();
        var other = Guid.NewGuid();
        var map = new Dictionary<Guid, Guid?> { [boss] = null, [lead] = boss, [dev] = lead, [other] = null };

        ManagerHierarchy.ReportsOf(boss, map).ShouldBe([lead, dev], ignoreOrder: true);
        ManagerHierarchy.ReportsOf(dev, map).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("10000000146", "*******0146")]
    [InlineData(null, null)]
    public void MaskNationalId_ShowsOnlyLastFourDigits(string? input, string? expected) =>
        EmployeeEntity.MaskNationalId(input).ShouldBe(expected);

    [Fact]
    public void NextBirthday_HandlesPastThisYearAndLeapDay()
    {
        var today = new DateOnly(2026, 9, 15);

        BirthdayCalculator.NextBirthday(new DateOnly(1990, 9, 20), today).ShouldBe(new DateOnly(2026, 9, 20));
        BirthdayCalculator.NextBirthday(new DateOnly(1990, 9, 15), today).ShouldBe(today);
        BirthdayCalculator.NextBirthday(new DateOnly(1990, 1, 10), today).ShouldBe(new DateOnly(2027, 1, 10));
        BirthdayCalculator.NextBirthday(new DateOnly(1992, 2, 29), today).ShouldBe(new DateOnly(2027, 2, 28));
    }

    [Fact]
    public void UpcomingBirthdays_FiltersWindowAndOrdersByDate()
    {
        var today = new DateOnly(2026, 12, 20);
        var rows = new[]
        {
            Row("Later", new DateOnly(1990, 1, 10)),
            Row("Soon", new DateOnly(1985, 12, 25)),
            Row("Outside", new DateOnly(1985, 3, 1)),
            Row("NoBirthDate", null),
        };

        var result = BirthdayCalculator.Upcoming(rows, today, 30);

        result.Select(r => r.DisplayName).ShouldBe(["Soon", "Later"]);
        result[1].NextBirthday.ShouldBe(new DateOnly(2027, 1, 10));
    }

    [Fact]
    public void OrgChart_RootsAreEmployeesWithoutKnownManager_AndLoopsDoNotDropRows()
    {
        var ceo = Row("Ceo", null);
        var cto = Row("Cto", null, ceo.Id);
        var dev = Row("Dev", null, cto.Id);
        var orphan = Row("Orphan", null, Guid.NewGuid());
        var loopA = Row("LoopA", null, Guid.Empty);
        var loopB = Row("LoopB", null, loopA.Id);
        loopA = loopA with { ManagerEmployeeId = loopB.Id };

        var tree = OrgChartBuilder.Build([dev, cto, ceo, orphan, loopA, loopB]);

        tree.Select(n => n.DisplayName).ShouldBe(["Ceo", "Orphan", "LoopA"]);
        tree[0].Children.Single().DisplayName.ShouldBe("Cto");
        tree[0].Children.Single().Children.Single().DisplayName.ShouldBe("Dev");
        tree[2].Children.Single().DisplayName.ShouldBe("LoopB");
    }

    private static EmployeeRow Row(string name, DateOnly? birthDate, Guid? managerId = null) =>
        new(Guid.NewGuid(), name, "X", name, null, null, null, null, null, birthDate, EmployeeStatus.Active, null, managerId, null);
}

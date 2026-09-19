using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>Career history rules on the Employee aggregate (E2): timeline ordering, denormalization, manager transitions, hire-date alignment.</summary>
public sealed class CareerDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyLevel = Guid.NewGuid();
    private static readonly Guid DepartmentLevel = Guid.NewGuid();
    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid Sales = Guid.NewGuid();
    private static readonly Guid Finance = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2024, 1, 1);
    private static readonly DateOnly Today = new(2026, 9, 15);
    private static readonly Dictionary<Guid, int> Levels = new() { [CompanyLevel] = 1, [DepartmentLevel] = 3 };

    private static CareerContext Context(DateOnly? today = null, IReadOnlyDictionary<Guid, Guid?>? managers = null) =>
        new(today ?? Today, Levels, managers ?? new Dictionary<Guid, Guid?>());

    private static IReadOnlyList<OrgAssignmentInput> Placement(Guid department) =>
        [new OrgAssignmentInput(DepartmentLevel, department), new OrgAssignmentInput(CompanyLevel, Acme)];

    private static EmployeeEntity Hired(Guid? department = null, Guid? managerId = null, DateOnly? hireDate = null)
    {
        var employee = EmployeeEntity.Create(TenantId, new EmployeeInfo("Ali", "Kaya", HireDate: hireDate ?? HireDate), orgUnitId: department ?? Sales, managerEmployeeId: managerId).Value;
        employee.EnsureInitialCareerRecord(Placement(department ?? Sales), Today).ShouldNotBeNull();
        return employee;
    }

    [Fact]
    public void InitialRecord_StartsOnHireDate_UsesEmployeeIdAndIsCreatedOnlyOnce()
    {
        var employee = Hired();

        var record = employee.CareerTimeline.ShouldHaveSingleItem();
        record.Id.ShouldBe(employee.Id);
        record.ValidFrom.ShouldBe(HireDate);
        record.ValidTo.ShouldBeNull();
        record.ChangeType.ShouldBe(CareerChangeType.Hire);
        employee.EnsureInitialCareerRecord(Placement(Finance), Today).ShouldBeNull();

        var draft = EmployeeEntity.Create(TenantId, new EmployeeInfo("Can", "Ay")).Value;
        draft.EnsureInitialCareerRecord([], Today).ShouldBeNull();
        draft.PositionStartDate(Today).ShouldBeNull();
    }

    [Fact]
    public void AddRecord_ClosesPreviousAtDayBefore_KeepsOneOpenRecord_AndDenormalizesDeepestUnit()
    {
        var employee = Hired();
        var managerId = Guid.NewGuid();
        var from = new DateOnly(2025, 6, 1);

        var added = employee.AddCareerRecord(new CareerRecordData(from, CareerChangeType.Promotion, EmploymentType.FullTime, managerId, "Uzman → Müdür", Placement(Finance)), Context());

        added.IsSuccess.ShouldBeTrue();
        var timeline = employee.CareerTimeline;
        timeline.Count.ShouldBe(2);
        timeline[0].ValidTo.ShouldBe(new DateOnly(2025, 5, 31));
        timeline.Count(r => r.IsOpen).ShouldBe(1);
        employee.OrgUnitId.ShouldBe(Finance);
        employee.ManagerEmployeeId.ShouldBe(managerId);
        employee.EmploymentType.ShouldBe(EmploymentType.FullTime);
        employee.PositionStartDate(Today).ShouldBe(from);
        added.Value.ManagerTransition.ShouldBe(new ManagerTransition(null, managerId, from));
    }

    [Fact]
    public void AddRecord_RejectsStartNotAfterPrevious_BeforeHireDate_DuplicateLevel_AndSelfOrCyclicManager()
    {
        var employee = Hired();
        employee.AddCareerRecord(new CareerRecordData(HireDate, CareerChangeType.Other), Context()).Error.Code.ShouldBe(EmployeeDomainErrors.CareerValidFromInvalid);
        employee.AddCareerRecord(new CareerRecordData(HireDate.AddDays(-5), CareerChangeType.Other), Context()).Error.Code.ShouldBe(EmployeeDomainErrors.CareerValidFromInvalid);

        var fresh = EmployeeEntity.Create(TenantId, new EmployeeInfo("Ece", "Su", HireDate: HireDate)).Value;
        fresh.AddCareerRecord(new CareerRecordData(HireDate.AddDays(-1), CareerChangeType.Hire), Context()).Error.Code.ShouldBe(EmployeeDomainErrors.CareerBeforeHireDate);

        var duplicate = new CareerRecordData(Today, CareerChangeType.Other, Assignments: [new(DepartmentLevel, Sales), new(DepartmentLevel, Finance)]);
        employee.AddCareerRecord(duplicate, Context()).Error.Code.ShouldBe(EmployeeDomainErrors.CareerDuplicateLevel);

        employee.AddCareerRecord(new CareerRecordData(Today, CareerChangeType.ManagerChange, ManagerEmployeeId: employee.Id), Context()).Error.Code.ShouldBe(EmployeeDomainErrors.ManagerSelf);
        var report = Guid.NewGuid();
        var cycle = new Dictionary<Guid, Guid?> { [report] = employee.Id, [employee.Id] = null };
        employee.AddCareerRecord(new CareerRecordData(Today, CareerChangeType.ManagerChange, ManagerEmployeeId: report), Context(managers: cycle)).Error.Code.ShouldBe(EmployeeDomainErrors.ManagerCycle);

        employee.CareerTimeline.ShouldHaveSingleItem();
    }

    [Fact]
    public void FutureRecord_DoesNotChangeDenormalizedFieldsUntilItsDate()
    {
        var employee = Hired();
        var future = Today.AddDays(10);

        employee.AddCareerRecord(new CareerRecordData(future, CareerChangeType.LateralMove, Assignments: Placement(Finance)), Context()).IsSuccess.ShouldBeTrue();

        employee.OrgUnitId.ShouldBe(Sales);
        employee.CurrentCareerRecord(Today)!.ValidFrom.ShouldBe(HireDate);
        employee.CurrentCareerRecord(future)!.ValidFrom.ShouldBe(future);
        employee.PositionStartDate(Today).ShouldBe(HireDate);
    }

    [Fact]
    public void UpdateRecord_OnlyLatestMovesStart_AndManagerEditIsATransition()
    {
        var employee = Hired();
        var boss = Guid.NewGuid();
        var second = employee.AddCareerRecord(new CareerRecordData(new DateOnly(2025, 1, 1), CareerChangeType.Promotion, ManagerEmployeeId: boss, Assignments: Placement(Finance)), Context()).Value.Record;
        var first = employee.CareerTimeline[0];

        employee.UpdateCareerRecord(first.Id, new CareerRecordData(HireDate.AddDays(1), CareerChangeType.Hire, Assignments: Placement(Sales)), Context())
            .Error.Code.ShouldBe(EmployeeDomainErrors.CareerNotLatest);
        employee.UpdateCareerRecord(second.Id, new CareerRecordData(HireDate, CareerChangeType.Promotion, ManagerEmployeeId: boss, Assignments: Placement(Finance)), Context())
            .Error.Code.ShouldBe(EmployeeDomainErrors.CareerValidFromInvalid);
        employee.UpdateCareerRecord(Guid.NewGuid(), new CareerRecordData(Today, CareerChangeType.Other), Context()).Error.Code.ShouldBe(EmployeeDomainErrors.CareerRecordNotFound);

        var moved = employee.UpdateCareerRecord(second.Id, new CareerRecordData(new DateOnly(2025, 3, 1), CareerChangeType.Promotion, ManagerEmployeeId: boss, Assignments: Placement(Finance)), Context());
        moved.IsSuccess.ShouldBeTrue();
        first.ValidTo.ShouldBe(new DateOnly(2025, 2, 28));
        moved.Value.ManagerTransition.ShouldBe(new ManagerTransition(null, boss, new DateOnly(2025, 3, 1)));

        var noOp = employee.UpdateCareerRecord(second.Id, new CareerRecordData(new DateOnly(2025, 3, 1), CareerChangeType.Promotion, ManagerEmployeeId: boss, Assignments: Placement(Finance)), Context());
        noOp.Value.Changed.ShouldBeFalse();
        noOp.Value.ManagerTransition.ShouldBeNull();

        var newBoss = Guid.NewGuid();
        var managerEdit = employee.UpdateCareerRecord(second.Id, new CareerRecordData(new DateOnly(2025, 3, 1), CareerChangeType.Promotion, ManagerEmployeeId: newBoss, Assignments: Placement(Finance)), Context());
        managerEdit.Value.ManagerTransition.ShouldBe(new ManagerTransition(boss, newBoss, new DateOnly(2025, 3, 1)));
        employee.ManagerEmployeeId.ShouldBe(newBoss);

        // Assignment edit of a closed record is allowed (only its start is frozen).
        employee.UpdateCareerRecord(first.Id, new CareerRecordData(HireDate, CareerChangeType.Hire, Note: "düzeltme", Assignments: [new(DepartmentLevel, Sales)]), Context()).Value.Changed.ShouldBeTrue();
        first.Assignments.ShouldHaveSingleItem().OrgUnitId.ShouldBe(Sales);
    }

    [Fact]
    public void DeleteRecord_OnlyLatest_ReopensPrevious_RestoresDenormalizedFields_AndNeverTheLastOne()
    {
        var boss = Guid.NewGuid();
        var employee = Hired(managerId: boss);
        var initial = employee.CareerTimeline[0];
        var newBoss = Guid.NewGuid();
        var latest = employee.AddCareerRecord(new CareerRecordData(new DateOnly(2025, 1, 1), CareerChangeType.ManagerChange, ManagerEmployeeId: newBoss, Assignments: Placement(Finance)), Context()).Value.Record;

        employee.DeleteCareerRecord(initial.Id, Context()).Error.Code.ShouldBe(EmployeeDomainErrors.CareerNotLatest);

        var deleted = employee.DeleteCareerRecord(latest.Id, Context());
        deleted.IsSuccess.ShouldBeTrue();
        deleted.Value.Record.ShouldBeSameAs(initial);
        deleted.Value.ManagerTransition.ShouldBe(new ManagerTransition(newBoss, boss, new DateOnly(2025, 1, 1)));
        initial.ValidTo.ShouldBeNull();
        employee.ManagerEmployeeId.ShouldBe(boss);
        employee.OrgUnitId.ShouldBe(Sales);

        employee.DeleteCareerRecord(initial.Id, Context()).Error.Code.ShouldBe(EmployeeDomainErrors.CareerLastRecord);
    }

    [Fact]
    public void ApplyPlacement_NoOp_AmendsRecordStartingToday_OtherwiseOpensRecordEffectiveToday()
    {
        var employee = Hired();
        var boss = Guid.NewGuid();

        employee.ApplyPlacement(new PlacementRequest(Placement(Sales), null, null, CareerChangeType.ManagerChange), Context()).Value.Changed.ShouldBeFalse();

        var moved = employee.ApplyPlacement(new PlacementRequest(Placement(Sales), boss, null, CareerChangeType.ManagerChange), Context());
        moved.Value.Record.ValidFrom.ShouldBe(Today);
        moved.Value.Record.ChangeType.ShouldBe(CareerChangeType.ManagerChange);
        moved.Value.ManagerTransition.ShouldBe(new ManagerTransition(null, boss, Today));
        employee.CareerTimeline.Count.ShouldBe(2);

        var amended = employee.ApplyPlacement(new PlacementRequest(Placement(Finance), boss, null, CareerChangeType.Other), Context());
        amended.Value.Record.ShouldBeSameAs(moved.Value.Record);
        amended.Value.ManagerTransition.ShouldBeNull();
        employee.CareerTimeline.Count.ShouldBe(2);
        employee.OrgUnitId.ShouldBe(Finance);

        var untouched = EmployeeEntity.Create(TenantId, new EmployeeInfo("Nur", "Ak")).Value;
        var first = untouched.ApplyPlacement(new PlacementRequest(Placement(Sales), boss, EmploymentType.PartTime, CareerChangeType.ManagerChange), Context());
        first.Value.Record.ChangeType.ShouldBe(CareerChangeType.Hire);
        first.Value.Record.ValidFrom.ShouldBe(Today);
        untouched.OrgUnitId.ShouldBe(Sales);
        untouched.EmploymentType.ShouldBe(EmploymentType.PartTime);
    }

    [Fact]
    public void HireDateCorrection_RealignsUntouchedInitialRecord_ButCannotPassACustomPositionStart()
    {
        var employee = Hired();
        var corrected = new DateOnly(2024, 2, 1);
        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: corrected)).IsSuccess.ShouldBeTrue();
        employee.CareerTimeline[0].ValidFrom.ShouldBe(corrected);

        // Training period: position starts 90 days after the hire date - a later hire date may not overtake it.
        var trainee = Hired();
        var start = HireDate.AddDays(90);
        trainee.UpdateCareerRecord(trainee.Id, new CareerRecordData(start, CareerChangeType.Hire, Assignments: Placement(Sales)), Context()).IsSuccess.ShouldBeTrue();
        trainee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: start.AddDays(1))).Error.Code.ShouldBe(EmployeeDomainErrors.CareerBeforeHireDate);
        trainee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: start)).IsSuccess.ShouldBeTrue();
        trainee.CareerTimeline[0].ValidFrom.ShouldBe(start);
    }

    [Fact]
    public void LeaveAccrualStart_DefaultsToFirstHireDate_FollowsCorrections_AndKeepsExplicitEarlierDate()
    {
        var employee = EmployeeEntity.Create(TenantId, new EmployeeInfo("Ali", "Kaya")).Value;
        employee.LeaveAccrualStartDate.ShouldBeNull();

        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate)).IsSuccess.ShouldBeTrue();
        employee.LeaveAccrualStartDate.ShouldBe(HireDate);

        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate.AddDays(3))).IsSuccess.ShouldBeTrue();
        employee.LeaveAccrualStartDate.ShouldBe(HireDate.AddDays(3));

        var groupTransfer = new DateOnly(2019, 5, 1);
        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate.AddDays(3), LeaveAccrualStartDate: groupTransfer)).IsSuccess.ShouldBeTrue();
        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate.AddDays(4))).IsSuccess.ShouldBeTrue();
        employee.LeaveAccrualStartDate.ShouldBe(groupTransfer);
    }

    /// <summary>
    /// senseik-044: the "temizle" (X) button in the web form must actually persist an empty value. Before
    /// <see cref="EmployeeInfo.ClearLeaveAccrualStartDate"/> existed, a plain <c>null</c> LeaveAccrualStartDate could
    /// never mean "clear it" - that shape was already claimed by "untouched, keep following HireDate" (the test
    /// above). The explicit flag wins outright, even over a customized (non-hire-date-following) value.
    /// </summary>
    [Fact]
    public void LeaveAccrualStart_ExplicitClearFlag_ForcesNull_EvenOverACustomizedValue_AndSurvivesUnrelatedSaves()
    {
        var employee = EmployeeEntity.Create(TenantId, new EmployeeInfo("Ali", "Kaya", HireDate: HireDate)).Value;
        employee.LeaveAccrualStartDate.ShouldBe(HireDate);

        var groupTransfer = new DateOnly(2019, 5, 1);
        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate, LeaveAccrualStartDate: groupTransfer)).IsSuccess.ShouldBeTrue();
        employee.LeaveAccrualStartDate.ShouldBe(groupTransfer);

        // The card's actual complaint: temizle (X) + kaydet + kartı yenile must show empty, not the old value.
        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate, ClearLeaveAccrualStartDate: true)).IsSuccess.ShouldBeTrue();
        employee.LeaveAccrualStartDate.ShouldBeNull();

        // A later, unrelated save (hire date unchanged, field untouched) must not bring the value back...
        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate, WorkPhone: "02120000000")).IsSuccess.ShouldBeTrue();
        employee.LeaveAccrualStartDate.ShouldBeNull();

        // ...but a hire-date correction re-derives it (AC2: accrual follows the corrected hire date).
        employee.UpdateInfo(new EmployeeInfo("Ali", "Kaya", HireDate: HireDate.AddDays(5))).IsSuccess.ShouldBeTrue();
        employee.LeaveAccrualStartDate.ShouldBe(HireDate.AddDays(5));
    }

    [Fact]
    public void OrgPlacement_ChainForUnit_AddsShallowerAncestorsOnly_AndValidatesLevel()
    {
        var company = new OrgUnitTypeInfo(CompanyLevel, "Company", "Şirket", 1);
        var department = new OrgUnitTypeInfo(DepartmentLevel, "Department", "Departman", 3);
        var emea = Guid.NewGuid();
        var placement = new OrgPlacement(
            [department, company],
            [new OrgUnitInfo(Acme, CompanyLevel, null, "Acme"), new OrgUnitInfo(Sales, DepartmentLevel, Acme, "Sales"), new OrgUnitInfo(emea, DepartmentLevel, Sales, "Sales EMEA")]);

        placement.ChainFor(emea).ShouldBe([new OrgAssignmentInput(DepartmentLevel, emea), new OrgAssignmentInput(CompanyLevel, Acme)]);
        placement.ChainFor(Acme).ShouldBe([new OrgAssignmentInput(CompanyLevel, Acme)]);
        placement.ChainFor(Guid.NewGuid()).ShouldBeEmpty();
        placement.IsValidAssignment(DepartmentLevel, Sales).ShouldBeTrue();
        placement.IsValidAssignment(CompanyLevel, Sales).ShouldBeFalse();
    }
}

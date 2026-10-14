using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Incentives;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>Incentive assignment handlers, the profile-code migration tool and the month plan (PAY-INCENTIVE-2).</summary>
public sealed class IncentiveAssignmentFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeA = Guid.NewGuid();
    private static readonly Guid EmployeeB = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeIncentiveDefinitionRepository _definitions = new();
    private readonly FakeIncentiveAssignmentRepository _assignments = new();
    private readonly IPayrollPeriodRepository _periods = Substitute.For<IPayrollPeriodRepository>();
    private readonly IPayslipDirtyFlagRepository _flags = Substitute.For<IPayslipDirtyFlagRepository>();
    private readonly IEmployeeDirectory _directory = Substitute.For<IEmployeeDirectory>();
    private readonly IEmployeePayrollProfileRepository _profiles = Substitute.For<IEmployeePayrollProfileRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly TimeProvider _clock = Substitute.For<TimeProvider>();
    private readonly IncentiveChangeFlagger _flagger;

    public IncentiveAssignmentFeaturesTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _clock.GetUtcNow().Returns(Now);
        _periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EmployeeContact>>(
                call.ArgAt<IReadOnlyCollection<Guid>>(1).Select(id => new EmployeeContact(id, "Ayşe", "Yılmaz", null, null, null, "Active")).ToList()));
        _profiles.ListActiveAsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>([]));
        _flagger = new IncentiveChangeFlagger(_tenant, _periods, _assignments, new PayslipDirtyFlagService(_periods, _flags, _clock));
    }

    private static IncentiveDefinitionData Data(
        IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount, string? declarationCode = "06111", int? maxMonths = null, bool complete = true) =>
        new("Test teşviki", "Test Kanunu", declarationCode, kind, complete ? IncentiveDefinitionTests.DefaultParameters(kind) : IncentiveParameters.Empty, maxMonths, new DateOnly(2026, 1, 1), null);

    private IncentiveDefinition Stored(string code, IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount, string? declarationCode = "06111", int? maxMonths = null, bool complete = true)
    {
        var definition = IncentiveDefinition.Create(TenantId, code, Data(kind, declarationCode, maxMonths, complete)).Value;
        _definitions.Add(definition);
        return definition;
    }

    private PayrollPeriod ClosedPeriod(int year, int month)
    {
        var period = PayrollPeriod.Create(TenantId, year, month).Value;
        period.Approve(null);
        _periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([period]));
        return period;
    }

    private PayrollPeriod OpenPeriod(int year, int month)
    {
        var period = PayrollPeriod.Create(TenantId, year, month).Value;
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([period]));
        _periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<PayrollPeriod>>(
                new[] { period }.Where(p => p.Year > call.Arg<DateOnly>().Year || (p.Year == call.Arg<DateOnly>().Year && p.Month >= call.Arg<DateOnly>().Month)).ToList()));
        return period;
    }

    private EmployeeIncentiveAssignment Assigned(IncentiveDefinition definition, Guid employeeId, DateOnly from, DateOnly? to)
    {
        var assignment = EmployeeIncentiveAssignment.Create(
            TenantId, employeeId, definition, from, to, null, new HashSet<DateOnly>(), _assignments.Items.Where(a => a.EmployeeId == employeeId).ToList(),
            id => _definitions.Items.Single(d => d.Id == id).Category).Value;
        _assignments.Add(assignment);
        return assignment;
    }

    private CreateIncentiveAssignmentsHandler CreateHandler() =>
        new(_tenant, _periods, _definitions, _assignments, _directory, _flagger);

    private ConvertLegacyIncentiveCodeHandler ConvertHandler() =>
        new(_tenant, _profiles, _periods, _definitions, _assignments, _flagger, _clock);

    private static DateOnly M(int year, int month) => new(year, month, 1);

    private static EmployeePayrollProfile Profile(Guid employeeId, string? incentiveCode) =>
        EmployeePayrollProfile.Create(
            TenantId, employeeId,
            new EmployeePayrollProfileData(
                Guid.NewGuid(), null, new DateOnly(2025, 1, 1), null, WageBasis.Monthly, SgkStatus.Normal, incentiveCode, MealBenefitVariant.None, 0m,
                TransportVariant.None, PayrollPaymentMethod.Bank, null, false, null, null)).Value;

    [Fact]
    public async Task Assign_ToSeveralEmployees_CreatesAllAndFlagsThemInOpenPeriods()
    {
        var definition = Stored("SGK_A");
        var period = OpenPeriod(2026, 6);

        var result = await CreateHandler().Handle(
            new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA, EmployeeB, EmployeeA], 2026, 6, 2026, 12, "ilk işe giriş"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedIds.Count.ShouldBe(2);
        _assignments.Items.Select(a => a.EmployeeId).ShouldBe([EmployeeA, EmployeeB], ignoreOrder: true);
        _assignments.Items.ShouldAllBe(a => a.ValidFromMonth == M(2026, 6) && a.ValidToMonth == M(2026, 12) && a.Note == "ilk işe giriş");
        _flags.Received(2).Add(Arg.Is<PayslipDirtyFlag>(f => f.PayrollPeriodId == period.Id && f.Reasons.Contains(PayslipDirtyFlagReasons.IncentiveChanged)));
    }

    [Fact]
    public async Task Assign_AnUnapprovedDefinition_IsAllowed()
    {
        var definition = Stored("SGK_DRAFT", complete: false);

        var result = await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA], 2026, 6, null, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.IsSmmmApproved.ShouldBeFalse();
    }

    [Fact]
    public async Task Assign_IsAllOrNothing_WhenOneEmployeeConflicts()
    {
        var definition = Stored("SGK_A");
        var other = Stored("SGK_B", declarationCode: "07103");
        Assigned(other, EmployeeB, M(2026, 5), null);
        var before = _assignments.Items.Count;
        OpenPeriod(2026, 6);

        var result = await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA, EmployeeB], 2026, 6, null, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        _assignments.Items.Count.ShouldBe(before);
        _flags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());
    }

    [Fact]
    public async Task Assign_ASgkAndATaxIncentive_AtTheSameTime_IsAllowed()
    {
        var sgk = Stored("SGK_A");
        var tax = Stored("ARGE_GV", IncentiveKind.IncomeTaxWithholdingRelief, declarationCode: null);
        Assigned(sgk, EmployeeA, M(2026, 1), null);

        var result = await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(tax.Id, [EmployeeA], 2026, 6, null, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _assignments.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Assign_WithAnUnknownEmployee_IsNotFound_AndNothingIsWritten()
    {
        var definition = Stored("SGK_A");
        _directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(EmployeeA, "Ayşe", "Yılmaz", null, null, null, "Active")]));

        var result = await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA, EmployeeB], 2026, 6, null, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemEmployeeNotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        _assignments.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Assign_AnUnknownDefinition_IsNotFound()
    {
        var result = await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(Guid.NewGuid(), [EmployeeA], 2026, 6, null, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveNotFound);
    }

    [Fact]
    public async Task Assign_ToAClosedMonth_IsRefused()
    {
        var definition = Stored("SGK_A");
        ClosedPeriod(2026, 5);

        var result = await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA], 2026, 5, null, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
        _assignments.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Assign_AnInactiveDefinition_IsRefused()
    {
        var definition = Stored("SGK_A");
        definition.Deactivate();

        (await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA], 2026, 6, null, null, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.IncentiveInactive);
    }

    [Fact]
    public async Task Assign_WithMaxMonths_NeedsAnEndMonth_AndCannotExceedIt()
    {
        var definition = Stored("SGK_A", maxMonths: 6);
        var handler = CreateHandler();

        (await handler.Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA], 2026, 6, null, null, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentEndRequired);
        (await handler.Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA], 2026, 6, 2026, 12, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentExceedsMaxMonths);
        (await handler.Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA], 2026, 6, 2026, 11, null), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Assign_ASgkDefinitionWithoutDeclarationCode_IsRefused()
    {
        var definition = Stored("SGK_A", declarationCode: null);

        (await CreateHandler().Handle(new CreateIncentiveAssignmentsCommand(definition.Id, [EmployeeA], 2026, 6, null, null, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.IncentiveDeclarationCodeRequired);
    }

    [Fact]
    public void AssignValidator_RejectsMoreThan500Employees_AndNone()
    {
        var validator = new CreateIncentiveAssignmentsValidator();
        var definitionId = Guid.NewGuid();

        validator.Validate(new CreateIncentiveAssignmentsCommand(definitionId, Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList(), 2026, 6, null, null, null)).IsValid.ShouldBeTrue();
        validator.Validate(new CreateIncentiveAssignmentsCommand(definitionId, Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList(), 2026, 6, null, null, null)).IsValid.ShouldBeFalse();
        validator.Validate(new CreateIncentiveAssignmentsCommand(definitionId, [], 2026, 6, null, null, null)).IsValid.ShouldBeFalse();
        validator.Validate(new CreateIncentiveAssignmentsCommand(definitionId, [EmployeeA], 2026, 13, null, null, null)).IsValid.ShouldBeFalse();
        validator.Validate(new CreateIncentiveAssignmentsCommand(definitionId, [EmployeeA], 2026, 6, 2026, null, null)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_EndsAnOpenAssignment_AndFlagsTheEmployee()
    {
        var definition = Stored("SGK_A");
        var assignment = Assigned(definition, EmployeeA, M(2026, 1), null);
        OpenPeriod(2026, 9);
        var handler = new UpdateIncentiveAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdateIncentiveAssignmentCommand(assignment.Id, 2026, 12, "bitiyor"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        assignment.ValidToMonth.ShouldBe(M(2026, 12));
        assignment.Note.ShouldBe("bitiyor");
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA && f.Reasons.Contains(PayslipDirtyFlagReasons.IncentiveChanged)));
    }

    [Fact]
    public async Task Update_CannotExtendIntoAnOverlapOfTheSameCategory()
    {
        var first = Stored("SGK_A");
        var second = Stored("SGK_B", declarationCode: "07103");
        var assignment = Assigned(first, EmployeeA, M(2026, 1), M(2026, 3));
        Assigned(second, EmployeeA, M(2026, 4), null);
        var handler = new UpdateIncentiveAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdateIncentiveAssignmentCommand(assignment.Id, 2026, 6, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        assignment.ValidToMonth.ShouldBe(M(2026, 3));
    }

    [Fact]
    public async Task Update_CannotCutOffAClosedMonth()
    {
        var definition = Stored("SGK_A");
        var assignment = Assigned(definition, EmployeeA, M(2026, 1), null);
        ClosedPeriod(2026, 5);
        var handler = new UpdateIncentiveAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        (await handler.Handle(new UpdateIncentiveAssignmentCommand(assignment.Id, 2026, 3, null), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
        (await handler.Handle(new UpdateIncentiveAssignmentCommand(assignment.Id, 2026, 6, null), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_OfAnUnknownAssignment_IsNotFound()
    {
        var handler = new UpdateIncentiveAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        (await handler.Handle(new UpdateIncentiveAssignmentCommand(Guid.NewGuid(), null, null, null), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveAssignmentNotFound);
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedAssignment_AndFlags_ButRefusesAUsedOne()
    {
        var definition = Stored("SGK_A");
        var unused = Assigned(definition, EmployeeA, M(2026, 7), null);
        var used = Assigned(definition, EmployeeB, M(2026, 1), null);
        ClosedPeriod(2026, 5);
        OpenPeriod(2026, 8);
        var handler = new DeleteIncentiveAssignmentHandler(_periods, _assignments, _flagger);

        (await handler.Handle(new DeleteIncentiveAssignmentCommand(unused.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await handler.Handle(new DeleteIncentiveAssignmentCommand(used.Id), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
        (await handler.Handle(new DeleteIncentiveAssignmentCommand(Guid.NewGuid()), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveAssignmentNotFound);

        _assignments.Items.ShouldBe([used]);
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));
    }

    [Fact]
    public async Task List_ByEmployee_AndByPeriod_ShowDefinitionDetails()
    {
        var definition = Stored("SGK_A");
        Assigned(definition, EmployeeA, M(2026, 1), M(2026, 6));
        Assigned(definition, EmployeeB, M(2026, 7), null);
        var period = PayrollPeriod.Create(TenantId, 2026, 7).Value;
        _periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var handler = new ListIncentiveAssignmentsHandler(_periods, _assignments, _definitions, _directory, _tenant);

        var byEmployee = (await handler.Handle(new ListIncentiveAssignmentsQuery(EmployeeA, null), CancellationToken.None)).Value;
        var byPeriod = (await handler.Handle(new ListIncentiveAssignmentsQuery(null, period.Id), CancellationToken.None)).Value;

        var dto = byEmployee.Single();
        dto.Code.ShouldBe("SGK_A");
        dto.DeclarationCode.ShouldBe("06111");
        dto.Kind.ShouldBe(IncentiveKind.EmployerSgkRateDiscount);
        dto.IsSmmmApproved.ShouldBeFalse();
        (dto.FromYear, dto.FromMonth, dto.ToYear, dto.ToMonth).ShouldBe((2026, 1, 2026, 6));
        dto.EmployeeName.ShouldBe("Ayşe Yılmaz");
        byPeriod.Select(a => a.EmployeeId).ShouldBe([EmployeeB]);
    }

    [Fact]
    public void ListValidator_RequiresAnEmployeeOrAPeriod()
    {
        var validator = new ListIncentiveAssignmentsValidator();

        validator.Validate(new ListIncentiveAssignmentsQuery(null, null)).IsValid.ShouldBeFalse();
        validator.Validate(new ListIncentiveAssignmentsQuery(EmployeeA, null)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("5510", "05510")]
    [InlineData("05510", "05510")]
    [InlineData(" 5510 ", "05510")]
    [InlineData("27103", "27103")]
    [InlineData("7103", "07103")]
    [InlineData("5510-A", "05510")]
    [InlineData("123456", "123456")]
    [InlineData("abc", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_KeepsOnlyDigits_AndPadsToFive(string? raw, string expected) =>
        LegacyIncentiveCodes.Normalize(raw).ShouldBe(expected);

    [Fact]
    public async Task LegacyPreview_MapsCodesToDefinitions_AndSaysWhenTheCalculationChanges()
    {
        var approvedDiscount = Stored("SGK_5510", declarationCode: "05510");
        approvedDiscount.Update(Data(declarationCode: "05510") with
        {
            Parameters = new IncentiveParameters { Rate = IncentiveRate.Key(IncentiveTemplates.EmployerDiscountRateKey) },
        }).IsSuccess.ShouldBeTrue();
        approvedDiscount.Approve(Guid.NewGuid(), Now, null);
        Stored("SGK_6111", IncentiveKind.EmployerShareCoverage, declarationCode: "06111");
        var unmatchedEmployee = Guid.NewGuid();
        _profiles.ListActiveAsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>(
        [
            Profile(EmployeeA, "5510"),
            Profile(EmployeeB, "05510"),
            Profile(Guid.NewGuid(), "5510"),
            Profile(Guid.NewGuid(), "6111"),
            Profile(unmatchedEmployee, "99999"),
            Profile(Guid.NewGuid(), null),
            Profile(Guid.NewGuid(), "  "),
        ]));

        var preview = (await new ListLegacyIncentiveCodesHandler(_profiles, _definitions, _clock)
            .Handle(new ListLegacyIncentiveCodesQuery(), CancellationToken.None)).Value;

        preview.Count.ShouldBe(4);
        var short5510 = preview.Single(p => p.Code == "5510");
        short5510.NormalizedCode.ShouldBe("05510");
        short5510.EmployeeCount.ShouldBe(2);
        short5510.SuggestedDefinitionCode.ShouldBe("SGK_5510");
        short5510.SuggestedDefinitionId.ShouldBe(approvedDiscount.Id);
        short5510.SuggestedIsApproved.ShouldBe(true);
        short5510.CalculationChanges.ShouldBeFalse();

        var full5510 = preview.Single(p => p.Code == "05510");
        full5510.EmployeeCount.ShouldBe(1);
        full5510.SuggestedDefinitionCode.ShouldBe("SGK_5510");

        var code6111 = preview.Single(p => p.Code == "6111");
        code6111.NormalizedCode.ShouldBe("06111");
        code6111.SuggestedDefinitionCode.ShouldBe("SGK_6111");
        code6111.SuggestedIsApproved.ShouldBe(false);
        code6111.CalculationChanges.ShouldBeTrue();

        var nothing = preview.Single(p => p.Code == "99999");
        nothing.SuggestedDefinitionId.ShouldBeNull();
        nothing.SuggestedDefinitionCode.ShouldBeNull();
        nothing.SuggestedIsApproved.ShouldBeNull();
        nothing.CalculationChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task LegacyPreview_AnUnapprovedRateDiscount_ChangesTheCalculation()
    {
        Stored("SGK_5510", declarationCode: "05510");
        _profiles.ListActiveAsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>([Profile(EmployeeA, "5510")]));

        var preview = (await new ListLegacyIncentiveCodesHandler(_profiles, _definitions, _clock)
            .Handle(new ListLegacyIncentiveCodesQuery(new DateOnly(2026, 3, 1)), CancellationToken.None)).Value;

        preview.Single().CalculationChanges.ShouldBeTrue();
        await _profiles.Received(1).ListActiveAsOfAsync(new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Convert_CreatesAssignmentsForTheCodeHolders_AndLeavesTheProfilesAlone()
    {
        var definition = Stored("SGK_5510", declarationCode: "05510");
        var holderA = Profile(EmployeeA, "5510");
        var holderB = Profile(EmployeeB, "05510");
        var other = Profile(Guid.NewGuid(), "6111");
        _profiles.ListActiveAsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>([holderA, holderB, other]));
        var period = OpenPeriod(2026, 6);

        var result = await ConvertHandler().Handle(new ConvertLegacyIncentiveCodeCommand("5510", definition.Id, 2026, 6, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedIds.Count.ShouldBe(2);
        result.Value.SkippedEmployeeIds.ShouldBeEmpty();
        _assignments.Items.Select(a => a.EmployeeId).ShouldBe([EmployeeA, EmployeeB], ignoreOrder: true);
        _assignments.Items.ShouldAllBe(a => a.ValidFromMonth == M(2026, 6) && a.ValidToMonth == null);
        holderA.IncentiveLawCode.ShouldBe("5510");
        holderB.IncentiveLawCode.ShouldBe("05510");
        _flags.Received(2).Add(Arg.Is<PayslipDirtyFlag>(f => f.PayrollPeriodId == period.Id));
    }

    [Fact]
    public async Task Convert_SkipsEmployeesWithAnOverlappingSgkAssignment_AndReportsThem()
    {
        var definition = Stored("SGK_5510", declarationCode: "05510");
        var other = Stored("SGK_6111", declarationCode: "06111");
        Assigned(other, EmployeeB, M(2026, 1), null);
        _profiles.ListActiveAsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>([Profile(EmployeeA, "5510"), Profile(EmployeeB, "5510")]));

        var result = await ConvertHandler().Handle(new ConvertLegacyIncentiveCodeCommand("05510", definition.Id, 2026, 6, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SkippedEmployeeIds.ShouldBe([EmployeeB]);
        result.Value.CreatedIds.Count.ShouldBe(1);
        _assignments.Items.Count(a => a.IncentiveDefinitionId == definition.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Convert_ToAClosedMonth_IsRefused_AndNothingIsWritten()
    {
        var definition = Stored("SGK_5510", declarationCode: "05510");
        ClosedPeriod(2026, 5);
        _profiles.ListActiveAsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>([Profile(EmployeeA, "5510")]));

        var result = await ConvertHandler().Handle(new ConvertLegacyIncentiveCodeCommand("5510", definition.Id, 2026, 4, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
        _assignments.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Convert_WithATaxDefinition_OrAnUnknownOne_OrNoDigits_IsRefused()
    {
        var tax = Stored("ARGE_GV", IncentiveKind.IncomeTaxWithholdingRelief, declarationCode: null);
        var sgk = Stored("SGK_5510", declarationCode: "05510");
        var handler = ConvertHandler();

        (await handler.Handle(new ConvertLegacyIncentiveCodeCommand("5510", tax.Id, 2026, 6, null, null), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
        (await handler.Handle(new ConvertLegacyIncentiveCodeCommand("5510", Guid.NewGuid(), 2026, 6, null, null), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveNotFound);
        (await handler.Handle(new ConvertLegacyIncentiveCodeCommand("abc", sgk.Id, 2026, 6, null, null), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveDeclarationCodeInvalid);
    }

    [Fact]
    public async Task MonthPlan_LoadsAssignmentsAndDefinitionsOnce_ForAnyNumberOfEmployees()
    {
        var sgk = Stored("SGK_A");
        var tax = Stored("ARGE_GV", IncentiveKind.IncomeTaxWithholdingRelief, declarationCode: null);
        var expired = Stored("SGK_OLD", declarationCode: "07103");
        Assigned(sgk, EmployeeA, M(2026, 1), null);
        Assigned(tax, EmployeeA, M(2026, 3), M(2026, 9));
        Assigned(sgk, EmployeeB, M(2026, 6), M(2026, 8));
        Assigned(expired, Guid.NewGuid(), M(2025, 1), M(2025, 12));
        _definitions.Items.Single(d => d.Id == sgk.Id).Deactivate();

        var plan = await IncentiveMonthPlan.LoadAsync(new DateOnly(2026, 6, 17), _assignments, _definitions);

        _assignments.CoveringMonthCalls.ShouldBe(1);
        _definitions.ListByIdsCalls.ShouldBe(1);
        plan.MonthStart.ShouldBe(M(2026, 6));
        plan.Definitions.Keys.ShouldBe([sgk.Id, tax.Id], ignoreOrder: true);
        plan.ForEmployee(EmployeeA).Select(p => p.Definition.Code).ShouldBe(["SGK_A", "ARGE_GV"]);
        plan.ForEmployee(EmployeeB).Select(p => p.Definition.Code).ShouldBe(["SGK_A"]);
        plan.ForEmployee(Guid.NewGuid()).ShouldBeEmpty();
        _assignments.CoveringMonthCalls.ShouldBe(1);
        _definitions.ListByIdsCalls.ShouldBe(1);

        // SGK_A was deactivated: still planned, but not effective.
        plan.EffectiveForEmployee(EmployeeA).Select(p => p.Definition.Code).ShouldBe(["ARGE_GV"]);
        plan.EffectiveOfCategory(EmployeeA, IncentiveCategory.Sgk).ShouldBeNull();
        plan.EffectiveOfCategory(EmployeeA, IncentiveCategory.Tax)!.Definition.Id.ShouldBe(tax.Id);
    }

    [Fact]
    public async Task MonthPlan_WithNoAssignments_IsEmpty_AndSkipsTheDefinitionQuery()
    {
        var plan = await IncentiveMonthPlan.LoadAsync(M(2026, 6), _assignments, _definitions);

        plan.Definitions.ShouldBeEmpty();
        plan.ForEmployee(EmployeeA).ShouldBeEmpty();
        _definitions.ListByIdsCalls.ShouldBe(0);
    }
}

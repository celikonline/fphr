using System.Reflection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>Application handlers of pay item definitions, formula validation and assignments (PAY-FORMULA-2). Repositories are NSubstitute fakes.</summary>
public sealed class PayItemFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeA = Guid.NewGuid();
    private static readonly Guid EmployeeB = Guid.NewGuid();

    private readonly IPayrollPeriodRepository _periods = Substitute.For<IPayrollPeriodRepository>();
    private readonly IPayItemDefinitionRepository _definitions = Substitute.For<IPayItemDefinitionRepository>();
    private readonly IEmployeePayItemAssignmentRepository _assignments = Substitute.For<IEmployeePayItemAssignmentRepository>();
    private readonly IPayslipDirtyFlagRepository _flags = Substitute.For<IPayslipDirtyFlagRepository>();
    private readonly IEmployeeDirectory _directory = Substitute.For<IEmployeeDirectory>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly PayItemChangeFlagger _flagger;

    public PayItemFeaturesTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _assignments.ListByEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([]));
        _directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<EmployeeContact>>(
                call.ArgAt<IReadOnlyCollection<Guid>>(1).Select(id => new EmployeeContact(id, "Ayşe", "Yılmaz", null, null, null, "Active")).ToList()));
        _flagger = new PayItemChangeFlagger(_tenant, _periods, _assignments, new PayslipDirtyFlagService(_periods, _flags, TimeProvider.System));
    }

    private static PayItemDefinitionInput Input(string formula = "deger", PayItemDirection direction = PayItemDirection.Earning) =>
        new("Yemek yardımı", direction, PayItemAmountBasis.Gross, true, true, true, formula, null, new DateOnly(2026, 1, 1), null);

    private PayItemDefinition Stored(string formula = "deger")
    {
        var definition = PayItemDefinition.Create(TenantId, "YEMEK", Input(formula).ToDataForTest()).Value;
        _definitions.FindByIdAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayItemDefinition?>(definition));
        return definition;
    }

    private PayrollPeriod OpenPeriod(int year, int month)
    {
        var period = PayrollPeriod.Create(TenantId, year, month).Value;
        _periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<PayrollPeriod>>(
                new[] { period }.Where(p => p.Year > call.Arg<DateOnly>().Year || (p.Year == call.Arg<DateOnly>().Year && p.Month >= call.Arg<DateOnly>().Month)).ToList()));
        return period;
    }

    [Fact]
    public async Task Create_StoresTheDefinition()
    {
        var handler = new CreatePayItemHandler(_tenant, _definitions, TimeProvider.System);

        var result = await handler.Handle(new CreatePayItemCommand("YEMEK", Input()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _definitions.Received(1).Add(Arg.Is<PayItemDefinition>(d => d.Code == "YEMEK" && d.TenantId == TenantId && d.IsActive));
    }

    [Fact]
    public async Task Create_WithoutValidFrom_StartsToday()
    {
        var handler = new CreatePayItemHandler(_tenant, _definitions, TimeProvider.System);
        var input = Input() with { ValidFrom = null };

        await handler.Handle(new CreatePayItemCommand("YEMEK", input), CancellationToken.None);

        _definitions.Received(1).Add(Arg.Is<PayItemDefinition>(d => d.ValidFrom == DateOnly.FromDateTime(DateTime.UtcNow) || d.ValidFrom == DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
    }

    [Fact]
    public async Task Create_WithATakenCode_IsAConflict()
    {
        _definitions.CodeExistsAsync("YEMEK", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreatePayItemHandler(_tenant, _definitions, TimeProvider.System);

        var result = await handler.Handle(new CreatePayItemCommand("YEMEK", Input()), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemCodeTaken);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        _definitions.DidNotReceive().Add(Arg.Any<PayItemDefinition>());
    }

    [Fact]
    public async Task Create_RejectsADeductionWithNetBasis_AndABadFormula()
    {
        var handler = new CreatePayItemHandler(_tenant, _definitions, TimeProvider.System);

        (await handler.Handle(new CreatePayItemCommand("KESINTI", Input(direction: PayItemDirection.Deduction) with { AmountBasis = PayItemAmountBasis.Net }), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemDeductionBasisInvalid);
        (await handler.Handle(new CreatePayItemCommand("YEMEK", Input(formula: "deger +")), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.FormulaSyntax);
        _definitions.DidNotReceive().Add(Arg.Any<PayItemDefinition>());
    }

    [Fact]
    public async Task Update_OfAnUnknownItem_IsNotFound()
    {
        var handler = new UpdatePayItemHandler(_definitions, _assignments, _flagger);

        (await handler.Handle(new UpdatePayItemCommand(Guid.NewGuid(), Input()), CancellationToken.None)).Error.Code.ShouldBe(PayrollDomainErrors.PayItemNotFound);
    }

    [Fact]
    public async Task Update_ChangesTheFormula_AndFlagsOnlyEmployeesAssignedInOpenPeriods()
    {
        var definition = Stored();
        OpenPeriod(2026, 6);
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([PayrollPeriod.Create(TenantId, 2026, 6).Value]));
        var current = EmployeePayItemAssignment.Create(TenantId, EmployeeA, definition, 10m, new DateOnly(2026, 1, 1), null, new HashSet<DateOnly>()).Value;
        _assignments.ListByDefinitionAsync(definition.Id, new DateOnly(2026, 6, 1), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([current]));
        var handler = new UpdatePayItemHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdatePayItemCommand(definition.Id, Input("deger * 2")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.Formula.ShouldBe("deger * 2");
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA && f.Reasons.Contains(PayslipDirtyFlagReasons.PayItemChanged)));
        _flags.DidNotReceive().Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeB));
    }

    [Fact]
    public async Task Update_WhenNoPeriodIsOpen_FlagsNobody()
    {
        var definition = Stored();
        var handler = new UpdatePayItemHandler(_definitions, _assignments, _flagger);

        (await handler.Handle(new UpdatePayItemCommand(definition.Id, Input("deger * 3")), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        _flags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());
        await _assignments.DidNotReceive().ListByDefinitionAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAndActivate_ToggleTheItem_AndFlagAffectedEmployeesOnlyOnAChange()
    {
        var definition = Stored();
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([PayrollPeriod.Create(TenantId, 2026, 6).Value]));
        OpenPeriod(2026, 6);
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeA, definition, 10m, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), new HashSet<DateOnly>()).Value;
        _assignments.ListByDefinitionAsync(definition.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([assignment]));

        (await new DeactivatePayItemHandler(_definitions, _flagger).Handle(new DeactivatePayItemCommand(definition.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        definition.IsActive.ShouldBeFalse();
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));

        _flags.ClearReceivedCalls();
        (await new DeactivatePayItemHandler(_definitions, _flagger).Handle(new DeactivatePayItemCommand(definition.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        _flags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());

        (await new ActivatePayItemHandler(_definitions, _flagger).Handle(new ActivatePayItemCommand(definition.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        definition.IsActive.ShouldBeTrue();
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));
    }

    [Fact]
    public async Task ValidateFormula_ReturnsTheUsedVariablesAndASampleResult()
    {
        var handler = new ValidatePayItemFormulaHandler();

        var result = (await handler.Handle(new ValidatePayItemFormulaQuery("brut * 0.1 + deger", null, null), CancellationToken.None)).Value;

        result.IsValid.ShouldBeTrue();
        result.UsedVariables.ShouldBe(["brut", "deger"]);
        result.SampleResult.ShouldBe(30000m * 0.1m + 1000m);
        result.ErrorCode.ShouldBeNull();
    }

    [Fact]
    public async Task ValidateFormula_UsesGivenSampleValues()
    {
        var samples = new Dictionary<string, decimal> { ["BRUT"] = 10000m, ["bilinmeyen"] = 1m };

        var result = (await new ValidatePayItemFormulaHandler().Handle(new ValidatePayItemFormulaQuery("brut / 10", null, samples), CancellationToken.None)).Value;

        result.SampleResult.ShouldBe(1000m);
    }

    [Theory]
    [InlineData("brut *", PayrollDomainErrors.FormulaSyntax)]
    [InlineData("yok + 1", PayrollDomainErrors.FormulaUnknownVariable)]
    [InlineData("", PayrollDomainErrors.FormulaEmpty)]
    [InlineData("1,5", PayrollDomainErrors.FormulaDecimalComma)]
    [InlineData("brut / 0", PayrollDomainErrors.FormulaDivisionByZero)]
    public async Task ValidateFormula_ReportsAnInvalidFormulaAsData_NotAsAFailure(string formula, string code)
    {
        var outcome = await new ValidatePayItemFormulaHandler().Handle(new ValidatePayItemFormulaQuery(formula, null, null), CancellationToken.None);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.IsValid.ShouldBeFalse();
        outcome.Value.ErrorCode.ShouldBe(code);
        outcome.Value.SampleResult.ShouldBeNull();
        outcome.Value.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task ValidateFormula_ReportsThePositionOfTheError()
    {
        var result = (await new ValidatePayItemFormulaHandler().Handle(new ValidatePayItemFormulaQuery("1 + yok", null, null), CancellationToken.None)).Value;

        result.Position.ShouldBe(5);
    }

    [Fact]
    public async Task FormulaCatalog_ListsEveryVariableWithASample_AndTheFunctions()
    {
        var catalog = (await new GetFormulaCatalogHandler().Handle(new GetFormulaCatalogQuery(), CancellationToken.None)).Value;

        catalog.Variables.Select(v => v.Name).ShouldBe(Sense.Modules.Payroll.Domain.PayItems.Formulas.PayItemVariableNames.All);
        catalog.Variables.ShouldAllBe(v => v.SampleValue != 0m);
        catalog.Functions.Select(f => f.Name).ShouldContain("eger");
        catalog.Functions.Single(f => f.Name == "yuvarla").Aliases.ShouldContain("round");
        catalog.Functions.Single(f => f.Name == "min").MaxArgs.ShouldBeNull();
    }

    private CreatePayItemAssignmentsHandler AssignHandler() => new(_tenant, _periods, _definitions, _assignments, _directory, _flagger);

    [Fact]
    public async Task Assign_CreatesOneAssignmentPerEmployee_AndFlagsThem()
    {
        var definition = Stored();
        OpenPeriod(2026, 6);

        var result = await AssignHandler().Handle(
            new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeA, EmployeeB, EmployeeA], 500m, 2026, 6, 2026, 8), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedIds.Count.ShouldBe(2);
        _assignments.Received(2).Add(Arg.Is<EmployeePayItemAssignment>(a => a.Value == 500m && a.ValidFromMonth == new DateOnly(2026, 6, 1) && a.ValidToMonth == new DateOnly(2026, 8, 1)));
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA && f.Reasons.Contains("pay_item_changed")));
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeB));
    }

    [Fact]
    public async Task Assign_DoesNotFlagPeriodsAfterTheEndMonth()
    {
        var definition = Stored();
        OpenPeriod(2026, 9);

        await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeA], 1m, 2026, 6, 2026, 8), CancellationToken.None);

        _flags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());
    }

    [Fact]
    public async Task Assign_RefusesMoreThan500Employees()
    {
        var definition = Stored();
        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, ids, 1m, 2026, 6, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentTooMany);
        _assignments.DidNotReceive().Add(Arg.Any<EmployeePayItemAssignment>());
    }

    [Fact]
    public async Task Assign_Accepts500Employees()
    {
        var definition = Stored();
        var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, ids, 1m, 2026, 6, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedIds.Count.ShouldBe(500);
    }

    [Fact]
    public async Task Assign_RefusesAClosedPeriod_AndCreatesNothing()
    {
        var definition = Stored();
        var approved = PayrollPeriod.Create(TenantId, 2026, 5).Value;
        approved.Approve(null);
        _periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([approved]));

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeA, EmployeeB], 1m, 2026, 5, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        _assignments.DidNotReceive().Add(Arg.Any<EmployeePayItemAssignment>());
        _flags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());
    }

    [Fact]
    public async Task Assign_RefusesAFormulaThatNeedsDeger_WhenNeitherValueNorDefaultExist()
    {
        var definition = Stored("deger");

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeA], null, 2026, 6, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentValueRequired);
    }

    [Fact]
    public async Task Assign_RefusesA51stItemInAMonth()
    {
        var definition = Stored();
        var existing = Enumerable.Range(0, 50)
            .Select(i => EmployeePayItemAssignment.Create(
                TenantId, EmployeeA, PayItemDefinition.Create(TenantId, $"ITEM{i}", PayItemDomainTests.Data()).Value, 1m, new DateOnly(2026, 6, 1), null, new HashSet<DateOnly>()).Value)
            .ToList();
        _assignments.ListByEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>(existing));

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeA], 1m, 2026, 7, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentTooMany);
        _assignments.DidNotReceive().Add(Arg.Any<EmployeePayItemAssignment>());
    }

    [Fact]
    public async Task Assign_RefusesAnUnknownOrInactiveItem_AndAnUnknownEmployee()
    {
        (await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(Guid.NewGuid(), [EmployeeA], 1m, 2026, 6, null, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemNotFound);

        var inactive = Stored();
        inactive.Deactivate();
        (await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(inactive.Id, [EmployeeA], 1m, 2026, 6, null, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemInactive);

        var active = Stored();
        _directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([]));
        var missing = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(active.Id, [EmployeeA], 1m, 2026, 6, null, null), CancellationToken.None);
        missing.Error.Code.ShouldBe(PayrollDomainErrors.PayItemEmployeeNotFound);
        missing.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateAssignment_EndsAUsedAssignment_ButCannotRevalueIt()
    {
        var definition = Stored();
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeA, definition, 100m, new DateOnly(2026, 1, 1), null, new HashSet<DateOnly>()).Value;
        _assignments.FindByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayItemAssignment?>(assignment));
        var approved = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        approved.Approve(null);
        _periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([approved]));
        var handler = new UpdatePayItemAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        (await handler.Handle(new UpdatePayItemAssignmentCommand(assignment.Id, 999m, null, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        (await handler.Handle(new UpdatePayItemAssignmentCommand(assignment.Id, 100m, 2026, 3), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        assignment.ValidToMonth.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public async Task UpdateAssignment_FlagsTheEmployee_OverTheWiderOfOldAndNewRange()
    {
        var definition = Stored();
        OpenPeriod(2026, 9);
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeA, definition, 100m, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1), new HashSet<DateOnly>()).Value;
        _assignments.FindByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayItemAssignment?>(assignment));
        var handler = new UpdatePayItemAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        (await handler.Handle(new UpdatePayItemAssignmentCommand(assignment.Id, 100m, 2026, 10), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));
    }

    private static EmployeePayItemAssignment Assignment(PayItemDefinition definition, Guid employee, DateOnly from, DateOnly? to) =>
        EmployeePayItemAssignment.Create(TenantId, employee, definition, 1m, from, to, new HashSet<DateOnly>()).Value;

    [Fact]
    public async Task Assign_RefusesAnOverlappingAssignmentOfTheSameItem_AsAConflict_AllOrNothing()
    {
        var definition = Stored();
        var existing = Assignment(definition, EmployeeA, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1));
        _assignments.ListByEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([existing]));

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeB, EmployeeA], 1m, 2026, 8, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentOverlap);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        _assignments.DidNotReceive().Add(Arg.Any<EmployeePayItemAssignment>());
    }

    [Fact]
    public async Task Assign_AcceptsAdjacentRanges_DifferentItems_AndStartsAfterAnEndedAssignment()
    {
        var definition = Stored();
        var other = PayItemDefinition.Create(TenantId, "DIGER", PayItemDomainTests.Data()).Value;
        var ended = Assignment(definition, EmployeeA, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1));
        var otherItem = Assignment(other, EmployeeB, new DateOnly(2026, 1, 1), null);
        _assignments.ListByEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([ended, otherItem]));

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeA, EmployeeB], 1m, 2026, 9, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAssignment_RefusesExtendingIntoAnotherAssignmentOfTheSameItem()
    {
        var definition = Stored();
        var first = Assignment(definition, EmployeeA, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));
        var second = Assignment(definition, EmployeeA, new DateOnly(2026, 9, 1), null);
        _assignments.FindByIdAsync(first.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayItemAssignment?>(first));
        _assignments.ListByEmployeeAsync(EmployeeA, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([first, second]));
        var handler = new UpdatePayItemAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        (await handler.Handle(new UpdatePayItemAssignmentCommand(first.Id, 1m, 2026, 9), CancellationToken.None)).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentOverlap);
        (await handler.Handle(new UpdatePayItemAssignmentCommand(first.Id, 1m, 2026, 8), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        first.ValidToMonth.ShouldBe(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task Assign_RefusesARangeThatReachesALaterClosedPeriod_EvenWhenTheStartHasNoPeriodYet()
    {
        var definition = Stored();
        var approved = PayrollPeriod.Create(TenantId, 2026, 7).Value;
        approved.Approve(null);
        _periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([approved]));

        var result = await AssignHandler().Handle(new CreatePayItemAssignmentsCommand(definition.Id, [EmployeeA], 1m, 2026, 6, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        _assignments.DidNotReceive().Add(Arg.Any<EmployeePayItemAssignment>());
    }

    [Fact]
    public async Task UpdateAssignment_RefusesExtendingTheEndPastTheMonthlyLimit()
    {
        var definition = Stored();
        var target = Assignment(definition, EmployeeA, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1));
        var others = Enumerable.Range(0, 50)
            .Select(i => Assignment(PayItemDefinition.Create(TenantId, $"ITEM{i}", PayItemDomainTests.Data()).Value, EmployeeA, new DateOnly(2026, 7, 1), null))
            .ToList();
        _assignments.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayItemAssignment?>(target));
        _assignments.ListByEmployeeAsync(EmployeeA, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([target, .. others]));
        var handler = new UpdatePayItemAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        (await handler.Handle(new UpdatePayItemAssignmentCommand(target.Id, 1m, 2026, 7), CancellationToken.None)).Error.Code
            .ShouldBe(PayrollDomainErrors.PayItemAssignmentTooMany);
        (await handler.Handle(new UpdatePayItemAssignmentCommand(target.Id, 1m, 2026, 6), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAssignment_OfAnUnknownId_IsNotFound()
    {
        var handler = new UpdatePayItemAssignmentHandler(_periods, _definitions, _assignments, _flagger);

        (await handler.Handle(new UpdatePayItemAssignmentCommand(Guid.NewGuid(), 1m, null, null), CancellationToken.None))
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentNotFound);
    }

    [Fact]
    public async Task DeleteAssignment_RemovesAnUnusedOne_AndFlagsTheEmployee()
    {
        var definition = Stored();
        OpenPeriod(2026, 6);
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeA, definition, 100m, new DateOnly(2026, 6, 1), null, new HashSet<DateOnly>()).Value;
        _assignments.FindByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayItemAssignment?>(assignment));

        var result = await new DeletePayItemAssignmentHandler(_periods, _assignments, _flagger).Handle(new DeletePayItemAssignmentCommand(assignment.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _assignments.Received(1).Remove(assignment);
        _flags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(f => f.EmployeeId == EmployeeA));
    }

    [Fact]
    public async Task DeleteAssignment_IsRefused_ForAUsedOne()
    {
        var definition = Stored();
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeA, definition, 100m, new DateOnly(2026, 1, 1), null, new HashSet<DateOnly>()).Value;
        _assignments.FindByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayItemAssignment?>(assignment));
        var locked = PayrollPeriod.Create(TenantId, 2026, 2).Value;
        locked.Approve(null);
        _periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([locked]));

        var result = await new DeletePayItemAssignmentHandler(_periods, _assignments, _flagger).Handle(new DeletePayItemAssignmentCommand(assignment.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        _assignments.DidNotReceive().Remove(Arg.Any<EmployeePayItemAssignment>());
    }

    [Fact]
    public async Task ListAssignments_ByEmployee_ReturnsNamesAndPlanShapedMonths()
    {
        var definition = Stored();
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeA, definition, 100m, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1), new HashSet<DateOnly>()).Value;
        _assignments.ListByEmployeeAsync(EmployeeA, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([assignment]));
        _definitions.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayItemDefinition>>([definition]));
        var handler = new ListPayItemAssignmentsHandler(_periods, _assignments, _definitions, _directory, _tenant);

        var dto = (await handler.Handle(new ListPayItemAssignmentsQuery(EmployeeA, null), CancellationToken.None)).Value.Single();

        dto.EmployeeName.ShouldBe("Ayşe Yılmaz");
        (dto.Code, dto.Value, dto.FromYear, dto.FromMonth, dto.ToYear, dto.ToMonth).ShouldBe(("YEMEK", (decimal?)100m, 2026, 6, (int?)2026, (int?)8));
    }

    [Fact]
    public async Task ListAssignments_ByPeriod_UsesThePeriodsMonth_AndRejectsAnUnknownPeriod()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 7).Value;
        _periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        _assignments.ListCoveringMonthAsync(new DateOnly(2026, 7, 1), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([]));
        _definitions.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayItemDefinition>>([]));
        var handler = new ListPayItemAssignmentsHandler(_periods, _assignments, _definitions, _directory, _tenant);

        (await handler.Handle(new ListPayItemAssignmentsQuery(null, period.Id), CancellationToken.None)).Value.ShouldBeEmpty();
        (await handler.Handle(new ListPayItemAssignmentsQuery(null, Guid.NewGuid()), CancellationToken.None)).Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotFound);
    }

    [Fact]
    public async Task ListAndGetPayItems_MapTheDefinition()
    {
        var definition = Stored("brut * 0.05");
        _definitions.ListAsync(false, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayItemDefinition>>([definition]));

        var listed = (await new ListPayItemsHandler(_definitions).Handle(new ListPayItemsQuery(), CancellationToken.None)).Value.Single();
        var got = (await new GetPayItemHandler(_definitions).Handle(new GetPayItemQuery(definition.Id), CancellationToken.None)).Value;

        listed.ShouldBe(got);
        got.Formula.ShouldBe("brut * 0.05");
        (await new GetPayItemHandler(_definitions).Handle(new GetPayItemQuery(Guid.NewGuid()), CancellationToken.None)).Error.Code.ShouldBe(PayrollDomainErrors.PayItemNotFound);
    }

    // ---- permissions -------------------------------------------------------------------------------------------

    public static TheoryData<Type> ManageRequests => new()
    {
        typeof(CreatePayItemCommand), typeof(UpdatePayItemCommand), typeof(ActivatePayItemCommand), typeof(DeactivatePayItemCommand),
        typeof(ValidatePayItemFormulaQuery), typeof(CreatePayItemAssignmentsCommand), typeof(UpdatePayItemAssignmentCommand), typeof(DeletePayItemAssignmentCommand),
    };

    public static TheoryData<Type> ViewRequests => new()
    {
        typeof(ListPayItemsQuery), typeof(GetPayItemQuery), typeof(GetFormulaCatalogQuery), typeof(ListPayItemAssignmentsQuery),
    };

    [Theory]
    [MemberData(nameof(ManageRequests))]
    public void WriteRequests_RequirePayItemManage(Type request) =>
        request.GetCustomAttributes<RequiresPermissionAttribute>().ShouldHaveSingleItem().Permission.ShouldBe(PayrollPermissions.PayItemManage.Key);

    [Theory]
    [MemberData(nameof(ViewRequests))]
    public void ReadRequests_RequireTraceView(Type request) =>
        request.GetCustomAttributes<RequiresPermissionAttribute>().ShouldHaveSingleItem().Permission.ShouldBe(PayrollPermissions.TraceView.Key);

    [Fact]
    public void PayItemManage_IsRegisteredWithThePlanKey()
    {
        PayrollPermissions.PayItemManage.Key.ShouldBe("payroll.payitem.manage");
        PayrollPermissions.All.ShouldContain(PayrollPermissions.PayItemManage);
    }

    [Fact]
    public async Task CreateWithoutThePermission_IsForbidden_AndTheHandlerIsNotReached()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.NewGuid());
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => call.ArgAt<string>(1) == "payroll.trace.view");
        var behaviour = new AuthorizationBehaviour<CreatePayItemCommand, Result<Guid>>(user, permissions, Substitute.For<IDataScopeService>());
        var reached = false;

        var result = await behaviour.Handle(new CreatePayItemCommand("YEMEK", Input()), () =>
        {
            reached = true;
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }, CancellationToken.None);

        reached.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Args!["permission"].ShouldBe("payroll.payitem.manage");
    }
}

internal static class PayItemInputTestExtensions
{
    public static PayItemDefinitionData ToDataForTest(this PayItemDefinitionInput input) => new(
        input.Name, input.Direction, input.AmountBasis, input.IsSgkSubject, input.IsIncomeTaxSubject, input.IsStampTaxSubject,
        input.Formula, input.DefaultValue, input.ValidFrom ?? new DateOnly(2026, 1, 1), input.ValidTo);
}

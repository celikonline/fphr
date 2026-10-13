using NSubstitute;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>PAY-DEDUCTION-1: the definition handlers with Direction = Deduction (accept + normalize, direction lock once assigned).</summary>
public sealed class PayDeductionFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IPayrollPeriodRepository _periods = Substitute.For<IPayrollPeriodRepository>();
    private readonly IPayItemDefinitionRepository _definitions = Substitute.For<IPayItemDefinitionRepository>();
    private readonly IEmployeePayItemAssignmentRepository _assignments = Substitute.For<IEmployeePayItemAssignmentRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly PayItemChangeFlagger _flagger;

    public PayDeductionFeaturesTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _periods.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        _assignments.ListByDefinitionAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([]));
        _flagger = new PayItemChangeFlagger(_tenant, _periods, _assignments, new PayslipDirtyFlagService(_periods, Substitute.For<IPayslipDirtyFlagRepository>(), TimeProvider.System));
    }

    private static PayItemDefinitionInput Input(
        PayItemDirection direction, string formula = "brut * 0.01", PayItemAmountBasis basis = PayItemAmountBasis.Gross, bool taxFlags = false) =>
        new("Kooperatif aidatı", direction, basis, taxFlags, taxFlags, taxFlags, formula, null, new DateOnly(2026, 1, 1), null);

    private PayItemDefinition Stored(PayItemDirection direction, bool assigned)
    {
        var definition = PayItemDefinition.Create(TenantId, "KOOP", Input(direction, taxFlags: direction == PayItemDirection.Earning).ToDataForTest()).Value;
        _definitions.FindByIdAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayItemDefinition?>(definition));
        if (assigned)
        {
            var assignment = EmployeePayItemAssignment.Create(TenantId, Guid.NewGuid(), definition, null, new DateOnly(2026, 1, 1), null, new HashSet<DateOnly>()).Value;
            _assignments.ListByDefinitionAsync(definition.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<EmployeePayItemAssignment>>([assignment]));
        }

        return definition;
    }

    [Fact]
    public async Task Create_AcceptsADeduction_AndStoresFalseFlagsWhateverWasSent()
    {
        var handler = new CreatePayItemHandler(_tenant, _definitions, TimeProvider.System);

        var result = await handler.Handle(new CreatePayItemCommand("KOOP", Input(PayItemDirection.Deduction, taxFlags: true)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _definitions.Received(1).Add(Arg.Is<PayItemDefinition>(d =>
            d.Direction == PayItemDirection.Deduction && !d.IsSgkSubject && !d.IsIncomeTaxSubject && !d.IsStampTaxSubject && d.AmountBasis == PayItemAmountBasis.Gross));
    }

    [Fact]
    public async Task Create_ADeductionWithNetBasis_IsRejected()
    {
        var handler = new CreatePayItemHandler(_tenant, _definitions, TimeProvider.System);

        var result = await handler.Handle(new CreatePayItemCommand("KOOP", Input(PayItemDirection.Deduction, basis: PayItemAmountBasis.Net)), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemDeductionBasisInvalid);
        _definitions.DidNotReceive().Add(Arg.Any<PayItemDefinition>());
    }

    [Fact]
    public async Task Dto_ShowsTheNormalizedFalseFlagsAndTheDirection()
    {
        var definition = Stored(PayItemDirection.Deduction, assigned: false);
        _definitions.FindByIdAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayItemDefinition?>(definition));

        var result = await new GetPayItemHandler(_definitions).Handle(new GetPayItemQuery(definition.Id), CancellationToken.None);

        result.Value.Direction.ShouldBe(PayItemDirection.Deduction);
        result.Value.IsSgkSubject.ShouldBeFalse();
        result.Value.IsIncomeTaxSubject.ShouldBeFalse();
        result.Value.IsStampTaxSubject.ShouldBeFalse();
    }

    [Theory]
    [InlineData(PayItemDirection.Earning, PayItemDirection.Deduction)]
    [InlineData(PayItemDirection.Deduction, PayItemDirection.Earning)]
    public async Task Update_ChangingTheDirectionOfAnAssignedItem_IsAConflict(PayItemDirection from, PayItemDirection to)
    {
        var definition = Stored(from, assigned: true);
        var handler = new UpdatePayItemHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdatePayItemCommand(definition.Id, Input(to, taxFlags: to == PayItemDirection.Earning)), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemDirectionLocked);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        definition.Direction.ShouldBe(from);
    }

    [Fact]
    public async Task Update_ChangingTheDirectionOfAnUnassignedItem_IsAllowed()
    {
        var definition = Stored(PayItemDirection.Earning, assigned: false);
        var handler = new UpdatePayItemHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdatePayItemCommand(definition.Id, Input(PayItemDirection.Deduction, taxFlags: true)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.Direction.ShouldBe(PayItemDirection.Deduction);
        definition.IsSgkSubject.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_KeepingTheDirectionOfAnAssignedItem_StillWorks_AndNormalizesTheFlags()
    {
        var definition = Stored(PayItemDirection.Deduction, assigned: true);
        var handler = new UpdatePayItemHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdatePayItemCommand(definition.Id, Input(PayItemDirection.Deduction, formula: "brut * 0.02", taxFlags: true)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        definition.Formula.ShouldBe("brut * 0.02");
        definition.IsStampTaxSubject.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_ADeductionToNetBasis_IsRejectedWithTheBasisError()
    {
        var definition = Stored(PayItemDirection.Deduction, assigned: false);
        var handler = new UpdatePayItemHandler(_definitions, _assignments, _flagger);

        var result = await handler.Handle(new UpdatePayItemCommand(definition.Id, Input(PayItemDirection.Deduction, basis: PayItemAmountBasis.Net)), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemDeductionBasisInvalid);
    }
}

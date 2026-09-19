using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Garnishments;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Unit tests for the <see cref="GarnishmentOrder"/> aggregate (senseik-019 2c).</summary>
public sealed class GarnishmentOrderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static readonly DateOnly StartDate = new(2026, 1, 1);

    [Fact]
    public void Create_ValidData_Succeeds()
    {
        var result = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Icra, "2026/123 E.", 1_000m, 10_000m, StartDate);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsClosed.ShouldBeFalse();
        result.Value.RemainingBalance.ShouldBe(10_000m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCaseReference_Fails(string caseReference)
    {
        var result = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Nafaka, caseReference, 1_000m, 10_000m, StartDate);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.GarnishmentCaseReferenceRequired);
    }

    [Fact]
    public void Create_NonPositiveInstallment_Fails()
    {
        var result = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Icra, "dosya-1", 0m, 10_000m, StartDate);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.GarnishmentInstallmentInvalid);
    }

    [Fact]
    public void Create_NonPositiveBalance_Fails()
    {
        var result = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Icra, "dosya-1", 1_000m, 0m, StartDate);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.GarnishmentBalanceInvalid);
    }

    [Fact]
    public void ApplyMonthlyDeduction_ReducesBalance_KeepsOpenWhilePositive()
    {
        var order = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Icra, "dosya-1", 1_000m, 10_000m, StartDate).Value;

        order.ApplyMonthlyDeduction(1_000m, StartDate.AddMonths(1));

        order.RemainingBalance.ShouldBe(9_000m);
        order.IsClosed.ShouldBeFalse();
    }

    [Fact]
    public void ApplyMonthlyDeduction_ExhaustsBalance_ClosesAutomatically()
    {
        var order = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Nafaka, "karar-1", 1_000m, 1_000m, StartDate).Value;
        var closedOn = StartDate.AddMonths(1);

        order.ApplyMonthlyDeduction(1_000m, closedOn);

        order.RemainingBalance.ShouldBe(0m);
        order.IsClosed.ShouldBeTrue();
        order.ClosedDate.ShouldBe(closedOn);
    }

    [Fact]
    public void Close_AlreadyClosed_Fails()
    {
        var order = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Icra, "dosya-1", 1_000m, 1_000m, StartDate).Value;
        order.ApplyMonthlyDeduction(1_000m, StartDate.AddMonths(1));

        var result = order.Close(StartDate.AddMonths(2));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.GarnishmentAlreadyClosed);
    }

    [Fact]
    public void Close_BeforeStartDate_Fails()
    {
        var order = GarnishmentOrder.Create(TenantId, EmployeeId, GarnishmentType.Icra, "dosya-1", 1_000m, 10_000m, StartDate).Value;

        var result = order.Close(StartDate.AddDays(-1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.GarnishmentDateRangeInvalid);
    }
}

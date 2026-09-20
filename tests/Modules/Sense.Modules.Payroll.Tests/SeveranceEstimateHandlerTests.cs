using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>QA teslim testi bulgu 6: the severance estimate answers 400/422 (not 500) for a nonsensical exit date or a
/// date without statutory parameters.</summary>
public sealed class SeveranceEstimateHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2024, 3, 1);

    private static GetSeveranceEstimateHandler Handler(ILegislationParameterReader? reader = null)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var salary = Substitute.For<IEmployeeSalaryLookup>();
        salary.GetEmploymentDatesAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeEmploymentDates?>(new EmployeeEmploymentDates(HireDate, null)));
        salary.GetCurrentSalaryAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeCurrentSalary?>(new EmployeeCurrentSalary(60_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, HireDate)));
        var seniority = Substitute.For<IEmployeeSeniorityLookup>();
        seniority.GetCountedSeniorityDaysAsync(TenantId, EmployeeId, Arg.Any<EmployeeSeniorityPurpose>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(400));
        var leave = Substitute.For<ILeaveBalanceLookup>();

        // A reader with no rows: every parameter is "missing".
        reader ??= Substitute.For<ILegislationParameterReader>();
        return new GetSeveranceEstimateHandler(tenant, salary, seniority, leave, reader, TimeProvider.System);
    }

    [Fact]
    public async Task Handle_WhenTheTerminationDateIsBeforeTheHireDate_IsValidationError()
    {
        var result = await Handler().Handle(new GetSeveranceEstimateQuery(EmployeeId, HireDate.AddDays(-1), SeveranceCauseCategory.EmployerNoticeBased), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.SeveranceTerminationDateInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_WhenTheTerminationDateIsFarInTheFuture_IsValidationError()
    {
        var result = await Handler().Handle(new GetSeveranceEstimateQuery(EmployeeId, new DateOnly(2100, 1, 1), SeveranceCauseCategory.EmployerNoticeBased), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.SeveranceTerminationDateInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_WhenNoLegislationParameterCoversTheDate_IsBusinessRuleError_NotAnException()
    {
        var result = await Handler().Handle(new GetSeveranceEstimateQuery(EmployeeId, new DateOnly(2025, 6, 1), SeveranceCauseCategory.EmployerNoticeBased), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.LegislationParameterMissing);
        result.Error.Type.ShouldBe(ErrorType.Rule);
    }
}

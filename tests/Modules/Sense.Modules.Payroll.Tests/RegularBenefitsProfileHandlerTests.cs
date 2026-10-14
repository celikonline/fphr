using NSubstitute;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

public sealed class RegularBenefitsProfileHandlerTests
{
    [Fact]
    public async Task AProfileUpdateFromAnOlderClient_PreservesDeclaredBenefits_WhenTheFieldIsOmitted()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var data = new EmployeePayrollProfileData(Guid.NewGuid(), null, new DateOnly(2026, 1, 1), null,
            WageBasis.Monthly, SgkStatus.Normal, null, MealBenefitVariant.Cash, 150m,
            TransportVariant.None, PayrollPaymentMethod.Bank, null, false, null, null);
        var profile = EmployeePayrollProfile.Create(tenantId, employeeId, data with { RegularBenefitsMonthlyGross = 3_000m }).Value;
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.FindByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(profile));
        profiles.ListByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EmployeePayrollProfile>>([profile]));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        var flags = new PayslipDirtyFlagService(periods, Substitute.For<IPayslipDirtyFlagRepository>(), TimeProvider.System);
        var handler = new UpdateEmployeePayrollProfileHandler(tenant, profiles, flags);

        var result = await handler.Handle(new UpdateEmployeePayrollProfileCommand(profile.Id,
            data with { Notes = "Updated note" }, PreserveRegularBenefits: true), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        profile.RegularBenefitsMonthlyGross.ShouldBe(3_000m);
        profile.MealDailyAmount.ShouldBe(150m);
        profile.Notes.ShouldBe("Updated note");
    }

    [Fact]
    public async Task BenefitsCannotBeChangedThroughAnotherEmployeesProfileRoute()
    {
        var tenantId = Guid.NewGuid();
        var profile = EmployeePayrollProfile.Create(tenantId, Guid.NewGuid(), new EmployeePayrollProfileData(Guid.NewGuid(), null,
            new DateOnly(2026, 1, 1), null, WageBasis.Monthly, SgkStatus.Normal, null,
            MealBenefitVariant.None, 0m, TransportVariant.None, PayrollPaymentMethod.Bank, null, false, null, null)).Value;
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        profiles.FindByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(profile));
        var handler = new UpdateEmployeeRegularBenefitsHandler(tenant, profiles,
            new PayslipDirtyFlagService(Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IPayslipDirtyFlagRepository>(), TimeProvider.System));

        var result = await handler.Handle(new UpdateEmployeeRegularBenefitsCommand(Guid.NewGuid(), profile.Id, 3_000m), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(PayrollDomainErrors.ProfileNotFound);
        profile.RegularBenefitsMonthlyGross.ShouldBe(0m);
    }
}

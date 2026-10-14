using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class RegularBenefitsPersistenceTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<PayrollDbContext>(configuration, PayrollDbContext.SchemaName);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PayrollDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task RecurringBenefits_RoundTripThroughTheMigration_WithoutChangingOtherProfileAmounts()
    {
        var tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var tenant = TenantContextSetter!.BeginScope(tenantId);
        var ct = TestContext.Current.CancellationToken;
        var profile = EmployeePayrollProfile.Create(tenantId, id, new EmployeePayrollProfileData(Guid.NewGuid(), null,
            new DateOnly(2026, 1, 1), null, WageBasis.Monthly, SgkStatus.Normal, null,
            MealBenefitVariant.Cash, 150m, TransportVariant.Cash, PayrollPaymentMethod.Bank, null, false, null, null,
            TransportDailyAmount: 25m)).Value;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<PayrollDbContext>();
            db.EmployeePayrollProfiles.Add(profile);
            await db.SaveChangesAsync(ct);
        }

        using (var update = ServiceProvider!.CreateScope())
        {
            var db = update.ServiceProvider.GetRequiredService<PayrollDbContext>();
            var stored = await db.EmployeePayrollProfiles.SingleAsync(p => p.EmployeeId == id, ct);
            stored.RegularBenefitsMonthlyGross.ShouldBe(0m);
            stored.SetRegularBenefitsMonthlyGross(3_000.125m).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);
        }

        using var read = ServiceProvider!.CreateScope();
        var fresh = await read.ServiceProvider.GetRequiredService<PayrollDbContext>().EmployeePayrollProfiles.AsNoTracking().SingleAsync(p => p.EmployeeId == id, ct);
        fresh.RegularBenefitsMonthlyGross.ShouldBe(3_000.13m);
        fresh.MealDailyAmount.ShouldBe(150m);
        fresh.TransportDailyAmount.ShouldBe(25m);
    }
}

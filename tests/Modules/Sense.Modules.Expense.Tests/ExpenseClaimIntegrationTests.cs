using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Modules.Expense.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

/// <summary>
/// Integration tests (real PostgreSQL via Testcontainers, see TestFixture) for ExpenseClaim persistence - in
/// particular that ExpenseItem's client-generated Guid id (Guid.CreateVersion7(), ValueGeneratedNever in
/// ExpenseDbContext.cs) round-trips correctly when items are appended across separate SaveChanges calls, the same
/// class of EF Added-vs-Modified pitfall documented on Workflow's ApprovalTaskConfiguration.
/// </summary>
[Collection("PostgreSQL")]
public sealed class ExpenseClaimIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<ExpenseDbContext>(configuration, ExpenseDbContext.SchemaName);
        services.AddScoped<IExpenseClaimRepository, ExpenseClaimRepository>();
        services.AddScoped<IExpenseCategoryRepository, ExpenseCategoryRepository>();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<ExpenseDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task AddSecondItem_InALaterSaveChanges_PersistsBothItemsWithDistinctIds()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var employeeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var receiptDate = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid claimId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claims = writeScope.ServiceProvider.GetRequiredService<IExpenseClaimRepository>();

            var claim = ExpenseClaim.Create(tenantId, employeeId, "Müşteri ziyareti");
            claim.AddItem(categoryId, 100m, receiptDate, null, "TRY", null, "taxi");
            claims.Add(claim);
            await db.SaveChangesAsync();
            claimId = claim.Id;
        }

        // Reload in a fresh scope, then append a second item in a *different* SaveChanges - this is exactly the
        // scenario that trips EF's Added-vs-Modified heuristic without ValueGeneratedNever on ExpenseItem.Id.
        using (var appendScope = ServiceProvider!.CreateScope())
        {
            var db = appendScope.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claims = appendScope.ServiceProvider.GetRequiredService<IExpenseClaimRepository>();

            var reloaded = (await claims.GetByIdAsync(claimId)).Value;
            reloaded.Items.Count.ShouldBe(1);

            var added = reloaded.AddItem(categoryId, 50m, receiptDate, 20m, "TRY", null, "lunch");
            added.IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        using (var readScope = ServiceProvider!.CreateScope())
        {
            var claims = readScope.ServiceProvider.GetRequiredService<IExpenseClaimRepository>();
            var reloaded = (await claims.GetByIdAsync(claimId)).Value;

            reloaded.Items.Count.ShouldBe(2);
            reloaded.Items.Select(i => i.Id).Distinct().Count().ShouldBe(2);
            reloaded.TotalAmount.ShouldBe(150m);
            reloaded.Items.Single(i => i.Description == "lunch").TaxAmount.ShouldBe(8.33m);
        }
    }

    [Fact]
    public async Task Submit_ThenApprove_TransitionsStatusAndPersists()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var employeeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        Guid claimId;
        Guid approvalId = Guid.NewGuid();

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claims = writeScope.ServiceProvider.GetRequiredService<IExpenseClaimRepository>();

            var claim = ExpenseClaim.Create(tenantId, employeeId, null);
            claim.AddItem(categoryId, 100m, DateOnly.FromDateTime(DateTime.UtcNow), null, "TRY", null, null);
            claim.Submit().IsSuccess.ShouldBeTrue();
            claim.AttachApproval(approvalId, DateTime.UtcNow);
            claims.Add(claim);
            await db.SaveChangesAsync();
            claimId = claim.Id;
        }

        using (var decideScope = ServiceProvider!.CreateScope())
        {
            var db = decideScope.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claims = decideScope.ServiceProvider.GetRequiredService<IExpenseClaimRepository>();

            var reloaded = (await claims.GetByIdAsync(claimId)).Value;
            reloaded.Status.ShouldBe(ExpenseClaimStatus.Submitted);

            reloaded.Approve(approvalId, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        using (var readScope = ServiceProvider!.CreateScope())
        {
            var claims = readScope.ServiceProvider.GetRequiredService<IExpenseClaimRepository>();
            var reloaded = (await claims.GetByIdAsync(claimId)).Value;
            reloaded.Status.ShouldBe(ExpenseClaimStatus.Approved);
        }
    }
}

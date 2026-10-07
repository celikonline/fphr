using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
[Trait("Category", "Integration")]
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

    [Fact]
    public async Task Receipts_AppendedToTrackedItem_ReplacedAndRemoved_PersistWithoutChangingMoney()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        Guid claimId;
        Guid itemId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claim = ExpenseClaim.Create(tenantId, Guid.NewGuid(), null);
            var item = claim.AddItem(Guid.NewGuid(), 120m, DateOnly.FromDateTime(DateTime.UtcNow), 20m, "TRY", first.ToString(), null).Value;
            db.ExpenseClaims.Add(claim);
            await db.SaveChangesAsync();
            claimId = claim.Id;
            itemId = item.Id;
        }
        using (var append = ServiceProvider!.CreateScope())
        {
            var db = append.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claim = (await append.ServiceProvider.GetRequiredService<IExpenseClaimRepository>().GetByIdAsync(claimId)).Value;
            claim.UpdateItem(itemId, claim.Items.Single().CategoryId, 120m, claim.Items.Single().ReceiptDate, 20m, "TRY", first.ToString(), null, [first, second]);
            await db.SaveChangesAsync();
        }
        using (var replace = ServiceProvider!.CreateScope())
        {
            var db = replace.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claim = (await replace.ServiceProvider.GetRequiredService<IExpenseClaimRepository>().GetByIdAsync(claimId)).Value;
            var item = claim.Items.Single();
            item.ReceiptAttachments.Count.ShouldBe(2);
            item.ReceiptAttachments.Select(a => a.DocumentId).ShouldBe([first, second]);
            claim.TotalAmount.ShouldBe(120m);
            item.TaxAmount.ShouldBe(20m);
            claim.UpdateItem(itemId, item.CategoryId, item.Amount, item.ReceiptDate, item.TaxRate, item.Currency, third.ToString(), null, [third]);
            await db.SaveChangesAsync();
        }
        using (var remove = ServiceProvider!.CreateScope())
        {
            var db = remove.ServiceProvider.GetRequiredService<ExpenseDbContext>();
            var claim = (await remove.ServiceProvider.GetRequiredService<IExpenseClaimRepository>().GetByIdAsync(claimId)).Value;
            claim.Items.Single().ReceiptAttachments.Single().DocumentId.ShouldBe(third);
            claim.Items.Single().ReceiptFileId.ShouldBe(third.ToString());
            claim.RemoveItem(itemId);
            await db.SaveChangesAsync();
            (await db.Set<ExpenseReceiptAttachment>().CountAsync(a => a.ExpenseItemId == itemId)).ShouldBe(0);
        }
    }

    [Fact]
    public async Task ReceiptMigration_BackfillsOnlyValidLegacyGuids_AndPreservesLegacyKeys()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var services = ServiceProvider!.CreateScope();
        var db = services.ServiceProvider.GetRequiredService<ExpenseDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260916143213_AddExpenseCategoryLimit");
        var claimId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var firstItem = Guid.NewGuid();
        var secondItem = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var legacyKey = "old-storage/receipt.pdf";
        var legacyGuid = "  " + documentId.ToString().ToUpperInvariant() + "  ";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO expense.expense_claims (id, tenant_id, employee_id, total_amount, status, created_at) VALUES ({claimId}, {tenantId}, {employeeId}, 200, 'Draft', {now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO expense.expense_items (id, claim_id, category_id, amount, receipt_date, tax_amount, currency, receipt_file_id, created_at) VALUES ({firstItem}, {claimId}, {categoryId}, 100, {DateOnly.FromDateTime(now)}, 0, 'TRY', {legacyGuid}, {now}), ({secondItem}, {claimId}, {categoryId}, 100, {DateOnly.FromDateTime(now)}, 0, 'TRY', {legacyKey}, {now})");
        await migrator.MigrateAsync();
        var claim = (await services.ServiceProvider.GetRequiredService<IExpenseClaimRepository>().GetByIdAsync(claimId)).Value;
        claim.Items.Single(i => i.Id == firstItem).ReceiptAttachments.Single().DocumentId.ShouldBe(documentId);
        claim.Items.Single(i => i.Id == firstItem).ReceiptFileId.ShouldBe(legacyGuid);
        claim.Items.Single(i => i.Id == secondItem).ReceiptAttachments.ShouldBeEmpty();
        claim.Items.Single(i => i.Id == secondItem).ReceiptFileId.ShouldBe(legacyKey);
        claim.TotalAmount.ShouldBe(200m);
    }
}

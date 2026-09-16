using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Expense.Contracts;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Modules.Expense.Infrastructure;
using Sense.Modules.Expense.Infrastructure.Lookups;
using Sense.Modules.Expense.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

/// <summary>
/// Integration tests (real PostgreSQL via Testcontainers) for <see cref="ApprovedExpensePayoutReader"/>, the
/// Payroll bridge (S-13, faz-07 tur 1 madde 2): only Approved-and-not-yet-Paid claims decided in the requested
/// month count toward <see cref="ExpensePayoutSummary.ApprovedExpenseTotal"/>, and only Paid advances count toward
/// <see cref="ExpensePayoutSummary.OpenAdvanceBalance"/> (month-agnostic snapshot).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ApprovedExpensePayoutReaderIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<ExpenseDbContext>(configuration, ExpenseDbContext.SchemaName);
        services.AddExpenseCoreServices();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<ExpenseDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task GetAsync_ReturnsApprovedThisMonthTotal_AndOpenAdvanceBalance_ExcludingOtherStatusesAndMonths()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var receiptDate = new DateOnly(2026, 9, 10);

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<ExpenseDbContext>();

            // Approved in September 2026 -> counted.
            var approvedThisMonth = ExpenseClaim.Create(tenantId, employeeId, "Eylul masrafi");
            approvedThisMonth.AddItem(categoryId, 300m, receiptDate, null, "TRY", null, null);
            approvedThisMonth.Submit();
            var approvalId1 = Guid.NewGuid();
            approvedThisMonth.AttachApproval(approvalId1, DateTime.UtcNow);
            approvedThisMonth.Approve(approvalId1, new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));
            db.ExpenseClaims.Add(approvedThisMonth);

            // Approved in August 2026 -> excluded (different month).
            var approvedLastMonth = ExpenseClaim.Create(tenantId, employeeId, "Agustos masrafi");
            approvedLastMonth.AddItem(categoryId, 999m, receiptDate, null, "TRY", null, null);
            approvedLastMonth.Submit();
            var approvalId2 = Guid.NewGuid();
            approvedLastMonth.AttachApproval(approvalId2, DateTime.UtcNow);
            approvedLastMonth.Approve(approvalId2, new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
            db.ExpenseClaims.Add(approvedLastMonth);

            // Already paid in September 2026 -> excluded (no longer Approved).
            var paidThisMonth = ExpenseClaim.Create(tenantId, employeeId, "Odenmis masraf");
            paidThisMonth.AddItem(categoryId, 500m, receiptDate, null, "TRY", null, null);
            paidThisMonth.Submit();
            var approvalId3 = Guid.NewGuid();
            paidThisMonth.AttachApproval(approvalId3, DateTime.UtcNow);
            paidThisMonth.Approve(approvalId3, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));
            paidThisMonth.MarkPaid(DateTime.UtcNow, employeeId, null);
            db.ExpenseClaims.Add(paidThisMonth);

            // Other employee's claim -> excluded from this employee's total, but present when queried for both.
            var otherEmployeeClaim = ExpenseClaim.Create(tenantId, otherEmployeeId, null);
            otherEmployeeClaim.AddItem(categoryId, 150m, receiptDate, null, "TRY", null, null);
            otherEmployeeClaim.Submit();
            var approvalId4 = Guid.NewGuid();
            otherEmployeeClaim.AttachApproval(approvalId4, DateTime.UtcNow);
            otherEmployeeClaim.Approve(approvalId4, new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));
            db.ExpenseClaims.Add(otherEmployeeClaim);

            var paidAdvance = AdvanceRequest.Submit(tenantId, employeeId, 1000m, "TRY", receiptDate, false, null, null).Value;
            var advanceApprovalId = Guid.NewGuid();
            paidAdvance.AttachApproval(advanceApprovalId);
            paidAdvance.Approve(advanceApprovalId, DateTime.UtcNow);
            paidAdvance.MarkPaid(DateTime.UtcNow, otherEmployeeId, "elden odendi");
            db.AdvanceRequests.Add(paidAdvance);

            var approvedNotYetPaidAdvance = AdvanceRequest.Submit(tenantId, employeeId, 200m, "TRY", receiptDate, false, null, null).Value;
            var advanceApprovalId2 = Guid.NewGuid();
            approvedNotYetPaidAdvance.AttachApproval(advanceApprovalId2);
            approvedNotYetPaidAdvance.Approve(advanceApprovalId2, DateTime.UtcNow);
            db.AdvanceRequests.Add(approvedNotYetPaidAdvance);

            await db.SaveChangesAsync();
        }

        using (var readScope = ServiceProvider!.CreateScope())
        {
            var reader = readScope.ServiceProvider.GetRequiredService<IApprovedExpensePayoutReader>();

            var result = await reader.GetAsync(tenantId, 2026, 9, [employeeId, otherEmployeeId]);

            var mine = result.Single(r => r.EmployeeId == employeeId);
            mine.ApprovedExpenseTotal.ShouldBe(300m);
            mine.OpenAdvanceBalance.ShouldBe(1000m);

            var others = result.Single(r => r.EmployeeId == otherEmployeeId);
            others.ApprovedExpenseTotal.ShouldBe(150m);
            others.OpenAdvanceBalance.ShouldBe(0m);
        }
    }

    [Fact]
    public async Task GetAsync_WithEmptyEmployeeList_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using var scope = ServiceProvider!.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IApprovedExpensePayoutReader>();

        var result = await reader.GetAsync(tenantId, 2026, 9, []);

        result.ShouldBeEmpty();
    }
}

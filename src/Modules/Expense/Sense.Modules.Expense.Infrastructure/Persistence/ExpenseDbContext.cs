using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Expense.Application;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Expense.Infrastructure.Persistence;

/// <summary>Expense module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "expense").</summary>
public sealed class ExpenseDbContext(DbContextOptions<ExpenseDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IExpenseUnitOfWork
{
    public const string SchemaName = "expense";

    public override string Schema => SchemaName;

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();

    public DbSet<AdvanceRequest> AdvanceRequests => Set<AdvanceRequest>();

    /// <summary>expense.audit_log_entries - shared AuditLogEntry type in the module's own schema (same pattern as Leave/Employee/Workflow).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}

internal static class ExpenseTables
{
    public const string ExpenseCategories = "expense_categories";
    public const string ExpenseClaims = "expense_claims";
    public const string ExpenseItems = "expense_items";
    public const string AdvanceRequests = "advance_requests";

    public const int CodeLength = 100;
    public const int NameKeyLength = 200;
    public const int DescriptionLength = 1000;
    public const int CurrencyLength = 3;
    public const int FileKeyLength = 500;
    public const int StatusLength = 30;
}

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable(ExpenseTables.ExpenseCategories);
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(ExpenseTables.CodeLength).IsRequired();
        b.Property(x => x.NameKey).HasMaxLength(ExpenseTables.NameKeyLength).IsRequired();
        b.Property(x => x.DefaultTaxRate).HasPrecision(5, 2);
        // ADR-0005: model-only for now, see status/faz-07/MIGRATION-ISTEGI.md (AddExpenseCategoryLimit) - column
        // does not exist in the DB until that migration is generated and applied.
        b.Property(x => x.MaxAmount).HasPrecision(18, 2);

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ExpenseClaimConfiguration : IEntityTypeConfiguration<ExpenseClaim>
{
    public void Configure(EntityTypeBuilder<ExpenseClaim> b)
    {
        b.ToTable(ExpenseTables.ExpenseClaims);
        b.HasKey(x => x.Id);

        b.Property(x => x.Description).HasMaxLength(ExpenseTables.DescriptionLength);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(ExpenseTables.StatusLength).IsRequired();
        b.Property(x => x.PaymentNote).HasMaxLength(ExpenseTables.DescriptionLength);

        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasIndex(x => new { x.TenantId, x.Status });

        // ExpenseClaim.Items must always load fully - AddItem/UpdateItem/RemoveItem/Submit mutate the aggregate in memory.
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ExpenseItemConfiguration : IEntityTypeConfiguration<ExpenseItem>
{
    public void Configure(EntityTypeBuilder<ExpenseItem> b)
    {
        b.ToTable(ExpenseTables.ExpenseItems);
        b.HasKey(x => x.Id);

        // Client-generated Guid id (Guid.CreateVersion7() in ExpenseItem.Create): without this, EF's heuristic for a
        // row newly appended to an already-tracked ExpenseClaim.Items navigation (in a later SaveChanges than the one
        // that added the claim) can classify it as Modified instead of Added - same class of bug documented on
        // Workflow's ApprovalTaskConfiguration/WorkflowDbContext.cs.
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.TaxRate).HasPrecision(5, 2);
        b.Property(x => x.TaxAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(ExpenseTables.CurrencyLength).IsRequired();
        b.Property(x => x.ReceiptFileId).HasMaxLength(ExpenseTables.FileKeyLength);
        b.Property(x => x.Description).HasMaxLength(ExpenseTables.DescriptionLength);

        b.HasIndex(x => x.ClaimId);
        b.HasMany(x => x.ReceiptAttachments).WithOne().HasForeignKey(x => x.ExpenseItemId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.ReceiptAttachments).AutoInclude();
    }
}

public sealed class ExpenseReceiptAttachmentConfiguration : IEntityTypeConfiguration<ExpenseReceiptAttachment>
{
    public void Configure(EntityTypeBuilder<ExpenseReceiptAttachment> b)
    {
        b.ToTable("expense_receipt_attachments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.ExpenseItemId, x.DocumentId }).IsUnique();
    }
}

public sealed class AdvanceRequestConfiguration : IEntityTypeConfiguration<AdvanceRequest>
{
    public void Configure(EntityTypeBuilder<AdvanceRequest> b)
    {
        b.ToTable(ExpenseTables.AdvanceRequests);
        b.HasKey(x => x.Id);

        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(ExpenseTables.CurrencyLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(ExpenseTables.DescriptionLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(ExpenseTables.StatusLength).IsRequired();
        b.Property(x => x.PaymentNote).HasMaxLength(ExpenseTables.DescriptionLength);

        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasIndex(x => new { x.TenantId, x.Status });

        b.Ignore(x => x.DomainEvents);
    }
}

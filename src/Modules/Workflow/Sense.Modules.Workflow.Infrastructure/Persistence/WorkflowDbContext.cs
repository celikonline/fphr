using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Domain.Delegations;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Workflow.Infrastructure.Persistence;

/// <summary>Workflow module DbContext (approval engine). Schema "workflow".</summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "workflow";

    public override string Schema => SchemaName;

    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();

    public DbSet<ApprovalHistoryEntry> ApprovalHistoryEntries => Set<ApprovalHistoryEntry>();

    public DbSet<EmailDecisionToken> EmailDecisionTokens => Set<EmailDecisionToken>();

    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();

    /// <summary>senseik-049: workflow.audit_log_entries - bkz. AuditLogEntry'nin doc comment'i (Outbox/Inbox'la aynı "paylaşımlı tip, modül şeması" deseni).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>
    /// AuditLogEntryConfiguration ÖNCE uygulanmalı - bkz. EmployeeDbContext'teki aynı override'ın doc comment'i
    /// (ModuleDbContext.OnModelCreating'in Tenant query filter/index döngüsü base çağrısı sırasında çalışır).
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}

internal static class WorkflowTables
{
    public const string ApprovalPolicies = "approval_policies";
    public const string ApprovalPolicySteps = "approval_policy_steps";
    public const string ApprovalRequests = "approval_requests";
    public const string ApprovalTasks = "approval_tasks";
    public const string ApprovalHistoryEntries = "approval_history_entries";
    public const string EmailDecisionTokens = "email_decision_tokens";
    public const string ApprovalDelegations = "approval_delegations";

    /// <summary>String length for enum-as-string columns (Status, EventType, Outcome, Channel...).</summary>
    public const int EnumCodeLength = 30;

    /// <summary>Hex-encoded SHA-256 hash length (see IEmailTokenGenerator.Hash).</summary>
    public const int TokenHashLength = 128;
}

public sealed class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicy> b)
    {
        b.ToTable(WorkflowTables.ApprovalPolicies);
        b.HasKey(x => x.Id);

        b.Property(x => x.RequestType).HasMaxLength(WorkflowLimits.RequestTypeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(WorkflowLimits.NameMaxLength).IsRequired();

        // Not unique: multiple named policies may exist for the same request type - Organization's
        // OrgUnitApprovalPolicyAssignment decides which one applies (see ApprovalPolicy's class doc comment).
        b.HasIndex(x => new { x.TenantId, x.RequestType });

        b.OwnsMany(x => x.Steps, s =>
        {
            s.ToTable(WorkflowTables.ApprovalPolicySteps);
            s.WithOwner().HasForeignKey(x => x.PolicyId);
            s.HasKey(x => new { x.PolicyId, x.StepNumber });

            // Without this EF treats StepNumber as store-generated, so a newly added step row is saved as an UPDATE (0 rows).
            s.Property(x => x.StepNumber).ValueGeneratedNever();
        });

        b.Navigation(x => x.Steps).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> b)
    {
        b.ToTable(WorkflowTables.ApprovalRequests);
        b.HasKey(x => x.Id);

        b.Property(x => x.RequestType).HasMaxLength(WorkflowLimits.RequestTypeMaxLength).IsRequired();
        b.Property(x => x.PolicyNameAtSubmission).HasMaxLength(WorkflowLimits.NameMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(WorkflowTables.EnumCodeLength);
        b.Property(x => x.SubmittedIpAddress).HasMaxLength(WorkflowLimits.IpAddressMaxLength);
        b.Property(x => x.SubmittedUserAgent).HasMaxLength(WorkflowLimits.UserAgentMaxLength);

        b.HasIndex(x => new { x.TenantId, x.RequesterEmployeeId });
        b.HasIndex(x => new { x.TenantId, x.Status });

        // ApprovalRequest must always load fully (Tasks + History) - Decide()/Cancel() mutate the aggregate in memory.
        b.HasMany(x => x.Tasks).WithOne().HasForeignKey(t => t.ApprovalRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.History).WithOne().HasForeignKey(h => h.ApprovalRequestId).OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Tasks).AutoInclude();
        b.Navigation(x => x.History).AutoInclude();

        // Derived from Tasks/CurrentStepNumber, not a persisted relationship.
        b.Ignore(x => x.CurrentTask);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ApprovalTaskConfiguration : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> b)
    {
        b.ToTable(WorkflowTables.ApprovalTasks);
        b.HasKey(x => x.Id);

        // Id is client-generated (Guid.CreateVersion7() in the constructor). Without this, EF's heuristic for a task
        // newly appended to an already-tracked ApprovalRequest.Tasks (via Decide()/Submit() on a request loaded in a
        // *different* SaveChanges than the one that added it) can classify it as Modified instead of Added, which
        // flags every scalar - including TenantId - as changed and trips AuditTenantInterceptor's immutability check.
        // Same class of bug as ApprovalPolicyStep.StepNumber below; ValueGeneratedNever tells EF the key is never
        // store-assigned so a non-default value alone doesn't make it look "existing".
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.FallbackRoleCode).HasMaxLength(WorkflowLimits.RoleCodeMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(WorkflowTables.EnumCodeLength);
        b.Property(x => x.Comment).HasMaxLength(WorkflowLimits.CommentMaxLength);
        b.Property(x => x.DecisionChannel).HasConversion<string>().HasMaxLength(WorkflowTables.EnumCodeLength);
        b.Property(x => x.DecisionIpAddress).HasMaxLength(WorkflowLimits.IpAddressMaxLength);
        b.Property(x => x.DecisionUserAgent).HasMaxLength(WorkflowLimits.UserAgentMaxLength);

        // Inbox queries: tasks assigned directly to an employee, or via a matching fallback role queue.
        b.HasIndex(x => x.ApprovalRequestId);
        b.HasIndex(x => new { x.TenantId, x.ApproverEmployeeId });
        b.HasIndex(x => new { x.TenantId, x.FallbackRoleCode });
    }
}

public sealed class ApprovalHistoryEntryConfiguration : IEntityTypeConfiguration<ApprovalHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ApprovalHistoryEntry> b)
    {
        b.ToTable(WorkflowTables.ApprovalHistoryEntries);
        b.HasKey(x => x.Id);

        // See the identical comment on ApprovalTaskConfiguration - append-only history entries hit the exact same
        // Added-vs-Modified misclassification since they're appended to an already-tracked ApprovalRequest.History.
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(WorkflowTables.EnumCodeLength).IsRequired();
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(WorkflowTables.EnumCodeLength);
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(WorkflowTables.EnumCodeLength);
        b.Property(x => x.IpAddress).HasMaxLength(WorkflowLimits.IpAddressMaxLength);
        b.Property(x => x.UserAgent).HasMaxLength(WorkflowLimits.UserAgentMaxLength);
        b.Property(x => x.Note).HasMaxLength(WorkflowLimits.CommentMaxLength);

        b.HasIndex(x => new { x.TenantId, x.ApprovalRequestId });
    }
}

public sealed class EmailDecisionTokenConfiguration : IEntityTypeConfiguration<EmailDecisionToken>
{
    public void Configure(EntityTypeBuilder<EmailDecisionToken> b)
    {
        b.ToTable(WorkflowTables.EmailDecisionTokens);
        b.HasKey(x => x.Id);

        // Client-generated Guid key (see ApprovalTaskConfiguration's comment) - defensive consistency even though
        // today's only insertion path is an explicit repository.Add(), not navigation fixup.
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TokenHash).HasMaxLength(WorkflowTables.TokenHashLength).IsRequired();

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ApprovalTaskId });
    }
}

public sealed class ApprovalDelegationConfiguration : IEntityTypeConfiguration<ApprovalDelegation>
{
    public void Configure(EntityTypeBuilder<ApprovalDelegation> b)
    {
        b.ToTable(WorkflowTables.ApprovalDelegations);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        // Comma-joined request type codes: up to ~20 codes of RequestTypeMaxLength is plenty; null = all types.
        b.Property(x => x.RequestTypes).HasMaxLength(WorkflowLimits.RequestTypeMaxLength * 20);
        b.Ignore(x => x.IsRevoked);
        b.Ignore(x => x.RequestTypeList);

        b.HasIndex(x => new { x.TenantId, x.DelegateEmployeeId });
        b.HasIndex(x => new { x.TenantId, x.DelegatorEmployeeId });
    }
}

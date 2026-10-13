using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Adjustments;
using Sense.Modules.Payroll.Domain.Closing;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Modules.Payroll.Domain.Garnishments;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Domain.Unions;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Payroll.Infrastructure.Persistence;

/// <summary>Payroll module DbContext. Schema "payroll" (ADR 0003).</summary>
public sealed class PayrollDbContext(DbContextOptions<PayrollDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IPayrollUnitOfWork
{
    public const string SchemaName = "payroll";

    public override string Schema => SchemaName;

    public DbSet<PayrollPeriod> Periods => Set<PayrollPeriod>();

    public DbSet<Payslip> Payslips => Set<Payslip>();

    public DbSet<PayslipCalculationTrace> CalculationTraces => Set<PayslipCalculationTrace>();

    public DbSet<PayslipDelivery> PayslipDeliveries => Set<PayslipDelivery>();

    public DbSet<EmployeePayrollProfile> EmployeePayrollProfiles => Set<EmployeePayrollProfile>();

    public DbSet<PayslipDirtyFlag> DirtyFlags => Set<PayslipDirtyFlag>();

    public DbSet<ChangeDetectionRun> ChangeDetectionRuns => Set<ChangeDetectionRun>();

    public DbSet<GarnishmentOrder> GarnishmentOrders => Set<GarnishmentOrder>();

    public DbSet<Union> Unions => Set<Union>();

    public DbSet<EmployeeUnionMembership> EmployeeUnionMemberships => Set<EmployeeUnionMembership>();

    public DbSet<ExitSettlement> ExitSettlements => Set<ExitSettlement>();

    public DbSet<PeriodClosingCheck> ClosingChecks => Set<PeriodClosingCheck>();

    public DbSet<PayrollAdjustment> Adjustments => Set<PayrollAdjustment>();

    public DbSet<PayItemDefinition> PayItemDefinitions => Set<PayItemDefinition>();

    public DbSet<EmployeePayItemAssignment> PayItemAssignments => Set<EmployeePayItemAssignment>();

    public DbSet<IncentiveDefinition> IncentiveDefinitions => Set<IncentiveDefinition>();

    public DbSet<EmployeeIncentiveAssignment> IncentiveAssignments => Set<EmployeeIncentiveAssignment>();

    /// <summary>payroll.audit_log_entries - shared AuditLogEntry type in the module's own schema (same pattern as Leave/Employee/Workflow).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}

internal static class PayrollTables
{
    public const string Periods = "payroll_periods";
    public const string Payslips = "payslips";
    public const string PayslipDeliveries = "payslip_deliveries";
    public const string CalculationTraces = "payslip_calculation_traces";
    public const string EmployeePayrollProfiles = "employee_payroll_profiles";
    public const string DirtyFlags = "payslip_dirty_flags";
    public const string ChangeDetectionRuns = "change_detection_runs";
    public const string GarnishmentOrders = "garnishment_orders";
    public const string Unions = "unions";
    public const string EmployeeUnionMemberships = "employee_union_memberships";
    public const string ExitSettlements = "exit_settlements";
    public const string ExitSettlementLines = "exit_settlement_lines";
    public const string PeriodClosingChecks = "period_closing_checks";
    public const string Adjustments = "payroll_adjustments";
    public const string PayItemDefinitions = "payroll_pay_item_definitions";
    public const string PayItemAssignments = "payroll_pay_item_assignments";
    public const string IncentiveDefinitions = "payroll_incentive_definitions";
    public const string IncentiveAssignments = "payroll_incentive_assignments";
    public const int CurrencyLength = 3;
    public const int EnumCodeLength = 30;
    public const int HashLength = 128;
    public const int ReasonCodeLength = 40;
    public const int CaseReferenceMaxLength = 100;

    /// <summary>Shadow concurrency token mapped to PostgreSQL's xmin (Payslip).</summary>
    public const string RowVersion = "RowVersion";
}

public sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> b)
    {
        b.ToTable(PayrollTables.Periods);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    private static readonly JsonSerializerOptions HistoryJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly ValueComparer<IReadOnlyList<PayslipAcknowledgementRecord>> HistoryComparer = new(
        (a, b) => (a ?? Array.Empty<PayslipAcknowledgementRecord>()).SequenceEqual(b ?? Array.Empty<PayslipAcknowledgementRecord>()),
        v => v.Aggregate(0, (hash, r) => HashCode.Combine(hash, r)),
        v => v.ToList());

    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.ToTable(PayrollTables.Payslips);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.InputSnapshot).HasColumnType(PersistenceDefaults.JsonbColumnType);
        b.Property(x => x.InputHash).HasMaxLength(PayrollTables.HashLength).IsRequired();
        b.Property(x => x.AcknowledgedFromIp).HasMaxLength(PayrollLimits.ClientIpMaxLength);
        b.Property(x => x.AcknowledgedUserAgent).HasMaxLength(PayrollLimits.UserAgentMaxLength);
        b.Property(x => x.ObjectionStatus).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength).IsRequired().HasDefaultValue(PayslipObjectionStatus.None);
        b.Property(x => x.ObjectionReason).HasMaxLength(PayrollLimits.ObjectionReasonMaxLength);
        b.Ignore(x => x.AwaitsAcknowledgement);

        // Optimistic concurrency on PostgreSQL's xmin system column (no schema change): two acknowledgements, or an
        // acknowledgement racing an objection, cannot both win - the loser gets DbUpdateConcurrencyException (409).
        b.Property<uint>(PayrollTables.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        // K4: append-only evidence of earlier versions' acknowledgements/objections (Payslip.Recalculate archives them).
        b.Property(x => x.AcknowledgementHistory)
            .HasConversion(
                v => JsonSerializer.Serialize(v, HistoryJsonOptions),
                v => JsonSerializer.Deserialize<List<PayslipAcknowledgementRecord>>(v, HistoryJsonOptions) ?? new List<PayslipAcknowledgementRecord>())
            .Metadata.SetValueComparer(HistoryComparer);
        b.Property(x => x.AcknowledgementHistory)
            .HasColumnName("acknowledgement_history").HasColumnType(PersistenceDefaults.JsonbColumnType)
            .IsRequired().HasDefaultValueSql("'[]'::jsonb");
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PayrollPeriodId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PayslipDeliveryConfiguration : IEntityTypeConfiguration<PayslipDelivery>
{
    public void Configure(EntityTypeBuilder<PayslipDelivery> b)
    {
        b.ToTable(PayrollTables.PayslipDeliveries);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.EvidenceHash).HasMaxLength(PayrollTables.HashLength);
        b.HasIndex(x => x.PayslipId).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
    }
}

public sealed class PayslipCalculationTraceConfiguration : IEntityTypeConfiguration<PayslipCalculationTrace>
{
    public void Configure(EntityTypeBuilder<PayslipCalculationTrace> b)
    {
        b.ToTable(PayrollTables.CalculationTraces);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StepsJson).HasColumnType(PersistenceDefaults.JsonbColumnType);
        b.HasIndex(x => new { x.TenantId, x.PayslipId, x.Version });
    }
}

public sealed class EmployeePayrollProfileConfiguration : IEntityTypeConfiguration<EmployeePayrollProfile>
{
    public void Configure(EntityTypeBuilder<EmployeePayrollProfile> b)
    {
        b.ToTable(PayrollTables.EmployeePayrollProfiles);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.WageBasis).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.SgkStatus).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.MealBenefitVariant).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.TransportVariant).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.IncentiveLawCode).HasMaxLength(PayrollLimits.IncentiveLawCodeMaxLength);
        b.Property(x => x.ExternalPayrollCode).HasMaxLength(PayrollLimits.ExternalPayrollCodeMaxLength);
        b.Property(x => x.Notes).HasMaxLength(PayrollLimits.NotesMaxLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ValidFrom });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PayslipDirtyFlagConfiguration : IEntityTypeConfiguration<PayslipDirtyFlag>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<string>> ReasonsComparer = new(
        (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
        v => v.ToList());

    public void Configure(EntityTypeBuilder<PayslipDirtyFlag> b)
    {
        b.ToTable(PayrollTables.DirtyFlags);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Reasons)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>())
            .Metadata.SetValueComparer(ReasonsComparer);
        b.Property(x => x.Reasons).HasColumnName("reasons").HasColumnType(PersistenceDefaults.JsonbColumnType).IsRequired();

        // Partial-unique-index style constraint (one active flag per period/employee) is enforced in the Application
        // layer (PayslipDirtyFlagService merges into the existing active flag) rather than a filtered DB index, to
        // keep multiple cleared (historical) flags per (period, employee) - EF Core has no portable filtered-index API.
        b.HasIndex(x => new { x.TenantId, x.PayrollPeriodId, x.EmployeeId, x.ClearedAt });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ChangeDetectionRunConfiguration : IEntityTypeConfiguration<ChangeDetectionRun>
{
    public void Configure(EntityTypeBuilder<ChangeDetectionRun> b)
    {
        b.ToTable(PayrollTables.ChangeDetectionRuns);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.HasIndex(x => new { x.TenantId, x.PeriodId, x.StartedAt });
    }
}

/// <summary>senseik-019 2c: icra/nafaka kesinti emirleri.</summary>
public sealed class GarnishmentOrderConfiguration : IEntityTypeConfiguration<GarnishmentOrder>
{
    public void Configure(EntityTypeBuilder<GarnishmentOrder> b)
    {
        b.ToTable(PayrollTables.GarnishmentOrders);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.CaseReference).HasMaxLength(PayrollTables.CaseReferenceMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ClosedDate });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class UnionConfiguration : IEntityTypeConfiguration<Union>
{
    public void Configure(EntityTypeBuilder<Union> b)
    {
        b.ToTable(PayrollTables.Unions);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(PayrollLimits.UnionNameMaxLength).IsRequired();
        b.Property(x => x.IndustryBranchCode).HasMaxLength(PayrollLimits.UnionIndustryBranchCodeMaxLength);
        b.HasIndex(x => new { x.TenantId, x.Name });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class EmployeeUnionMembershipConfiguration : IEntityTypeConfiguration<EmployeeUnionMembership>
{
    public void Configure(EntityTypeBuilder<EmployeeUnionMembership> b)
    {
        b.ToTable(PayrollTables.EmployeeUnionMemberships);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CoverageType).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.DuesRule).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.MembershipDate });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ExitSettlementConfiguration : IEntityTypeConfiguration<ExitSettlement>
{
    public void Configure(EntityTypeBuilder<ExitSettlement> b)
    {
        b.ToTable(PayrollTables.ExitSettlements);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CauseCategory).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Currency).HasMaxLength(PayrollTables.CurrencyLength).IsRequired();
        b.Property(x => x.StepsJson).HasColumnType(PersistenceDefaults.JsonbColumnType).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.TerminationDate }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ExitSettlementId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ExitSettlementLineConfiguration : IEntityTypeConfiguration<ExitSettlementLine>
{
    public void Configure(EntityTypeBuilder<ExitSettlementLine> b)
    {
        b.ToTable(PayrollTables.ExitSettlementLines);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Label).HasMaxLength(PayrollLimits.ExitLineLabelMaxLength).IsRequired();
        b.Ignore(x => x.Treatment);
        b.HasIndex(x => x.ExitSettlementId);
    }
}

public sealed class PeriodClosingCheckConfiguration : IEntityTypeConfiguration<PeriodClosingCheck>
{
    public void Configure(EntityTypeBuilder<PeriodClosingCheck> b)
    {
        b.ToTable(PayrollTables.PeriodClosingChecks);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(PayrollTables.EnumCodeLength * 2).IsRequired();
        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Detail).HasMaxLength(PayrollLimits.ClosingCheckTextMaxLength);
        b.Property(x => x.WaiverReason).HasMaxLength(PayrollLimits.ClosingCheckTextMaxLength);
        b.Ignore(x => x.IsSatisfied);
        b.HasIndex(x => new { x.TenantId, x.PeriodId, x.Code }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> b)
    {
        b.ToTable(PayrollTables.Adjustments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Label).HasMaxLength(PayrollLimits.AdjustmentLabelMaxLength).IsRequired();
        b.Property(x => x.IsNet).HasDefaultValue(false);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Year, x.Month });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PayItemDefinitionConfiguration : IEntityTypeConfiguration<PayItemDefinition>
{
    public void Configure(EntityTypeBuilder<PayItemDefinition> b)
    {
        b.ToTable(PayrollTables.PayItemDefinitions);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(PayItemDefinition.CodeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(PayItemDefinition.NameMaxLength).IsRequired();
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.AmountBasis).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.Formula).HasMaxLength(Domain.PayItems.Formulas.FormulaLimits.MaxTextLength).IsRequired();
        b.Property(x => x.DefaultValue).HasPrecision(18, 4);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Ignore(x => x.UsesValue);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class EmployeePayItemAssignmentConfiguration : IEntityTypeConfiguration<EmployeePayItemAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeePayItemAssignment> b)
    {
        b.ToTable(PayrollTables.PayItemAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Value).HasPrecision(18, 4);
        b.HasOne<PayItemDefinition>().WithMany().HasForeignKey(x => x.PayItemDefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ValidFromMonth });
        b.HasIndex(x => x.PayItemDefinitionId);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class IncentiveDefinitionConfiguration : IEntityTypeConfiguration<IncentiveDefinition>
{
    private const string ParametersColumn = "parameters";

    private static readonly ValueConverter<IncentiveCoverageBase, string> CoverageBaseConverter = new(
        v => char.ToLowerInvariant(v.ToString()[0]) + v.ToString().Substring(1),
        v => Enum.Parse<IncentiveCoverageBase>(v, true));

    public void Configure(EntityTypeBuilder<IncentiveDefinition> b)
    {
        b.ToTable(PayrollTables.IncentiveDefinitions);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(IncentiveDefinition.NameMaxLength).IsRequired();
        b.Property(x => x.LawReference).HasMaxLength(IncentiveDefinition.LawReferenceMaxLength);
        b.Property(x => x.DeclarationCode).HasMaxLength(IncentiveDefinition.DeclarationCodeMaxLength);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(PayrollTables.EnumCodeLength);
        b.Property(x => x.ApprovalNote).HasMaxLength(IncentiveDefinition.ApprovalNoteMaxLength);
        b.Property(x => x.TemplateKey).HasMaxLength(PayrollTables.EnumCodeLength);

        // Kind-specific parameters: one jsonb column in the same shape as the API (camelCase names, camelCase enum text).
        b.OwnsOne(x => x.Parameters, p =>
        {
            p.ToJson(ParametersColumn);
            p.Property(x => x.CoverageBase).HasConversion(CoverageBaseConverter).HasJsonPropertyName("coverageBase");
            p.Property(x => x.CapMultiplier).HasJsonPropertyName("capMultiplier");
            p.Property(x => x.CoversEmployerSgk).HasJsonPropertyName("coversEmployerSgk");
            p.Property(x => x.CoversEmployerUnemployment).HasJsonPropertyName("coversEmployerUnemployment");
            p.Property(x => x.CoversEmployeeSgk).HasJsonPropertyName("coversEmployeeSgk");
            p.Property(x => x.CoversEmployeeUnemployment).HasJsonPropertyName("coversEmployeeUnemployment");
            p.OwnsOne(x => x.Rate, ConfigureRate("rate"));
            p.OwnsOne(x => x.EmployerSgkRate, ConfigureRate("employerSgkRate"));
        });

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.TemplateKey }).IsUnique().HasFilter("template_key IS NOT NULL");
        b.Ignore(x => x.Category);
        b.Ignore(x => x.IsComplete);
        b.Ignore(x => x.DomainEvents);
    }

    private static Action<OwnedNavigationBuilder<IncentiveParameters, IncentiveRate>> ConfigureRate(string jsonName) =>
        r =>
        {
            r.HasJsonPropertyName(jsonName);
            r.Property(x => x.Value).HasJsonPropertyName("value");
            r.Property(x => x.LegislationKey).HasJsonPropertyName("legislationKey");
            r.Ignore(x => x.IsResolvable);
        };
}

public sealed class EmployeeIncentiveAssignmentConfiguration : IEntityTypeConfiguration<EmployeeIncentiveAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeIncentiveAssignment> b)
    {
        b.ToTable(PayrollTables.IncentiveAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Note).HasMaxLength(EmployeeIncentiveAssignment.NoteMaxLength);
        b.HasOne<IncentiveDefinition>().WithMany().HasForeignKey(x => x.IncentiveDefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ValidFromMonth });
        b.HasIndex(x => x.IncentiveDefinitionId);
        b.Ignore(x => x.MonthCount);
        b.Ignore(x => x.DomainEvents);
    }
}

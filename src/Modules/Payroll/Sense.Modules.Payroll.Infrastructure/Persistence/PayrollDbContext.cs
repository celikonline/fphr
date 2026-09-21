using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Garnishments;
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

    public DbSet<EmployeePayrollProfile> EmployeePayrollProfiles => Set<EmployeePayrollProfile>();

    public DbSet<PayslipDirtyFlag> DirtyFlags => Set<PayslipDirtyFlag>();

    public DbSet<ChangeDetectionRun> ChangeDetectionRuns => Set<ChangeDetectionRun>();

    public DbSet<GarnishmentOrder> GarnishmentOrders => Set<GarnishmentOrder>();

    public DbSet<Union> Unions => Set<Union>();

    public DbSet<EmployeeUnionMembership> EmployeeUnionMemberships => Set<EmployeeUnionMembership>();

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
    public const string CalculationTraces = "payslip_calculation_traces";
    public const string EmployeePayrollProfiles = "employee_payroll_profiles";
    public const string DirtyFlags = "payslip_dirty_flags";
    public const string ChangeDetectionRuns = "change_detection_runs";
    public const string GarnishmentOrders = "garnishment_orders";
    public const string Unions = "unions";
    public const string EmployeeUnionMemberships = "employee_union_memberships";
    public const int EnumCodeLength = 30;
    public const int HashLength = 128;
    public const int ReasonCodeLength = 40;
    public const int CaseReferenceMaxLength = 100;
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
    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.ToTable(PayrollTables.Payslips);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.InputSnapshot).HasColumnType(PersistenceDefaults.JsonbColumnType);
        b.Property(x => x.InputHash).HasMaxLength(PayrollTables.HashLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PayrollPeriodId }).IsUnique();
        b.Ignore(x => x.DomainEvents);
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

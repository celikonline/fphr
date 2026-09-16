using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

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
}

internal static class PayrollTables
{
    public const string Periods = "payroll_periods";
    public const string Payslips = "payslips";
    public const string CalculationTraces = "payslip_calculation_traces";
    public const string EmployeePayrollProfiles = "employee_payroll_profiles";
    public const int EnumCodeLength = 30;
    public const int HashLength = 128;
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

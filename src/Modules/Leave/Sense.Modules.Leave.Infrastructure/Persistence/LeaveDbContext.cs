using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Legislation;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Persistence;

/// <summary>Leave module DbContext. Schema "leave"; also hosts the system-wide legislation_parameters table (ADR 0008).</summary>
public sealed class LeaveDbContext(DbContextOptions<LeaveDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), ILeaveUnitOfWork
{
    public const string SchemaName = "leave";

    public override string Schema => SchemaName;

    public DbSet<LeaveRuleSet> RuleSets => Set<LeaveRuleSet>();

    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();

    public DbSet<EmployeeLeaveProfile> Profiles => Set<EmployeeLeaveProfile>();

    public DbSet<LeaveLedgerEntry> LedgerEntries => Set<LeaveLedgerEntry>();

    public DbSet<LegislationParameter> LegislationParameters => Set<LegislationParameter>();

    public DbSet<LeaveRequest> Requests => Set<LeaveRequest>();

    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();

    public DbSet<OvertimeConsent> OvertimeConsents => Set<OvertimeConsent>();

    public DbSet<OvertimeConversion> OvertimeConversions => Set<OvertimeConversion>();
}

internal static class LeaveTables
{
    public const string RuleSets = "rule_sets";
    public const string LeaveTypes = "leave_types";
    public const string Profiles = "employee_leave_profiles";
    public const string LedgerEntries = "ledger_entries";
    public const string LegislationParameters = "legislation_parameters";
    public const string Requests = "leave_requests";
    public const string OvertimeRequests = "overtime_requests";
    public const string OvertimeConsents = "overtime_consents";
    public const string OvertimeConversions = "overtime_conversions";
    public const int RatePrecision = 5;
    public const int RateScale = 2;
    public const int IpAddressLength = 64;
    public const int EnumCodeLength = 30;
    public const int KeyLength = 100;
    public const int CountryLength = 2;
    public const int DaysPrecision = 9;
    public const int DaysScale = 4;
    public static readonly DateTime SeedCreatedAt = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
}

public sealed class LeaveRuleSetConfiguration : IEntityTypeConfiguration<LeaveRuleSet>
{
    public void Configure(EntityTypeBuilder<LeaveRuleSet> b)
    {
        b.ToTable(LeaveTables.RuleSets);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(LeaveLimits.NameMaxLength).IsRequired();
        b.HasMany(x => x.Types).WithOne().HasForeignKey(t => t.RuleSetId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Types).AutoInclude();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.ToTable(LeaveTables.LeaveTypes);
        b.HasKey(x => x.Id);

        // Client-generated key appended to an already-tracked rule set: without this EF classifies it as Modified (see WorkflowDbContext).
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(LeaveLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(LeaveLimits.DescriptionMaxLength);
        b.Property(x => x.Code).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.LimitType).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.NegativeBalance).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.AccrualFrequency).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.AccrualTiming).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.AccrualBasis).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.CarryOverMode).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Unit).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        foreach (var days in new[] { nameof(LeaveType.PerRequestMaxDays), nameof(LeaveType.AnnualDays), nameof(LeaveType.AgeExceptionMinDays),
                     nameof(LeaveType.NegativeBalanceLimitDays), nameof(LeaveType.CarryOverMaxDays), nameof(LeaveType.MinConsecutiveDays), nameof(LeaveType.MaxConsecutiveDays) })
        {
            b.Property(days).HasPrecision(LeaveTables.DaysPrecision, LeaveTables.DaysScale);
        }

        b.OwnsMany(x => x.SeniorityTiers, t =>
        {
            t.ToJson("seniority_tiers");
            t.Property(x => x.Comparison).HasConversion<string>();
        });
        b.Navigation(x => x.SeniorityTiers).HasField("_seniorityTiers").UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => new { x.TenantId, x.RuleSetId });
    }
}

public sealed class EmployeeLeaveProfileConfiguration : IEntityTypeConfiguration<EmployeeLeaveProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeLeaveProfile> b)
    {
        b.ToTable(LeaveTables.Profiles);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.RuleSetId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class LeaveLedgerEntryConfiguration : IEntityTypeConfiguration<LeaveLedgerEntry>
{
    public void Configure(EntityTypeBuilder<LeaveLedgerEntry> b)
    {
        b.ToTable(LeaveTables.LedgerEntries);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Amount).HasPrecision(LeaveTables.DaysPrecision, LeaveTables.DaysScale);
        b.Property(x => x.Reason).HasMaxLength(LeaveLimits.ReasonMaxLength);
        b.Property(x => x.IdempotencyKey).HasMaxLength(LeaveLimits.IdempotencyKeyMaxLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.EffectiveDate });
        b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        b.Ignore(x => x.IsGenerated);
    }
}

public sealed class LegislationParameterConfiguration : IEntityTypeConfiguration<LegislationParameter>
{
    public void Configure(EntityTypeBuilder<LegislationParameter> b)
    {
        b.ToTable(LeaveTables.LegislationParameters);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Key).HasMaxLength(LeaveTables.KeyLength).IsRequired();
        b.Property(x => x.Country).HasMaxLength(LeaveTables.CountryLength).IsRequired();
        b.Property(x => x.ValueJson).HasColumnType(PersistenceDefaults.JsonbColumnType);
        b.Property(x => x.Source).HasMaxLength(LeaveLimits.ReasonMaxLength).IsRequired();
        b.HasIndex(x => new { x.Country, x.Key, x.EffectiveFrom }).IsUnique();

        b.HasData(LegislationParameterSeed.All.Select(p => new
        {
            p.Id,
            p.Key,
            p.Country,
            p.DecimalValue,
            p.ValueJson,
            p.EffectiveFrom,
            p.EffectiveTo,
            p.Source,
            CreatedAt = LeaveTables.SeedCreatedAt,
        }));
    }
}

public sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.ToTable(LeaveTables.Requests);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.LeaveTypeCodeSnapshot).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.CalculatedDays).HasPrecision(LeaveTables.DaysPrecision, LeaveTables.DaysScale);
        b.Property(x => x.Description).HasMaxLength(LeaveLimits.DescriptionMaxLength);
        b.Property(x => x.SubmittedIpAddress).HasMaxLength(LeaveTables.IpAddressLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartDate });
        b.HasIndex(x => new { x.TenantId, x.ApprovalRequestId });
        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OvertimeRequestConfiguration : IEntityTypeConfiguration<OvertimeRequest>
{
    public void Configure(EntityTypeBuilder<OvertimeRequest> b)
    {
        b.ToTable(LeaveTables.OvertimeRequests);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DayKind).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Description).HasMaxLength(LeaveLimits.DescriptionMaxLength);
        b.Property(x => x.SubmittedIpAddress).HasMaxLength(LeaveTables.IpAddressLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate });
        b.HasIndex(x => new { x.TenantId, x.ApprovalRequestId });
        b.Ignore(x => x.StartAt);
        b.Ignore(x => x.EndAt);
        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OvertimeConsentConfiguration : IEntityTypeConfiguration<OvertimeConsent>
{
    public void Configure(EntityTypeBuilder<OvertimeConsent> b)
    {
        b.ToTable(LeaveTables.OvertimeConsents);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OvertimeConversionConfiguration : IEntityTypeConfiguration<OvertimeConversion>
{
    public void Configure(EntityTypeBuilder<OvertimeConversion> b)
    {
        b.ToTable(LeaveTables.OvertimeConversions);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.MinutesBasis).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(LeaveTables.EnumCodeLength);
        b.Property(x => x.RateSnapshot).HasPrecision(LeaveTables.RatePrecision, LeaveTables.RateScale);
        b.Property(x => x.ResultDays).HasPrecision(LeaveTables.DaysPrecision, LeaveTables.DaysScale);
        b.Property(x => x.Note).HasMaxLength(LeaveLimits.ReasonMaxLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });

        // One active (posted) conversion per overtime request (11.3.5).
        b.HasIndex(x => x.OvertimeRequestId).IsUnique().HasFilter("status = 'Posted'");
        b.Ignore(x => x.DomainEvents);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Modules.Attendance.Domain.Compensatory;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Attendance.Infrastructure.Persistence;

/// <summary>Attendance module DbContext. Schema "attendance".</summary>
public sealed class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IAttendanceUnitOfWork
{
    public const string SchemaName = "attendance";

    public override string Schema => SchemaName;

    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();

    public DbSet<EmployeeWorkScheduleAssignment> WorkScheduleAssignments => Set<EmployeeWorkScheduleAssignment>();

    public DbSet<AttendanceDay> AttendanceDays => Set<AttendanceDay>();

    public DbSet<AnomalyResolution> AnomalyResolutions => Set<AnomalyResolution>();

    public DbSet<ShiftSettings> ShiftSettings => Set<ShiftSettings>();

    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();

    public DbSet<ShiftPlanLock> ShiftPlanLocks => Set<ShiftPlanLock>();

    public DbSet<EmployeeAvailability> EmployeeAvailabilities => Set<EmployeeAvailability>();

    public DbSet<CompensatoryWorkLedger> CompensatoryWorkLedgers => Set<CompensatoryWorkLedger>();

    public DbSet<MonthlyTimesheetApproval> MonthlyTimesheetApprovals => Set<MonthlyTimesheetApproval>();

    public DbSet<EqualizationPeriod> EqualizationPeriods => Set<EqualizationPeriod>();
    public DbSet<SelfCheckIn> SelfCheckIns => Set<SelfCheckIn>();
}

internal static class AttendanceTables
{
    public const string WorkSchedules = "work_schedules";
    public const string WorkScheduleAssignments = "employee_work_schedule_assignments";
    public const string AttendanceDays = "attendance_days";
    public const string AnomalyResolutions = "anomaly_resolutions";
    public const string ShiftSettings = "shift_settings";
    public const string ShiftAssignments = "shift_assignments";
    public const string ShiftPlanLocks = "shift_plan_locks";
    public const string EmployeeAvailabilities = "employee_availabilities";
    public const string CompensatoryWorkLedgers = "compensatory_work_ledgers";
    public const string MonthlyTimesheetApprovals = "monthly_timesheet_approvals";
    public const string EqualizationPeriods = "equalization_periods";
    public const string SelfCheckIns = "self_check_ins";
    public const int OverrideReasonMaxLength = 1000;
    public const int LinkedEntityTypeMaxLength = 50;
    public const int NoteMaxLength = 1000;
    public const int UnlockReasonMaxLength = 1000;
}

public sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.ToTable(AttendanceTables.WorkSchedules);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(AttendanceLimits.NameMaxLength).IsRequired();

        b.OwnsMany(x => x.Days, d =>
        {
            d.ToJson("days");
            d.Property(x => x.DayOfWeek).HasConversion<string>();
        });
        b.Navigation(x => x.Days).HasField("_days").UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.WeeklyMinutes);
    }
}

public sealed class EmployeeWorkScheduleAssignmentConfiguration : IEntityTypeConfiguration<EmployeeWorkScheduleAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeWorkScheduleAssignment> b)
    {
        b.ToTable(AttendanceTables.WorkScheduleAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Source).HasConversion<string>();
        b.Property(x => x.EffectiveTo);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveFrom }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.WorkScheduleId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AttendanceDayConfiguration : IEntityTypeConfiguration<AttendanceDay>
{
    public void Configure(EntityTypeBuilder<AttendanceDay> b)
    {
        b.ToTable(AttendanceTables.AttendanceDays);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Anomalies).HasConversion<string>();
        b.Property(x => x.ResolutionState).HasConversion<string>();
        b.Property(x => x.Source).HasConversion<string>();
        b.Property(x => x.OverrideReason).HasMaxLength(AttendanceTables.OverrideReasonMaxLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AnomalyResolutionConfiguration : IEntityTypeConfiguration<AnomalyResolution>
{
    public void Configure(EntityTypeBuilder<AnomalyResolution> b)
    {
        b.ToTable(AttendanceTables.AnomalyResolutions);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Anomaly).HasConversion<string>();
        b.Property(x => x.Action).HasConversion<string>();
        b.Property(x => x.LinkedEntityType).HasMaxLength(AttendanceTables.LinkedEntityTypeMaxLength);
        b.HasIndex(x => new { x.TenantId, x.AttendanceDayId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ShiftSettingsConfiguration : IEntityTypeConfiguration<ShiftSettings>
{
    public void Configure(EntityTypeBuilder<ShiftSettings> b)
    {
        b.ToTable(AttendanceTables.ShiftSettings);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.TenantId).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ShiftAssignmentConfiguration : IEntityTypeConfiguration<ShiftAssignment>
{
    public void Configure(EntityTypeBuilder<ShiftAssignment> b)
    {
        b.ToTable(AttendanceTables.ShiftAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>();
        b.Property(x => x.Note).HasMaxLength(AttendanceTables.NoteMaxLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date }).IsUnique();
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.ShiftMinutes);
    }
}

public sealed class ShiftPlanLockConfiguration : IEntityTypeConfiguration<ShiftPlanLock>
{
    public void Configure(EntityTypeBuilder<ShiftPlanLock> b)
    {
        b.ToTable(AttendanceTables.ShiftPlanLocks);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Source).HasConversion<string>();
        b.Property(x => x.UnlockReason).HasMaxLength(AttendanceTables.UnlockReasonMaxLength);
        b.HasIndex(x => new { x.TenantId, x.PeriodMonth }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class EmployeeAvailabilityConfiguration : IEntityTypeConfiguration<EmployeeAvailability>
{
    public void Configure(EntityTypeBuilder<EmployeeAvailability> b)
    {
        b.ToTable(AttendanceTables.EmployeeAvailabilities);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>();
        b.Property(x => x.Status).HasConversion<string>();
        b.Property(x => x.Note).HasMaxLength(AttendanceTables.NoteMaxLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class SelfCheckInConfiguration : IEntityTypeConfiguration<SelfCheckIn>
{
    public void Configure(EntityTypeBuilder<SelfCheckIn> b)
    {
        b.ToTable(AttendanceTables.SelfCheckIns);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Direction).HasConversion<string>();
        b.Property(x => x.Method).HasConversion<string>();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.TimestampUtc });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class CompensatoryWorkLedgerConfiguration : IEntityTypeConfiguration<CompensatoryWorkLedger>
{
    public void Configure(EntityTypeBuilder<CompensatoryWorkLedger> b)
    {
        b.ToTable(AttendanceTables.CompensatoryWorkLedgers);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WeekStart }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class MonthlyTimesheetApprovalConfiguration : IEntityTypeConfiguration<MonthlyTimesheetApproval>
{
    public void Configure(EntityTypeBuilder<MonthlyTimesheetApproval> b)
    {
        b.ToTable(AttendanceTables.MonthlyTimesheetApprovals);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PeriodMonth }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class EqualizationPeriodConfiguration : IEntityTypeConfiguration<EqualizationPeriod>
{
    public void Configure(EntityTypeBuilder<EqualizationPeriod> b)
    {
        b.ToTable(AttendanceTables.EqualizationPeriods);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PeriodStart }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status, x.PeriodEnd });
        b.Ignore(x => x.DomainEvents);
    }
}

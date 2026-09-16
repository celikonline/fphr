using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
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
}

internal static class AttendanceTables
{
    public const string WorkSchedules = "work_schedules";
    public const string WorkScheduleAssignments = "employee_work_schedule_assignments";
    public const string AttendanceDays = "attendance_days";
    public const string AnomalyResolutions = "anomaly_resolutions";
    public const int OverrideReasonMaxLength = 1000;
    public const int LinkedEntityTypeMaxLength = 50;
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

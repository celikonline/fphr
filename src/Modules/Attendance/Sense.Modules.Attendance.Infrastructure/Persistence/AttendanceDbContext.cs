using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Modules.Attendance.Domain.Compensatory;
using Sense.Modules.Attendance.Domain.Corrections;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

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

    public DbSet<PunchCorrectionRequest> PunchCorrectionRequests => Set<PunchCorrectionRequest>();

    /// <summary>attendance.audit_log_entries - shared AuditLogEntry type in the module's own schema (same pattern as Leave/Employee/Workflow).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    // zaman-devam W0-a (11.10): ham okutma, kural setli vardiya, eksik kayıt talebi, şablon, rotasyon, terminal.
    public DbSet<Punch> Punches => Set<Punch>();

    public DbSet<ShiftDefinition> ShiftDefinitions => Set<ShiftDefinition>();

    public DbSet<PunchRequest> PunchRequests => Set<PunchRequest>();

    public DbSet<ShiftPlanTemplate> ShiftPlanTemplates => Set<ShiftPlanTemplate>();

    public DbSet<RotationPattern> RotationPatterns => Set<RotationPattern>();

    public DbSet<RotationAssignment> RotationAssignments => Set<RotationAssignment>();

    public DbSet<TerminalGroup> TerminalGroups => Set<TerminalGroup>();

    public DbSet<Terminal> Terminals => Set<Terminal>();

    public DbSet<EmployeeTerminalCredential> EmployeeTerminalCredentials => Set<EmployeeTerminalCredential>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
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
    public const string PunchCorrectionRequests = "punch_correction_requests";
    public const int OverrideReasonMaxLength = 1000;
    public const int LinkedEntityTypeMaxLength = 50;
    public const int NoteMaxLength = 1000;
    public const int UnlockReasonMaxLength = 1000;
    public const string Punches = "punches";
    public const string ShiftDefinitions = "shift_definitions";
    public const string PunchRequests = "punch_requests";
    public const string ShiftPlanTemplates = "shift_plan_templates";
    public const string RotationPatterns = "rotation_patterns";
    public const string RotationAssignments = "rotation_assignments";
    public const string TerminalGroups = "terminal_groups";
    public const string Terminals = "terminals";
    public const string EmployeeTerminalCredentials = "employee_terminal_credentials";
    public const string PunchUniqueIndex = "ux_punches_tenant_employee_occurred_direction_source";

    /// <summary>Gölge eşzamanlılık belirteci; PostgreSQL xmin sistem sütununa eşlenir (AttendanceDay).</summary>
    public const string RowVersion = "RowVersion";

    /// <summary>attendance_days (TenantId, EmployeeId, Date) tekillik indeksinin (EF adlandırması) adı - eşzamanlı gün oluşturma yarışı.</summary>
    public const string AttendanceDayUniqueIndex = "ix_attendance_days_tenant_id_employee_id_date";
    public const string PunchNotDeletedFilter = "deleted_at_utc IS NULL";
    public const string ShiftBreaksColumn = "breaks";
    public const string ShiftTemplateEntriesColumn = "entries";
    public const string RotationStepsColumn = "steps";
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

        // I0: iyimser eşzamanlılık PostgreSQL xmin sistem sütunuyla (şema değişikliği yok, Payroll Payslip kalıbı) - iki kanal aynı günü
        // aynı anda yeniden türetirse geç kalan DbUpdateConcurrencyException alır, PunchDayWriter günü taze veriden yeniden türetir.
        b.Property<uint>(AttendanceTables.RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
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
        b.HasIndex(x => new { x.TenantId, x.ShiftDefinitionId });
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

/// <summary>Ham okutma: tekillik yalnız silinmemiş satırlarda (silinen okutma aynı anahtarla yeniden girilebilir).</summary>
public sealed class PunchConfiguration : IEntityTypeConfiguration<Punch>
{
    public void Configure(EntityTypeBuilder<Punch> b)
    {
        b.ToTable(AttendanceTables.Punches);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Direction).HasConversion<string>();
        b.Property(x => x.Source).HasConversion<string>();
        b.Property(x => x.SourceRef).HasMaxLength(Punch.SourceRefMaxLength);
        b.Property(x => x.Reason).HasMaxLength(Punch.ReasonMaxLength);
        b.Property(x => x.DeleteReason).HasMaxLength(Punch.ReasonMaxLength);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate });
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.OccurredAtUtc, x.Direction, x.Source })
            .IsUnique()
            .HasFilter(AttendanceTables.PunchNotDeletedFilter)
            .HasDatabaseName(AttendanceTables.PunchUniqueIndex);
        b.Ignore(x => x.IsDeleted);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ShiftDefinitionConfiguration : IEntityTypeConfiguration<ShiftDefinition>
{
    public void Configure(EntityTypeBuilder<ShiftDefinition> b)
    {
        b.ToTable(AttendanceTables.ShiftDefinitions);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(ShiftDefinition.CodeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(AttendanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.ColorHex).HasMaxLength(ShiftDefinition.ColorHexLength).IsRequired();
        b.Property(x => x.Type).HasConversion<string>();
        b.OwnsMany(x => x.Breaks, br => br.ToJson(AttendanceTables.ShiftBreaksColumn));
        b.Navigation(x => x.Breaks).HasField("_breaks").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PunchRequestConfiguration : IEntityTypeConfiguration<PunchRequest>
{
    public void Configure(EntityTypeBuilder<PunchRequest> b)
    {
        b.ToTable(AttendanceTables.PunchRequests);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Direction).HasConversion<string>();
        b.Property(x => x.Status).HasConversion<string>();
        b.Property(x => x.Reason).HasMaxLength(PunchRequest.ReasonMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate });
        b.HasIndex(x => new { x.TenantId, x.ApprovalRequestId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ShiftPlanTemplateConfiguration : IEntityTypeConfiguration<ShiftPlanTemplate>
{
    public void Configure(EntityTypeBuilder<ShiftPlanTemplate> b)
    {
        b.ToTable(AttendanceTables.ShiftPlanTemplates);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(AttendanceLimits.NameMaxLength).IsRequired();
        b.OwnsMany(x => x.Entries, e =>
        {
            e.ToJson(AttendanceTables.ShiftTemplateEntriesColumn);
            e.Property(x => x.DayType).HasConversion<string>();
        });
        b.Navigation(x => x.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RotationPatternConfiguration : IEntityTypeConfiguration<RotationPattern>
{
    public void Configure(EntityTypeBuilder<RotationPattern> b)
    {
        b.ToTable(AttendanceTables.RotationPatterns);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(RotationPattern.CodeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(AttendanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Preset).HasConversion<string>();
        b.OwnsMany(x => x.Steps, s =>
        {
            s.ToJson(AttendanceTables.RotationStepsColumn);
            s.Property(x => x.DayType).HasConversion<string>();
        });
        b.Navigation(x => x.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RotationAssignmentConfiguration : IEntityTypeConfiguration<RotationAssignment>
{
    public void Configure(EntityTypeBuilder<RotationAssignment> b)
    {
        b.ToTable(AttendanceTables.RotationAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartDate });
        b.HasIndex(x => new { x.TenantId, x.RotationPatternId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class TerminalGroupConfiguration : IEntityTypeConfiguration<TerminalGroup>
{
    public void Configure(EntityTypeBuilder<TerminalGroup> b)
    {
        b.ToTable(AttendanceTables.TerminalGroups);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(AttendanceLimits.NameMaxLength).IsRequired();
        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>ApiKeyPrefix indeksi kiracıdan bağımsızdır: terminal alım ucu kiracıyı anahtardan çözer.</summary>
public sealed class TerminalConfiguration : IEntityTypeConfiguration<Terminal>
{
    public void Configure(EntityTypeBuilder<Terminal> b)
    {
        b.ToTable(AttendanceTables.Terminals);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(Terminal.CodeMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(AttendanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.DirectionMode).HasConversion<string>();
        b.Property(x => x.ApiKeyHash).HasMaxLength(Terminal.ApiKeyHashMaxLength).IsRequired();
        b.Property(x => x.ApiKeyPrefix).HasMaxLength(Terminal.ApiKeyPrefixMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => x.ApiKeyPrefix);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class EmployeeTerminalCredentialConfiguration : IEntityTypeConfiguration<EmployeeTerminalCredential>
{
    public void Configure(EntityTypeBuilder<EmployeeTerminalCredential> b)
    {
        b.ToTable(AttendanceTables.EmployeeTerminalCredentials);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>();
        b.Property(x => x.Value).HasMaxLength(EmployeeTerminalCredential.ValueMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Kind, x.Value }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class PunchCorrectionRequestConfiguration : IEntityTypeConfiguration<PunchCorrectionRequest>
{
    public void Configure(EntityTypeBuilder<PunchCorrectionRequest> b)
    {
        b.ToTable(AttendanceTables.PunchCorrectionRequests);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>();
        b.Property(x => x.Reason).HasMaxLength(PunchCorrectionLimits.ReasonMaxLength).IsRequired();

        // FG-004: one open (Pending) request per employee and day; closed ones stay as history.
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.AttendanceDate }).IsUnique().HasFilter("status = 'Pending'");
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.SubmittedAt });
        b.Ignore(x => x.DomainEvents);
    }
}

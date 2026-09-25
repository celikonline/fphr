using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Modules.Attendance.Domain.Compensatory;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain;

public interface IWorkScheduleRepository
{
    void Add(WorkSchedule schedule);

    void Remove(WorkSchedule schedule);

    Task<Result<WorkSchedule>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkSchedule?> FindDefaultAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkSchedule>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<int> CountAssignedEmployeesAsync(Guid workScheduleId, CancellationToken cancellationToken = default);
}

public interface IEmployeeWorkScheduleAssignmentRepository
{
    void Add(EmployeeWorkScheduleAssignment assignment);

    void Remove(EmployeeWorkScheduleAssignment assignment);

    Task<Result<EmployeeWorkScheduleAssignment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The assignment in effect on <paramref name="date"/> (latest EffectiveFrom on or before it), or null if none.</summary>
    Task<EmployeeWorkScheduleAssignment?> FindEffectiveOnAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Every assignment of the employee that could be effective at some point in [from, to] (the one just before <paramref name="from"/> included).</summary>
    Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListCoveringRangeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IAttendanceDayRepository
{
    void Add(AttendanceDay day);

    Task<Result<AttendanceDay>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AttendanceDay?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceDay>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Ekip haftalık ızgarası (11.3.6): every recorded day in [from, to] across a set of employees, in one query.</summary>
    Task<IReadOnlyList<AttendanceDay>> ListForEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IAnomalyResolutionRepository
{
    void Add(AnomalyResolution resolution);

    Task<IReadOnlyList<AnomalyResolution>> ListForDayAsync(Guid attendanceDayId, CancellationToken cancellationToken = default);
}

public interface IShiftSettingsRepository
{
    void Add(ShiftSettings settings);

    Task<ShiftSettings?> FindAsync(CancellationToken cancellationToken = default);
}

public interface IShiftAssignmentRepository
{
    void Add(ShiftAssignment assignment);

    void Remove(ShiftAssignment assignment);

    Task<Result<ShiftAssignment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ShiftAssignment?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Haftalık/aylık görünüm (11.3.6): every assignment in [from, to], optionally scoped to a set of employees.</summary>
    Task<IReadOnlyList<ShiftAssignment>> ListForRangeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);

    /// <summary>"Bekleyen/paylaşılmamış vardiyalar" sayacı (11.3.6 aylık görünüm).</summary>
    Task<int> CountUnpublishedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IShiftPlanLockRepository
{
    void Add(ShiftPlanLock plan);

    Task<ShiftPlanLock?> FindByPeriodAsync(DateOnly periodMonth, CancellationToken cancellationToken = default);
}

public interface IEmployeeAvailabilityRepository
{
    void Add(EmployeeAvailability availability);

    Task<Result<EmployeeAvailability>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeAvailability>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IMonthlyTimesheetApprovalRepository
{
    void Add(MonthlyTimesheetApproval approval);

    Task<MonthlyTimesheetApproval?> FindAsync(Guid employeeId, DateOnly periodMonth, CancellationToken cancellationToken = default);

    /// <summary>Aylık puantaj ızgarası (S-11): every existing approval row for these employees in this period, in one query.</summary>
    Task<IReadOnlyList<MonthlyTimesheetApproval>> ListForPeriodAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly periodMonth, CancellationToken cancellationToken = default);
}

/// <summary>Write-once audit log (S-31); no update/remove - see <see cref="SelfCheckIn"/>.</summary>
public interface ISelfCheckInRepository
{
    void Add(SelfCheckIn checkIn);
}

public interface ICompensatoryWorkLedgerRepository
{
    void Add(CompensatoryWorkLedger ledger);

    Task<Result<CompensatoryWorkLedger>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CompensatoryWorkLedger?> FindByEmployeeAndWeekAsync(Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompensatoryWorkLedger>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IEqualizationPeriodRepository
{
    void Add(EqualizationPeriod period);

    /// <summary>The period row whose [PeriodStart, PeriodEnd] contains <paramref name="date"/>, if one has already been opened.</summary>
    Task<EqualizationPeriod?> FindCoveringAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Closed periods whose PeriodEnd falls inside [from, to] (Payroll bridge: attributes the true-up to the month a period closes in).</summary>
    Task<IReadOnlyList<EqualizationPeriod>> ListClosedEndingInRangeAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ham okutmalar (zaman-devam Z0.1). Sorgular açık <c>tenantId</c> alır: yeniden hesaplama arka planda kiracı bağlamı
/// olmadan da çalışır. Yumuşak silinmiş okutmalar hiçbir sorguda dönmez.
/// </summary>
public interface IPunchRepository
{
    void Add(Punches.Punch punch);

    /// <summary>Tek okutma (tenant içinde); silinmiş okutma dönmez.</summary>
    Task<Punches.Punch?> FindAsync(Guid tenantId, Guid punchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Punches.Punch>> ListForDayAsync(Guid tenantId, Guid employeeId, DateOnly workDate, CancellationToken cancellationToken = default);

    /// <summary>[from, to] aralığındaki WorkDate'ler; <paramref name="employeeIds"/> null ise kiracının tüm çalışanları.</summary>
    Task<IReadOnlyList<Punches.Punch>> ListForRangeAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Tekillik anahtarı (Tenant, Employee, OccurredAtUtc, Direction, Source) silinmemiş bir okutmada var mı - idempotent alım için.</summary>
    Task<bool> ExistsAsync(
        Guid tenantId, Guid employeeId, DateTime occurredAtUtc, PunchDirection direction, Punches.PunchSource source,
        CancellationToken cancellationToken = default);
}

public interface IShiftDefinitionRepository
{
    void Add(ShiftDefinition definition);

    void Remove(ShiftDefinition definition);

    Task<Result<ShiftDefinition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiftDefinition>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiftDefinition>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Kod tenant içinde tekil mi (<paramref name="excludeId"/> güncellenen tanımın kendisi).</summary>
    Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default);

    /// <summary>"Geçmişe uygula" etki alanı: bu tanımlardan birine bağlı, tarihi [from, to] içindeki vardiya atamaları.</summary>
    Task<IReadOnlyList<ShiftAssignment>> ListAssignmentsUsingAsync(
        IReadOnlyCollection<Guid> definitionIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>shift_definition_id FK'sız (uygulama bütünlüğü): bir atama, rotasyon adımı ya da plan şablonu hücresi bu tanıma bağlı mı.</summary>
    Task<bool> IsInUseAsync(Guid definitionId, CancellationToken cancellationToken = default);
}

public interface IPunchRequestRepository
{
    void Add(PunchRequests.PunchRequest request);

    Task<Result<PunchRequests.PunchRequest>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PunchRequests.PunchRequest?> FindByApprovalRequestIdAsync(Guid approvalRequestId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PunchRequests.PunchRequest>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Yönetici listesi: tenant'ın talepleri, isteğe bağlı durum süzgeciyle, en yeni önce.</summary>
    Task<IReadOnlyList<PunchRequests.PunchRequest>> ListAsync(PunchRequests.PunchRequestStatus? status, CancellationToken cancellationToken = default);
}

public interface IShiftPlanTemplateRepository
{
    void Add(ShiftPlanTemplate template);

    void Remove(ShiftPlanTemplate template);

    Task<Result<ShiftPlanTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiftPlanTemplate>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IRotationRepository
{
    void AddPattern(Rotations.RotationPattern pattern);

    Task<Result<Rotations.RotationPattern>> GetPatternByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Rotations.RotationPattern>> ListPatternsAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<bool> PatternCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default);

    void AddAssignment(Rotations.RotationAssignment assignment);

    Task<Result<Rotations.RotationAssignment>> GetAssignmentByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>[from, to] ile kesişen atamalar; <paramref name="employeeIds"/> null ise tümü.</summary>
    Task<IReadOnlyList<Rotations.RotationAssignment>> ListAssignmentsOverlappingAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);
}

public interface ITerminalRepository
{
    void AddTerminal(Terminals.Terminal terminal);

    Task<Result<Terminals.Terminal>> GetTerminalByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Terminals.Terminal>> ListTerminalsAsync(CancellationToken cancellationToken = default);

    Task<bool> TerminalCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default);

    /// <summary>Terminal alım ucu (kiracı bağlamı henüz yok): anahtar önekine göre tüm kiracılarda aktif terminalleri arar; özet karşılaştırması çağırandadır.</summary>
    Task<IReadOnlyList<Terminals.Terminal>> ListActiveByApiKeyPrefixAcrossTenantsAsync(string apiKeyPrefix, CancellationToken cancellationToken = default);

    void AddGroup(Terminals.TerminalGroup group);

    Task<Result<Terminals.TerminalGroup>> GetGroupByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Terminals.TerminalGroup>> ListGroupsAsync(CancellationToken cancellationToken = default);

    void AddCredential(Terminals.EmployeeTerminalCredential credential);

    Task<Result<Terminals.EmployeeTerminalCredential>> GetCredentialByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Terminal okutmasını çalışana eşler: kiracı + tür + değer (yalnız aktif kimlikler).</summary>
    Task<Terminals.EmployeeTerminalCredential?> FindActiveCredentialAsync(
        Guid tenantId, Terminals.TerminalCredentialKind kind, string value, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Terminals.EmployeeTerminalCredential>> ListCredentialsForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

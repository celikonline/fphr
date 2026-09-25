using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.AttendanceDays;

/// <summary>
/// One employee's one calendar day of attendance (11.3.6 "Puantaj hücresi"): plan vs. actual, computed against the
/// <see cref="WorkSchedules.WorkSchedule"/> day in effect for that date. PlannedStart/End are a snapshot taken at
/// record time (mirrors <see cref="WorkSchedules.EmployeeWorkScheduleAssignment"/>'s dated-history approach) so a
/// later schedule edit never rewrites already-recorded history. One row per (EmployeeId, Date); a re-import or
/// re-record overwrites it (11.3.6: "aynı çalışan+gün tekrar yüklenirse üzerine yazar").
/// </summary>
public sealed class AttendanceDay : TenantAggregateRoot<Guid>
{
    private AttendanceDay()
    {
    }

    private AttendanceDay(
        Guid id, Guid tenantId, Guid employeeId, DateOnly date, TimeOnly? checkIn, TimeOnly? checkOut,
        TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes, AttendanceDaySource source,
        bool overnight, int actualBreakMinutes, ShiftLimits? shiftLimits)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Date = date;
        Source = source;
        ResolutionState = AttendanceResolutionState.Unresolved;
        Recompute(checkIn, checkOut, plannedStart, plannedEnd, plannedMinutes, breakMinutes, overnight, actualBreakMinutes, shiftLimits);
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public TimeOnly? CheckIn { get; private set; }

    public TimeOnly? CheckOut { get; private set; }

    /// <summary>Schedule snapshot at record time; null on both when the date was not a working day.</summary>
    public TimeOnly? PlannedStart { get; private set; }

    public TimeOnly? PlannedEnd { get; private set; }

    public int PlannedMinutes { get; private set; }

    public int BreakMinutes { get; private set; }

    public int WorkedMinutes { get; private set; }

    /// <summary>PDKS'de ilk "in" ile son "out" arasında görülen ara Out→In çiftlerinin toplamı (G-02, gerçek mola) - <see cref="BreakMinutes"/> (planlanan sabit mola) ile karıştırılmamalı. Manuel/Excel kayıtlarda ölçülmediği için 0.</summary>
    public int ActualBreakMinutes { get; private set; }

    /// <summary>m.68 uygulanan mola: max(<see cref="ActualBreakMinutes"/>, o günün brüt süresine göre yasal asgari) - bkz. <see cref="AttendanceDays.DailyWorkCalculator"/>.</summary>
    public int BreakMinutesApplied { get; private set; }

    /// <summary>Check-in→check-out brüt süresinden <see cref="BreakMinutesApplied"/> düşülmüş net çalışma süresi (G-02 hesaplama motoru çıktısı; legacy <see cref="WorkedMinutes"/>'tan farklı olabilir).</summary>
    public int NetWorkedMinutes { get; private set; }

    /// <summary>Gece çalışma penceresiyle (varsa <c>Shifts.ShiftSettings</c>) kesişen dakikalar (m.69).</summary>
    public int NightMinutes { get; private set; }

    /// <summary>m.63: günlük çalışma süresi üst sınırı (varsayılan 11 saat) aşıldı mı.</summary>
    public bool DailyLimitExceeded { get; private set; }

    /// <summary>m.69: gece çalışması 7,5 saat üst sınırı aşıldı mı (yalnız <c>ShiftSettings.NightWorkEnabled</c> açıkken anlamlı).</summary>
    public bool NightLimitExceeded { get; private set; }

    /// <summary>Günlük fazla mesai vekili (NetWorkedMinutes − PlannedMinutes, 0'da taban): kesin değer için denkleştirme dönemine bakılmalı.</summary>
    public int OvertimeMinutes { get; private set; }

    public AttendanceAnomaly Anomalies { get; private set; }

    public AttendanceResolutionState ResolutionState { get; private set; }

    /// <summary>"Çalışılan saat" resolution (package A3): overrides <see cref="WorkedMinutes"/> for reporting without re-deriving from CheckIn/CheckOut.</summary>
    public int? OverrideWorkedMinutes { get; private set; }

    public string? OverrideReason { get; private set; }

    public AttendanceDaySource Source { get; private set; }

    public static AttendanceDay Record(
        Guid tenantId, Guid employeeId, DateOnly date, TimeOnly? checkIn, TimeOnly? checkOut,
        TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes, AttendanceDaySource source,
        bool overnight = false, int actualBreakMinutes = 0, ShiftLimits? shiftLimits = null) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, date, checkIn, checkOut, plannedStart, plannedEnd, plannedMinutes, breakMinutes, source, overnight,
            actualBreakMinutes, shiftLimits);

    /// <summary>
    /// Overwrites this day's check-in/out and re-derives worked minutes and anomaly flags (re-import or re-record).
    /// <paramref name="overnight"/> (G-6-14, 4857 md.69) marks a night shift whose check-out punch belongs to the
    /// next calendar day but is attributed back to this day's record (PDKS import) - in that case
    /// <paramref name="checkOut"/> being earlier in the day than <paramref name="checkIn"/> is expected, and worked
    /// minutes wrap past midnight instead of being rejected as reversed order.
    /// </summary>
    public Result UpdateRecord(
        TimeOnly? checkIn, TimeOnly? checkOut, TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes, AttendanceDaySource source,
        bool overnight = false, int actualBreakMinutes = 0, ShiftLimits? shiftLimits = null)
    {
        if (!overnight && checkIn is not null && checkOut is not null && checkOut <= checkIn)
        {
            return Error.Validation(AttendanceDomainErrors.DayCheckOutBeforeCheckIn);
        }

        Source = source;
        Recompute(checkIn, checkOut, plannedStart, plannedEnd, plannedMinutes, breakMinutes, overnight, actualBreakMinutes, shiftLimits);
        return Result.Success();
    }

    /// <summary>
    /// "Çalışılan saat" resolution (11.3.6 Az çalışma → Çalışılan saat): overrides <see cref="WorkedMinutes"/> for
    /// reporting and resolves the day without touching CheckIn/CheckOut. Also used as the manual-correction action
    /// for missing/unplanned-data anomalies that have no Overwork/Underwork flag.
    /// </summary>
    public Result ResolveWithOverride(int overrideWorkedMinutes, string? reason)
    {
        var guard = EnsureResolvable();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (overrideWorkedMinutes is < 0 or > AttendanceLimits.MaxMinutesPerDay)
        {
            return Error.Validation(AttendanceDomainErrors.OverrideMinutesInvalid);
        }

        OverrideWorkedMinutes = overrideWorkedMinutes;
        OverrideReason = reason;
        ResolutionState = AttendanceResolutionState.Resolved;
        return Result.Success();
    }

    /// <summary>AddOvertime/CreateLeaveRequest resolution: the anomaly is addressed by a Leave-module record the caller links via <see cref="AnomalyResolution"/>, not by changing this day's own numbers.</summary>
    public Result MarkResolvedByLinkedEntity()
    {
        var guard = EnsureResolvable();
        if (guard.IsFailure)
        {
            return guard;
        }

        ResolutionState = AttendanceResolutionState.Resolved;
        return Result.Success();
    }

    /// <summary>Dismisses a flagged day without an override or a linked Leave record (manual "no action needed" call).</summary>
    public Result Ignore()
    {
        var guard = EnsureResolvable();
        if (guard.IsFailure)
        {
            return guard;
        }

        ResolutionState = AttendanceResolutionState.Ignored;
        return Result.Success();
    }

    /// <summary>
    /// İzinli günde çalışma çapraz kontrolü (senseik-029): Leave.Contracts üzerinden bu günün onaylı bir izin
    /// talebiyle kesiştiği öğrenildiğinde <see cref="AttendanceAnomaly.WorkedWhileOnLeave"/> bayrağını ekler; artık
    /// kesişmiyorsa (izin iptal edildi/tarihi değişti) kaldırır. Yalnız fiilen bir giriş/çıkış kaydı olan günlerde
    /// anlamlıdır - izin gününde hiç kayıt yoksa bu zaten normal bir izin günüdür, anomali değildir. Recompute'un
    /// aksine tek başına Application katmanınca (Leave lookup sonrası) çağrılır, çünkü günün kendi planı/kaydı bu
    /// bilgiyi taşımaz. Zaten çözümlenmiş/yok sayılmış bir güne yeni bayrak eklenmesi günü yeniden Unresolved yapar -
    /// Recompute'un her yeniden hesaplamada yaptığıyla aynı kural; bayrak kalkınca başka anomali kalmadıysa gün
    /// tekrar Resolved sayılır.
    /// </summary>
    public void ApplyLeaveOverlap(bool overlapsApprovedLeave)
    {
        var shouldFlag = overlapsApprovedLeave && (CheckIn is not null || CheckOut is not null);
        var currentlyFlagged = (Anomalies & AttendanceAnomaly.WorkedWhileOnLeave) != 0;
        if (shouldFlag == currentlyFlagged)
        {
            return;
        }

        Anomalies = shouldFlag ? Anomalies | AttendanceAnomaly.WorkedWhileOnLeave : Anomalies & ~AttendanceAnomaly.WorkedWhileOnLeave;
        if (shouldFlag)
        {
            ResolutionState = AttendanceResolutionState.Unresolved;
        }
        else if (Anomalies == AttendanceAnomaly.None)
        {
            ResolutionState = AttendanceResolutionState.Resolved;
        }
    }

    private Result EnsureResolvable()
    {
        if (Anomalies == AttendanceAnomaly.None)
        {
            return Error.Validation(AttendanceDomainErrors.NoAnomalyToResolve);
        }

        if (ResolutionState != AttendanceResolutionState.Unresolved)
        {
            return Error.Validation(AttendanceDomainErrors.DayAlreadyResolved);
        }

        return Result.Success();
    }

    private void Recompute(
        TimeOnly? checkIn, TimeOnly? checkOut, TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes, bool overnight = false,
        int actualBreakMinutes = 0, ShiftLimits? shiftLimits = null)
    {
        CheckIn = checkIn;
        CheckOut = checkOut;
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
        PlannedMinutes = plannedMinutes;
        BreakMinutes = breakMinutes;
        ActualBreakMinutes = actualBreakMinutes;

        // A re-record/re-import overwrites the day's own facts (class doc: "aynı çalışan+gün tekrar yüklenirse üzerine
        // yazar"), so any prior manual resolution is superseded along with it - new data means a fresh evaluation.
        OverrideWorkedMinutes = null;
        OverrideReason = null;

        WorkedMinutes = checkIn is not null && checkOut is not null
            ? Math.Max((int)WorkedSpan(checkIn.Value, checkOut.Value, overnight).TotalMinutes - breakMinutes, 0)
            : 0;

        // G-02 puantaj hesaplama motoru (kart senseik-014): mola/gece/limit/fazla mesai vekili DailyWorkCalculator'a
        // delege edilir - legacy WorkedMinutes (üstte) hâlâ Overwork/Underwork anomali kıyaslaması için korunur.
        var limits = shiftLimits ?? ShiftLimits.None;
        var dailyWork = DailyWorkCalculator.Calculate(
            new DailyWorkInput(checkIn, checkOut, overnight, actualBreakMinutes, plannedMinutes, limits, plannedStart, plannedEnd));
        NetWorkedMinutes = dailyWork.NetWorkedMinutes;
        BreakMinutesApplied = dailyWork.BreakMinutesApplied;
        NightMinutes = dailyWork.NightMinutes;
        DailyLimitExceeded = dailyWork.DailyLimitExceeded;
        NightLimitExceeded = dailyWork.NightLimitExceeded;
        OvertimeMinutes = dailyWork.OvertimeMinutes;

        var isWorkingDay = plannedStart is not null && plannedEnd is not null;
        var anomalies = AttendanceAnomaly.None;

        if (isWorkingDay)
        {
            if (checkIn is null && checkOut is null)
            {
                anomalies |= AttendanceAnomaly.NoData;
            }

            if (checkIn is null && checkOut is not null)
            {
                anomalies |= AttendanceAnomaly.MissingCheckIn;
            }

            if (checkOut is null && checkIn is not null)
            {
                anomalies |= AttendanceAnomaly.MissingCheckOut;
            }

            // Z0.2: vardiya tanımı toleransları (kural yoksa tolerans 0 = önceki kesin karşılaştırma).
            if (DailyWorkCalculator.IsLateArrival(checkIn, plannedStart, limits.Rules))
            {
                anomalies |= AttendanceAnomaly.LateArrival;
            }

            if (DailyWorkCalculator.IsEarlyLeave(checkOut, plannedEnd, limits.Rules))
            {
                anomalies |= AttendanceAnomaly.EarlyLeave;
            }

            if (checkOut > plannedEnd)
            {
                anomalies |= AttendanceAnomaly.LateLeave;
            }

            if (checkIn is not null && checkOut is not null)
            {
                if (WorkedMinutes > plannedMinutes)
                {
                    anomalies |= AttendanceAnomaly.Overwork;
                }
                else if (WorkedMinutes < plannedMinutes)
                {
                    anomalies |= AttendanceAnomaly.Underwork;
                }
            }
        }
        else if (checkIn is not null || checkOut is not null)
        {
            anomalies |= AttendanceAnomaly.UnplannedWork;
        }

        Anomalies = anomalies;
        ResolutionState = anomalies == AttendanceAnomaly.None ? AttendanceResolutionState.Resolved : AttendanceResolutionState.Unresolved;
    }

    /// <summary>Elapsed time from check-in to check-out; wraps past midnight for an <paramref name="overnight"/> shift
    /// (same pattern as <c>Shifts.ShiftAssignment.ShiftMinutes</c>) instead of yielding a negative span.</summary>
    private static TimeSpan WorkedSpan(TimeOnly checkIn, TimeOnly checkOut, bool overnight)
    {
        var span = checkOut.ToTimeSpan() - checkIn.ToTimeSpan();
        if (overnight && span <= TimeSpan.Zero)
        {
            span += TimeSpan.FromDays(1);
        }

        return span;
    }
}

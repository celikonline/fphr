using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// <see cref="IPunchRecorder"/> (Z0.1): okutmanın puantaj günü plan penceresinden çözülür (<see cref="WorkDateResolver"/>, gece vardiyası
/// çakışması → Incalculable), K6 kilitli ay (onaylı aylık puantaj) reddedilir / toplu alımda atlanır, aynı tekillik anahtarı (Tenant,
/// Employee, OccurredAtUtc, Direction, Source) hata değil yoksayma sayılır; etkilenen günler okutmalardan yeniden türetilir ve kaydedilir.
/// Komut hattı dışından (entegrasyon olayı, terminal) da çağrılabildiği için her işlem kendi değişikliklerini kaydeder.
/// </summary>
public sealed class PunchRecorder(
    IPunchRepository punches, IAttendanceDayRepository days, IWorkScheduleReadService schedules, IMonthlyTimesheetApprovalRepository approvals,
    IShiftSettingsRepository shiftSettings, ITenantLocalClock clock, IAttendanceUnitOfWork unitOfWork) : IPunchRecorder
{
    private readonly IPunchRepository _punches = punches;
    private readonly IAttendanceDayRepository _days = days;
    private readonly IWorkScheduleReadService _schedules = schedules;
    private readonly IMonthlyTimesheetApprovalRepository _approvals = approvals;
    private readonly IShiftSettingsRepository _shiftSettings = shiftSettings;
    private readonly ITenantLocalClock _clock = clock;
    private readonly IAttendanceUnitOfWork _unitOfWork = unitOfWork;

    private enum StageKind
    {
        Added,
        Duplicate,
        Locked,
    }

    private sealed record Staged(StageKind Kind, Guid PunchId);

    public async Task<Result<Guid>> RecordAsync(PunchInput input, CancellationToken ct)
    {
        var session = await StartAsync(input.TenantId, ct).ConfigureAwait(false);
        var staged = await session.StageAsync(input, ct).ConfigureAwait(false);
        if (staged.IsFailure)
        {
            return staged.Error;
        }

        switch (staged.Value.Kind)
        {
            case StageKind.Locked:
                return Error.Conflict(AttendanceErrors.PeriodLocked);
            case StageKind.Duplicate:
                return staged.Value.PunchId;
        }

        var saved = await session.CommitAsync(ct).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error : staged.Value.PunchId;
    }

    public async Task<Result<PunchBatchResult>> RecordBatchAsync(IReadOnlyList<PunchInput> inputs, CancellationToken ct)
    {
        var sessions = new Dictionary<Guid, Session>();
        var accepted = 0;
        var ignored = 0;
        foreach (var input in inputs)
        {
            if (!sessions.TryGetValue(input.TenantId, out var session))
            {
                session = await StartAsync(input.TenantId, ct).ConfigureAwait(false);
                sessions[input.TenantId] = session;
            }

            var staged = await session.StageAsync(input, ct).ConfigureAwait(false);
            if (staged.IsFailure)
            {
                return staged.Error;
            }

            if (staged.Value.Kind == StageKind.Added)
            {
                accepted++;
            }
            else
            {
                ignored++;
            }
        }

        foreach (var session in sessions.Values)
        {
            var rebuilt = await session.RebuildAsync(ct).ConfigureAwait(false);
            if (rebuilt.IsFailure)
            {
                return rebuilt.Error;
            }
        }

        if (accepted > 0)
        {
            await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new PunchBatchResult(accepted, ignored);
    }

    public async Task<Result> SoftDeleteAsync(Guid tenantId, Guid punchId, Guid userId, string reason, CancellationToken ct)
    {
        var punch = await _punches.FindByIdAsync(tenantId, punchId, ct).ConfigureAwait(false);
        if (punch is null)
        {
            return Error.NotFound(AttendanceErrors.PunchNotFound);
        }

        if (await IsLockedAsync(punch.EmployeeId, punch.WorkDate, ct).ConfigureAwait(false))
        {
            return Error.Conflict(AttendanceErrors.PeriodLocked);
        }

        var session = await StartAsync(tenantId, ct).ConfigureAwait(false);
        var deleted = session.Writer.SoftDelete(punch, userId, reason);
        if (deleted.IsFailure)
        {
            return deleted;
        }

        session.Touch(punch.EmployeeId, punch.WorkDate, source: null, ambiguous: false);
        return await session.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task<Session> StartAsync(Guid tenantId, CancellationToken ct)
    {
        var zone = await _clock.GetTimeZoneAsync(tenantId, ct).ConfigureAwait(false);
        var now = await _clock.NowAsync(tenantId, ct).ConfigureAwait(false);
        var limits = AttendanceDayUpsert.ToShiftLimits(await _shiftSettings.FindAsync(ct).ConfigureAwait(false));
        return new Session(this, tenantId, new PunchDayWriter(tenantId, _days, _punches, _schedules, zone, now.Utc), now.Utc, limits);
    }

    private async Task<bool> IsLockedAsync(Guid employeeId, DateOnly workDate, CancellationToken ct) =>
        await _approvals.FindAsync(employeeId, MonthlyTimesheetApproval.NormalizeToMonthStart(workDate), ct).ConfigureAwait(false) is not null;

    /// <summary>Tek kiracı için bir kayıt işlemi: yeni okutmalar, dokunulan günler ve plan/kilit önbellekleri.</summary>
    private sealed class Session(PunchRecorder owner, Guid tenantId, PunchDayWriter writer, DateTime nowUtc, ShiftLimits limits)
    {
        private readonly Dictionary<(Guid EmployeeId, DateTime OccurredAtUtc, PunchDirection Direction, PunchSource Source), Guid> _seen = [];
        private readonly Dictionary<(Guid EmployeeId, DateOnly WorkDate), (AttendanceDaySource? Source, bool Ambiguous)> _touched = [];
        private readonly Dictionary<(Guid EmployeeId, DateOnly Date), PlannedDay> _plans = [];

        public PunchDayWriter Writer => writer;

        public async Task<Result<Staged>> StageAsync(PunchInput input, CancellationToken ct)
        {
            if (Validate(input) is { } invalid)
            {
                return invalid;
            }

            var occurredAtUtc = Punch.NormalizeOccurredAt(input.OccurredAtUtc);
            var direction = ToDirection(input.Direction);
            var source = ToSource(input.Source);
            var key = (input.EmployeeId, occurredAtUtc, direction, source);
            if (_seen.TryGetValue(key, out var seenId))
            {
                return new Staged(StageKind.Duplicate, seenId);
            }

            var local = PunchTime.ToLocal(occurredAtUtc, writer.Zone);
            var resolution = WorkDateResolver.ResolveDetailed(local, await CandidatesAsync(input.EmployeeId, DateOnly.FromDateTime(local), ct).ConfigureAwait(false));
            if (await owner.IsLockedAsync(input.EmployeeId, resolution.Date, ct).ConfigureAwait(false))
            {
                return new Staged(StageKind.Locked, Guid.Empty);
            }

            if (await owner._punches.ExistsAsync(tenantId, input.EmployeeId, occurredAtUtc, direction, source, ct).ConfigureAwait(false))
            {
                var stored = await owner._punches.ListForRangeAsync(tenantId, resolution.Date.AddDays(-1), resolution.Date.AddDays(1), [input.EmployeeId], ct)
                    .ConfigureAwait(false);
                var existingId = stored.FirstOrDefault(p => p.OccurredAtUtc == occurredAtUtc && p.Direction == direction && p.Source == source)?.Id ?? Guid.Empty;
                _seen[key] = existingId;
                return new Staged(StageKind.Duplicate, existingId);
            }

            await writer.LoadDayAsync(input.EmployeeId, resolution.Date, seedLegacy: true, ct).ConfigureAwait(false);
            var added = writer.Add(
                input.EmployeeId, occurredAtUtc, resolution.Date, direction, source, input.SourceRef, input.TerminalId, input.LocationId, input.Latitude,
                input.Longitude, input.IsManual, input.CreatedByUserId, input.Reason, nowUtc);
            if (added.IsFailure)
            {
                return added.Error;
            }

            _seen[key] = added.Value.Id;
            Touch(input.EmployeeId, resolution.Date, PunchTime.ToDaySource(source), resolution.Ambiguous);
            return new Staged(StageKind.Added, added.Value.Id);
        }

        public void Touch(Guid employeeId, DateOnly workDate, AttendanceDaySource? source, bool ambiguous)
        {
            var wasAmbiguous = _touched.TryGetValue((employeeId, workDate), out var previous) && previous.Ambiguous;
            _touched[(employeeId, workDate)] = (source ?? previous.Source, ambiguous || wasAmbiguous);
        }

        public async Task<Result> RebuildAsync(CancellationToken ct)
        {
            foreach (var ((employeeId, workDate), (source, ambiguous)) in _touched)
            {
                var rebuilt = await writer.RebuildAsync(employeeId, workDate, source, limits, ambiguous, ct).ConfigureAwait(false);
                if (rebuilt.IsFailure)
                {
                    return rebuilt.Error;
                }
            }

            return Result.Success();
        }

        public async Task<Result> CommitAsync(CancellationToken ct)
        {
            var rebuilt = await RebuildAsync(ct).ConfigureAwait(false);
            if (rebuilt.IsFailure)
            {
                return rebuilt;
            }

            await owner._unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }

        /// <summary>Yerel takvim gününün bir öncesi, kendisi ve sonrası (sonraki günün vardiyası önceki akşam başlayabilir).</summary>
        private async Task<IReadOnlyList<(DateOnly Date, PlannedDay? Plan)>> CandidatesAsync(Guid employeeId, DateOnly localDate, CancellationToken ct)
        {
            var candidates = new List<(DateOnly Date, PlannedDay? Plan)>();
            for (var offset = -1; offset <= 1; offset++)
            {
                var date = localDate.AddDays(offset);
                if (!_plans.TryGetValue((employeeId, date), out var plan))
                {
                    var (start, end, _, breakMinutes) = await PlannedDayResolver.ResolveAsync(owner._schedules, employeeId, date, ct).ConfigureAwait(false);
                    plan = start is not null && end is not null ? new PlannedDay(start, end, breakMinutes) : PlannedDay.Rest;
                    _plans[(employeeId, date)] = plan;
                }

                candidates.Add((date, plan));
            }

            return candidates;
        }
    }

    /// <summary>Giriş sınırı doğrulaması: tanımsız yön/kaynak ve uzun kaynak referansı reddedilir (Punch.Create referansı sessizce keser).</summary>
    private static Error? Validate(PunchInput input)
    {
        if (!Enum.IsDefined(input.Direction))
        {
            return Error.Validation(AttendanceErrors.PunchDirectionInvalid);
        }

        if (!Enum.IsDefined(input.Source))
        {
            return Error.Validation(AttendanceErrors.PunchSourceInvalid);
        }

        return input.SourceRef is { } sourceRef && sourceRef.Trim().Length > Punch.SourceRefMaxLength
            ? Error.Validation(AttendanceErrors.PunchSourceRefTooLong)
            : null;
    }

    private static PunchDirection ToDirection(PunchDirectionDto direction) => direction switch
    {
        PunchDirectionDto.In => PunchDirection.In,
        PunchDirectionDto.Out => PunchDirection.Out,
        _ => PunchDirection.Unknown,
    };

    private static PunchSource ToSource(PunchSourceDto source) => source switch
    {
        PunchSourceDto.Manual => PunchSource.Manual,
        PunchSourceDto.Import => PunchSource.Import,
        PunchSourceDto.Device => PunchSource.Device,
        PunchSourceDto.SelfService => PunchSource.SelfService,
        PunchSourceDto.Request => PunchSource.Request,
        PunchSourceDto.Terminal => PunchSource.Terminal,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}

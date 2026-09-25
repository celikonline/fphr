using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// <see cref="IPunchRecorder"/> (Z0.1): okutmanın puantaj günü plan penceresinden çözülür (<see cref="WorkDateResolver"/>, gece vardiyası
/// çakışması → Incalculable), K6 kilitli ay (onaylı aylık puantaj) reddedilir / toplu alımda "reddedilen" sayılır, aynı tekillik anahtarı
/// (Tenant, Employee, OccurredAtUtc µs, Direction, Source) hata değil yoksayma sayılır - eşzamanlı aynı okutmanın kaydetme anındaki tekillik
/// ihlali dahil (bir kez yeniden denenir); etkilenen günler okutmalardan yeniden türetilir ve kaydedilir. Başarısız işlemde eklenen okutmalar
/// değişiklik izlemeden çıkarılır.
/// Kiracı: ortam kiracısı (<see cref="ITenantContext"/>) varsa girişin kiracısıyla aynı olmalıdır (değilse <see cref="AttendanceErrors.PunchTenantMismatch"/>);
/// yoksa (entegrasyon olayı) girişin kiracısı için kapsam açılır. Toplu alım tek kiracılıdır. Komut hattı dışından da çağrılabildiği için her
/// işlem kendi değişikliklerini kaydeder.
/// </summary>
public sealed class PunchRecorder(
    IPunchRepository punches, IAttendanceDayRepository days, IPlannedDayResolver planner, IMonthlyTimesheetApprovalRepository approvals,
    IShiftSettingsRepository shiftSettings, ITenantLocalClock clock, IAttendanceUnitOfWork unitOfWork, ITenantContext tenantContext,
    ITenantContextSetter tenantSetter) : IPunchRecorder
{
    private const int MaxSaveAttempts = 2;

    private readonly IPunchRepository _punches = punches;
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
        if (CheckTenant([input.TenantId]) is { } mismatch)
        {
            return mismatch;
        }

        using var scope = OpenTenantScope(input.TenantId);
        var session = await StartAsync(input.TenantId, ct).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            var staged = await session.StageAsync(input, ct).ConfigureAwait(false);
            if (staged.IsFailure || staged.Value.Kind == StageKind.Locked)
            {
                session.Writer.DiscardPending();
                return staged.IsFailure ? staged.Error : Error.Conflict(AttendanceErrors.PeriodLocked);
            }

            if (!session.HasChanges)
            {
                return staged.Value.PunchId;
            }

            var saved = await session.TryCommitAsync(attempt < MaxSaveAttempts, ct).ConfigureAwait(false);
            if (saved is null)
            {
                continue;
            }

            return saved.IsFailure ? saved.Error : staged.Value.PunchId;
        }
    }

    public async Task<Result<PunchBatchResult>> RecordBatchAsync(IReadOnlyList<PunchInput> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0)
        {
            return new PunchBatchResult(0, 0);
        }

        if (CheckTenant(inputs.Select(i => i.TenantId)) is { } mismatch)
        {
            return mismatch;
        }

        var tenantId = inputs[0].TenantId;
        using var scope = OpenTenantScope(tenantId);
        var session = await StartAsync(tenantId, ct).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            var counts = new Dictionary<StageKind, int> { [StageKind.Added] = 0, [StageKind.Duplicate] = 0, [StageKind.Locked] = 0 };
            foreach (var input in inputs)
            {
                var staged = await session.StageAsync(input, ct).ConfigureAwait(false);
                if (staged.IsFailure)
                {
                    session.Writer.DiscardPending();
                    return staged.Error;
                }

                counts[staged.Value.Kind]++;
            }

            var result = new PunchBatchResult(counts[StageKind.Added], counts[StageKind.Duplicate], counts[StageKind.Locked]);
            if (!session.HasChanges)
            {
                return result;
            }

            var saved = await session.TryCommitAsync(attempt < MaxSaveAttempts, ct).ConfigureAwait(false);
            if (saved is null)
            {
                continue;
            }

            return saved.IsFailure ? saved.Error : result;
        }
    }

    public async Task<Result> SoftDeleteAsync(Guid tenantId, Guid punchId, Guid userId, string reason, CancellationToken ct)
    {
        if (CheckTenant([tenantId]) is { } mismatch)
        {
            return mismatch;
        }

        using var scope = OpenTenantScope(tenantId);
        var punch = await _punches.FindByIdAsync(tenantId, punchId, ct).ConfigureAwait(false);
        if (punch is null)
        {
            return Error.NotFound(AttendanceErrors.PunchNotFound);
        }

        var session = await StartAsync(tenantId, ct).ConfigureAwait(false);
        if (await session.Writer.IsLockedAsync(punch.EmployeeId, punch.WorkDate, ct).ConfigureAwait(false))
        {
            return Error.Conflict(AttendanceErrors.PeriodLocked);
        }

        var deleted = session.Writer.SoftDelete(punch, userId, reason);
        if (deleted.IsFailure)
        {
            return deleted;
        }

        session.Touch(punch.EmployeeId, punch.WorkDate, source: null, ambiguous: false);
        return await session.TryCommitAsync(retryOnDuplicate: false, ct).ConfigureAwait(false) ?? Result.Success();
    }

    private Error? CheckTenant(IEnumerable<Guid> tenantIds)
    {
        var distinct = tenantIds.Distinct().ToList();
        var mismatch = distinct.Count != 1 || (tenantContext.IsResolved && tenantContext.TenantId != distinct[0]);
        return mismatch ? Error.Forbidden(AttendanceErrors.PunchTenantMismatch) : null;
    }

    /// <summary>Ortam kiracısı yoksa (entegrasyon olayı, arka plan işi) okutmanın kiracısı için açık kapsam; varsa hiçbir şey (null).</summary>
    private IDisposable? OpenTenantScope(Guid tenantId) => tenantContext.IsResolved ? null : tenantSetter.BeginScope(tenantId);

    private async Task<Session> StartAsync(Guid tenantId, CancellationToken ct)
    {
        var zone = await clock.GetTimeZoneAsync(tenantId, ct).ConfigureAwait(false);
        var now = await clock.NowAsync(tenantId, ct).ConfigureAwait(false);
        var limits = AttendanceDayUpsert.ToShiftLimits(await shiftSettings.FindAsync(ct).ConfigureAwait(false));
        return new Session(this, tenantId, new PunchDayWriter(tenantId, days, _punches, planner, approvals, zone, now.Utc), now.Utc, limits);
    }

    /// <summary>Tek kiracı için bir kayıt işlemi: yeni okutmalar, dokunulan günler ve plan önbelleği.</summary>
    private sealed class Session(PunchRecorder owner, Guid tenantId, PunchDayWriter writer, DateTime nowUtc, ShiftLimits limits)
    {
        private readonly Dictionary<(Guid EmployeeId, DateTime OccurredAtUtc, PunchDirection Direction, PunchSource Source), Guid> _seen = [];
        private readonly Dictionary<(Guid EmployeeId, DateOnly WorkDate), (AttendanceDaySource? Source, bool Ambiguous)> _touched = [];

        public bool HasChanges => _touched.Count > 0;

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
            var resolution = WorkDateResolver.ResolveDetailed(local, await writer.CandidatesAsync(input.EmployeeId, DateOnly.FromDateTime(local), ct).ConfigureAwait(false));
            if (await writer.IsLockedAsync(input.EmployeeId, resolution.Date, ct).ConfigureAwait(false))
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

        /// <summary>
        /// Dokunulan günleri yeniden kurar ve kaydeder. Kaydetme eşzamanlı aynı okutma yüzünden tekillik ihlaliyle düşerse ve
        /// <paramref name="retryOnDuplicate"/> açıksa bu işlemin okutmaları bırakılır ve null döner - çağıran girişleri yeniden sahneler
        /// (artık kayıtlı olan okutma yoksayılır). Dokunulan günler korunur ki yeniden kurulsunlar.
        /// </summary>
        public async Task<Result?> TryCommitAsync(bool retryOnDuplicate, CancellationToken ct)
        {
            var rebuilt = await RebuildAsync(ct).ConfigureAwait(false);
            if (rebuilt.IsFailure)
            {
                writer.DiscardPending();
                return rebuilt;
            }

            try
            {
                await owner._unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result.Success();
            }
            catch (Exception ex) when (retryOnDuplicate && owner._punches.IsDuplicateKeyViolation(ex))
            {
                writer.DiscardPending();
                _seen.Clear();
                return null;
            }
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

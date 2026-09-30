using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Kaydetme ve eşzamanlı yazma çakışması (I0 düzeltme turu 1) - tüm kanallar için tek nokta. Çakışma türleri: günün <c>xmin</c>
/// eşzamanlılık belirteci değişti (başka işlem aynı günü güncelledi - kayıp güncelleme), aynı gün başka işlemde oluşturuldu ya da aynı
/// okutma başka işlemde kaydedildi (tekillik indeksleri). Her durumda dokunulan günler taze okutmalar ve kalıcı gün satırından yeniden
/// türetilir ve <see cref="MaxSaveAttempts"/> kez denenir; tükenirse <see cref="AttendanceErrors.DayConcurrentUpdate"/> (409).
/// </summary>
public sealed partial class PunchDayWriter
{
    public const int MaxSaveAttempts = 3;

    private readonly Dictionary<(Guid EmployeeId, DateOnly Date), RebuildRequest> _rebuilt = [];
    private readonly Dictionary<Guid, Guid> _duplicateOf = [];

    /// <summary>Kaydetmede başka işlem aynısını kaydettiği için bırakılan okutmanın yerine kalıcı okutmanın id'si; değilse kendisi.</summary>
    public Guid SavedPunchId(Guid punchId) => _duplicateOf.TryGetValue(punchId, out var winner) ? winner : punchId;

    /// <summary>Okutma kaydetme anında eşzamanlı aynı okutmaya düştü mü (yoksayılan sayılır).</summary>
    public bool WasDuplicate(Guid punchId) => _duplicateOf.ContainsKey(punchId);

    /// <summary>Yazıcının değişikliklerini kaydeder; eşzamanlı yazma çakışmasında günleri yeniden türetip sınırlı sayıda yeniden dener.</summary>
    public async Task<Result> SaveAsync(IAttendanceUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _pending.Clear();
                _createdDays.Clear();
                _rebuilt.Clear();
                return Result.Success();
            }
            catch (Exception ex) when (IsWriteConflict(ex))
            {
                if (attempt >= MaxSaveAttempts)
                {
                    DiscardPending();
                    return Error.Conflict(AttendanceErrors.DayConcurrentUpdate);
                }

                var prepared = await PrepareRetryAsync(cancellationToken).ConfigureAwait(false);
                if (prepared.IsFailure)
                {
                    DiscardPending();
                    return prepared;
                }
            }
        }
    }

    private bool IsWriteConflict(Exception exception) => days.IsConcurrencyConflict(exception) || punches.IsDuplicateKeyViolation(exception);

    private async Task<Result> PrepareRetryAsync(CancellationToken cancellationToken)
    {
        await DropPunchesSavedElsewhereAsync(cancellationToken).ConfigureAwait(false);

        // Bu yazıcının oluşturduğu günler başka işlemde oluşmuş olabilir: bırakılır, yeniden kurulurken kalıcı satır bulunur.
        foreach (var day in _createdDays.Values)
        {
            days.Discard(day);
        }

        _createdDays.Clear();

        // Güncellenen günlerin kalıcı değerleri (ve xmin) yeniden yüklenir, sonra gün taze okutmalardan yeniden türetilir.
        foreach (var ((employeeId, date), request) in _rebuilt.ToList())
        {
            if (await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false) is { } stored)
            {
                await days.ReloadAsync(stored, cancellationToken).ConfigureAwait(false);
            }

            var rebuilt = await RebuildAsync(employeeId, date, request.Source, request.Limits, request.ForceIncalculable, cancellationToken)
                .ConfigureAwait(false);
            if (rebuilt.IsFailure)
            {
                return rebuilt.Error;
            }
        }

        return Result.Success();
    }

    /// <summary>Başka işlemin aynı tekillik anahtarıyla kaydettiği okutmalar: bizimki bırakılır, kalıcı olanın id'si hatırlanır.</summary>
    private async Task DropPunchesSavedElsewhereAsync(CancellationToken cancellationToken)
    {
        foreach (var group in _pending.GroupBy(p => (p.EmployeeId, p.WorkDate)).ToList())
        {
            var stored = await punches.ListForDayAsync(tenantId, group.Key.EmployeeId, group.Key.WorkDate, cancellationToken).ConfigureAwait(false);
            foreach (var punch in group)
            {
                var winner = stored.FirstOrDefault(p =>
                    p.Id != punch.Id && p.OccurredAtUtc == punch.OccurredAtUtc && p.Direction == punch.Direction && p.Source == punch.Source);
                if (winner is null)
                {
                    continue;
                }

                punches.Discard(punch);
                _pending.Remove(punch);
                _duplicateOf[punch.Id] = winner.Id;
            }
        }
    }

    private sealed record RebuildRequest(AttendanceDaySource? Source, ShiftLimits? Limits, bool ForceIncalculable);
}

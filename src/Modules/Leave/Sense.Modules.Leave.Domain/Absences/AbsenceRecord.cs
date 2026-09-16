using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.Absences;

public enum AbsenceKind
{
    Unexcused = 0,
    Excused = 1,
    Late = 2,
}

/// <summary>
/// İK'nın elle girdiği devamsızlık kaydı - Attendance/PDKS'in (Faz 6) <c>AttendanceDay</c>'inden ayrı, minimal bir
/// kayıt (11.3.5 kapsamı dışı, tur 1 kararı). <see cref="LeaveLedgerEntry"/>'nin Usage/Reversal ikilisiyle aynı
/// desen izlenir: append-only, silme yok - bir düzeltme <see cref="ReversesId"/> ile orijinali işaret eden yeni
/// bir kayıttır. İzin bakiyesine hiç dokunmaz (bkz. docs/implementation/leave-kesirli-gun.md son bölüm).
/// </summary>
public sealed class AbsenceRecord : TenantAggregateRoot<Guid>, IAuditLogged
{
    private AbsenceRecord()
    {
    }

    private AbsenceRecord(
        Guid tenantId, Guid employeeId, DateOnly date, AbsenceKind kind, decimal? hours, string? note, Guid recordedByUserId, Guid? reversesId)
        : base(Guid.CreateVersion7(), tenantId)
    {
        EmployeeId = employeeId;
        Date = date;
        Kind = kind;
        Hours = hours;
        Note = note;
        RecordedByUserId = recordedByUserId;
        ReversesId = reversesId;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public AbsenceKind Kind { get; private set; }

    /// <summary>Esas olarak <see cref="AbsenceKind.Late"/> için ("kaç saat geç kaldı"); diğer türlerde boş kalması beklenir.</summary>
    public decimal? Hours { get; private set; }

    public string? Note { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    /// <summary>Yalnız bir ters kayıtta dolu: offset ettiği orijinal kaydın Id'si.</summary>
    public Guid? ReversesId { get; private set; }

    public bool IsReversal => ReversesId is not null;

    public static Result<AbsenceRecord> Record(
        Guid tenantId, Guid employeeId, DateOnly date, AbsenceKind kind, decimal? hours, string? note, Guid recordedByUserId)
    {
        var validated = ValidateShape(kind, hours, note);
        return validated.IsFailure
            ? validated.Error
            : new AbsenceRecord(tenantId, employeeId, date, kind, hours, Trim(note), recordedByUserId, null);
    }

    /// <summary>
    /// Ters kayıt: aynı çalışan/tarih/tür, orijinalin Id'sini <see cref="ReversesId"/>'de taşır, <see cref="Hours"/> taşımaz.
    /// <paramref name="alreadyReversed"/>: çağıranın (repository sorgusuyla) önceden doğrulaması gereken kural - bir
    /// orijinal kayıt yalnız bir kez ters çevrilebilir; bir ters kaydın kendisi tekrar ters çevrilemez (<see cref="IsReversal"/>).
    /// </summary>
    public static Result<AbsenceRecord> Reverse(AbsenceRecord original, string reason, Guid recordedByUserId, bool alreadyReversed)
    {
        if (original.IsReversal)
        {
            return Error.Validation(LeaveDomainErrors.AbsenceReversalNotReversible);
        }

        if (alreadyReversed)
        {
            return Error.Conflict(LeaveDomainErrors.AbsenceAlreadyReversed);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > LeaveLimits.AbsenceNoteMaxLength)
        {
            return Error.Validation(LeaveDomainErrors.AbsenceReversalReasonRequired);
        }

        return new AbsenceRecord(original.TenantId, original.EmployeeId, original.Date, original.Kind, null, reason.Trim(), recordedByUserId, original.Id);
    }

    private static Result ValidateShape(AbsenceKind kind, decimal? hours, string? note)
    {
        if (note is { Length: > LeaveLimits.AbsenceNoteMaxLength })
        {
            return Error.Validation(LeaveDomainErrors.AbsenceNoteTooLong);
        }

        if (hours is { } h && (h <= 0m || h > 24m))
        {
            return Error.Validation(LeaveDomainErrors.AbsenceHoursInvalid);
        }

        if (kind != AbsenceKind.Late && hours is not null)
        {
            return Error.Validation(LeaveDomainErrors.AbsenceHoursOnlyForLate);
        }

        return Result.Success();
    }

    private static string? Trim(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

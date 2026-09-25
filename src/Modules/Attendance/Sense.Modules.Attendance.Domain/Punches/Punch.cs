using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Punches;

/// <summary>
/// Ham okutma (zaman-devam Z0.1, spec §4): bir çalışanın tek bir giriş/çıkış anı. <see cref="AttendanceDays.AttendanceDay"/>
/// artık bu satırlardan türetilir (ilk <c>In</c> / son <c>Out</c>, ara <c>Out→In</c> boşlukları gerçek mola);
/// <see cref="WorkDate"/> okutmanın hangi puantaj gününe ait olduğunu tutar (gece vardiyasında bir önceki takvim günü
/// olabilir, bkz. spec §5.1). Fiziksel silinmez: <see cref="SoftDelete"/> gerekçe ister ve kim/ne zaman bilgisini saklar
/// (denetim). Tekillik (Tenant, Employee, OccurredAtUtc, Direction, Source) silinmemiş satırlarda veritabanında korunur.
/// </summary>
public sealed class Punch : TenantAggregateRoot<Guid>, IAuditLogged
{
    /// <summary>Cihaz/sunucu saat kayması payı: bu kadar dakikaya kadar ileri tarihli okutma kabul edilir.</summary>
    public const int FutureToleranceMinutes = 5;

    public const int SourceRefMaxLength = 200;
    public const int ReasonMaxLength = 1000;

    /// <summary>WorkDate, okutmanın UTC takvim gününden en çok bu kadar gün uzak olabilir (saat dilimi + gece vardiyası).</summary>
    private const int MaxWorkDateDistanceDays = 1;

    private const double MaxLatitude = 90;
    private const double MaxLongitude = 180;

    private Punch()
    {
    }

    private Punch(
        Guid id, Guid tenantId, Guid employeeId, DateTime occurredAtUtc, DateOnly workDate, PunchDirection direction, PunchSource source,
        string? sourceRef, Guid? terminalId, Guid? locationId, double? latitude, double? longitude, bool isManual, Guid? createdByUserId, string? reason)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        OccurredAtUtc = occurredAtUtc;
        WorkDate = workDate;
        Direction = direction;
        Source = source;
        SourceRef = sourceRef;
        TerminalId = terminalId;
        LocationId = locationId;
        Latitude = latitude;
        Longitude = longitude;
        IsManual = isManual;
        CreatedByUserId = createdByUserId;
        Reason = reason;
    }

    public Guid EmployeeId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Okutmanın ait olduğu puantaj günü (spec §5.1 plan penceresi kuralı; gece vardiyasında vardiyanın başladığı gün).</summary>
    public DateOnly WorkDate { get; private set; }

    public PunchDirection Direction { get; private set; }

    public PunchSource Source { get; private set; }

    /// <summary>Kaynağa özgü dış kimlik: SelfCheckIn id, PunchRequest id, içe aktarma işi id vb.</summary>
    public string? SourceRef { get; private set; }

    public Guid? TerminalId { get; private set; }

    public Guid? LocationId { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public bool IsManual { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public Guid? DeletedByUserId { get; private set; }

    public string? DeleteReason { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    /// <summary>
    /// Yeni okutma. <paramref name="nowUtc"/> verilmezse sistem saati kullanılır (ileri tarih kontrolü:
    /// <see cref="FutureToleranceMinutes"/>). Kind'ı belirtilmemiş zaman UTC kabul edilir.
    /// </summary>
    public static Result<Punch> Create(
        Guid tenantId, Guid employeeId, DateTime occurredAtUtc, DateOnly workDate, PunchDirection direction, PunchSource source,
        string? sourceRef, Guid? terminalId, Guid? locationId, double? lat, double? lon, bool isManual, Guid? createdByUserId, string? reason,
        DateTime? nowUtc = null)
    {
        var occurred = NormalizeOccurredAt(occurredAtUtc);
        var check = Validate(employeeId, occurred, workDate, lat, lon, reason, AsUtc(nowUtc ?? DateTime.UtcNow));
        if (check.IsFailure)
        {
            return check.Error;
        }

        return new Punch(
            Guid.CreateVersion7(), tenantId, employeeId, occurred, workDate, direction, source, Normalize(sourceRef, SourceRefMaxLength), terminalId, locationId,
            lat, lon, isManual, createdByUserId, Normalize(reason, ReasonMaxLength));
    }

    /// <summary>Gerekçeli yumuşak silme (denetim izi kalır); silinmiş okutma gün hesabına girmez.</summary>
    public Result SoftDelete(Guid userId, string reason, DateTime nowUtc)
    {
        if (IsDeleted)
        {
            return Error.Conflict(AttendanceDomainErrors.PunchAlreadyDeleted);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation(AttendanceDomainErrors.PunchDeleteReasonRequired);
        }

        if (reason.Trim().Length > ReasonMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.PunchReasonTooLong);
        }

        DeletedAtUtc = AsUtc(nowUtc);
        DeletedByUserId = userId;
        DeleteReason = reason.Trim();
        return Result.Success();
    }

    private static Result Validate(Guid employeeId, DateTime occurredAtUtc, DateOnly workDate, double? lat, double? lon, string? reason, DateTime nowUtc)
    {
        if (employeeId == Guid.Empty)
        {
            return Error.Validation(AttendanceDomainErrors.PunchEmployeeRequired);
        }

        if (occurredAtUtc > nowUtc.AddMinutes(FutureToleranceMinutes))
        {
            return Error.Validation(AttendanceDomainErrors.PunchInFuture);
        }

        var distance = Math.Abs(workDate.DayNumber - DateOnly.FromDateTime(occurredAtUtc).DayNumber);
        if (distance > MaxWorkDateDistanceDays)
        {
            return Error.Validation(AttendanceDomainErrors.PunchWorkDateInvalid);
        }

        if ((lat is null) != (lon is null) || lat is < -MaxLatitude or > MaxLatitude || lon is < -MaxLongitude or > MaxLongitude)
        {
            return Error.Validation(AttendanceDomainErrors.PunchCoordinatesInvalid);
        }

        if (reason is not null && reason.Trim().Length > ReasonMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.PunchReasonTooLong);
        }

        return Result.Success();
    }

    /// <summary>
    /// Okutma anını UTC'ye çevirip mikrosaniyeye kırpar: .NET 100 ns, PostgreSQL 1 µs saklar; kırpılmazsa aynı okutmanın tekrarı
    /// (terminal yeniden denemesi) bellekte farklı, veritabanında aynı görünür ve tekillik/idempotans kontrolü sapar.
    /// </summary>
    public static DateTime NormalizeOccurredAt(DateTime value)
    {
        var utc = AsUtc(value);
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

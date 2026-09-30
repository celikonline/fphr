using System.Text.RegularExpressions;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Shifts;

/// <summary>Vardiya tipi (K1): sabit saat, sabit süreli esnek giriş penceresi, ya da okutmaya en yakın tanımın seçildiği dinamik.</summary>
public enum ShiftType
{
    Fixed = 0,
    Flexible = 1,
    Dynamic = 2,
}

/// <summary>Vardiya molası (EF-owned, JSON). <see cref="IsFlexible"/> molanın sabit başlangıcı yoktur; sabit molada <see cref="StartMinute"/> zorunludur.</summary>
public sealed class ShiftBreak
{
    public ShiftBreak()
    {
    }

    private ShiftBreak(int? startMinute, int durationMinutes, bool isFlexible)
    {
        StartMinute = startMinute;
        DurationMinutes = durationMinutes;
        IsFlexible = isFlexible;
    }

    /// <summary>Gün başından dakika; esnek molada null.</summary>
    public int? StartMinute { get; private set; }

    public int DurationMinutes { get; private set; }

    public bool IsFlexible { get; private set; }

    public static ShiftBreak Fixed(int startMinute, int durationMinutes) => new(startMinute, durationMinutes, false);

    public static ShiftBreak Flexible(int durationMinutes) => new(null, durationMinutes, true);

    internal bool IsValid() =>
        DurationMinutes is > 0 and < AttendanceLimits.MaxMinutesPerDay
        && (IsFlexible ? StartMinute is null : StartMinute is >= 0 and < AttendanceLimits.MaxMinutesPerDay);
}

/// <summary><see cref="ShiftDefinition"/> oluşturma/güncelleme girdisi (spec §4 alanları; dakikalar gün başından).</summary>
public sealed record ShiftDefinitionValues(
    string Code,
    string Name,
    string ColorHex,
    ShiftType Type,
    int StartMinute,
    int EndMinute,
    bool StartsPreviousDay,
    int? DurationMinutes,
    int? FlexStartEarliestMinute,
    int? FlexStartLatestMinute,
    IReadOnlyList<ShiftBreak> Breaks,
    int LateToleranceMinutes,
    int EarlyLeaveToleranceMinutes,
    int? EarlyArrivalOvertimeMinMinutes,
    int? EarlyArrivalOvertimeMaxMinutes,
    int? LateDepartureOvertimeMinMinutes,
    int? LateDepartureOvertimeMaxMinutes,
    int BonusMinutes);

/// <summary>
/// Kural setli vardiya tanımı (zaman-devam Z0.2, spec §4): kod tenant içinde tekildir; <see cref="ShiftAssignment.ShiftDefinitionId"/>
/// ile bağlanan günlerde geç kalma/erken çıkma toleransı, fazla mesai eşikleri, mola listesi ve bonus süre buradan
/// gelir (hesap kuralı spec §5.2). <see cref="EndMinute"/> ≤ <see cref="StartMinute"/> gece vardiyası demektir.
/// </summary>
public sealed partial class ShiftDefinition : TenantAggregateRoot<Guid>, IAuditLogged
{
    public const int CodeMaxLength = 50;
    public const int ColorHexLength = 7;

    private readonly List<ShiftBreak> _breaks = [];

    private ShiftDefinition()
    {
    }

    private ShiftDefinition(Guid id, Guid tenantId)
        : base(id, tenantId)
    {
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>#RRGGBB (büyük harf).</summary>
    public string ColorHex { get; private set; } = string.Empty;

    public ShiftType Type { get; private set; }

    public int StartMinute { get; private set; }

    public int EndMinute { get; private set; }

    /// <summary>Vardiya bir önceki takvim gününde başlar (ör. pazartesi vardiyası pazar 22:00'de).</summary>
    public bool StartsPreviousDay { get; private set; }

    /// <summary>Esnek vardiyada sabit vardiya süresi (K1); diğer tiplerde null.</summary>
    public int? DurationMinutes { get; private set; }

    public int? FlexStartEarliestMinute { get; private set; }

    public int? FlexStartLatestMinute { get; private set; }

    public IReadOnlyList<ShiftBreak> Breaks => _breaks;

    public int LateToleranceMinutes { get; private set; }

    public int EarlyLeaveToleranceMinutes { get; private set; }

    public int? EarlyArrivalOvertimeMinMinutes { get; private set; }

    public int? EarlyArrivalOvertimeMaxMinutes { get; private set; }

    public int? LateDepartureOvertimeMinMinutes { get; private set; }

    public int? LateDepartureOvertimeMaxMinutes { get; private set; }

    public int BonusMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<ShiftDefinition> Create(Guid tenantId, ShiftDefinitionValues values)
    {
        var definition = new ShiftDefinition(Guid.CreateVersion7(), tenantId);
        var applied = definition.Update(values);
        return applied.IsFailure ? applied.Error : definition;
    }

    /// <summary>Tüm alanları birlikte değiştirir; geçmiş günlere uygulanması (yeniden hesap) uygulama katmanının işidir.</summary>
    public Result Update(ShiftDefinitionValues values)
    {
        var check = Validate(values);
        if (check.IsFailure)
        {
            return check;
        }

        Code = values.Code.Trim();
        Name = values.Name.Trim();
        ColorHex = values.ColorHex.Trim().ToUpperInvariant();
        Type = values.Type;
        StartMinute = values.StartMinute;
        EndMinute = values.EndMinute;
        StartsPreviousDay = values.StartsPreviousDay;
        DurationMinutes = values.DurationMinutes;
        FlexStartEarliestMinute = values.FlexStartEarliestMinute;
        FlexStartLatestMinute = values.FlexStartLatestMinute;
        _breaks.Clear();
        _breaks.AddRange(values.Breaks);
        LateToleranceMinutes = values.LateToleranceMinutes;
        EarlyLeaveToleranceMinutes = values.EarlyLeaveToleranceMinutes;
        EarlyArrivalOvertimeMinMinutes = values.EarlyArrivalOvertimeMinMinutes;
        EarlyArrivalOvertimeMaxMinutes = values.EarlyArrivalOvertimeMaxMinutes;
        LateDepartureOvertimeMinMinutes = values.LateDepartureOvertimeMinMinutes;
        LateDepartureOvertimeMaxMinutes = values.LateDepartureOvertimeMaxMinutes;
        BonusMinutes = values.BonusMinutes;
        return Result.Success();
    }

    /// <summary>
    /// "Kaydet &amp; Geçmişe Uygula": kaydedilen kuralların <paramref name="appliedFrom"/>'dan itibaren geçmiş günlere uygulandığını
    /// bildirir (<see cref="ShiftDefinitionChanged"/>). Yeniden hesaplamayı uygulama katmanı kuyruğa alır.
    /// </summary>
    public void MarkAppliedToPast(DateOnly appliedFrom) => Raise(new ShiftDefinitionChanged(Id, appliedFrom, TenantId));

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static Result Validate(ShiftDefinitionValues v)
    {
        if (string.IsNullOrWhiteSpace(v.Code) || v.Code.Trim().Length > CodeMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionCodeRequired);
        }

        if (string.IsNullOrWhiteSpace(v.Name) || v.Name.Trim().Length > AttendanceLimits.NameMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionNameRequired);
        }

        if (v.ColorHex is null || !ColorHexPattern().IsMatch(v.ColorHex.Trim()))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionColorInvalid);
        }

        if (!Enum.IsDefined(v.Type) || !IsMinuteOfDay(v.StartMinute) || !IsMinuteOfDay(v.EndMinute)
            || (v.Type != ShiftType.Flexible && v.StartMinute == v.EndMinute))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionTimeRangeInvalid);
        }

        if (!HasValidFlexibleWindow(v))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionFlexibleWindowInvalid);
        }

        if (!IsDuration(v.LateToleranceMinutes) || !IsDuration(v.EarlyLeaveToleranceMinutes) || !IsDuration(v.BonusMinutes))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionToleranceInvalid);
        }

        if (!IsOvertimeRange(v.EarlyArrivalOvertimeMinMinutes, v.EarlyArrivalOvertimeMaxMinutes)
            || !IsOvertimeRange(v.LateDepartureOvertimeMinMinutes, v.LateDepartureOvertimeMaxMinutes))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionOvertimeRangeInvalid);
        }

        if (v.Breaks is null || v.Breaks.Any(b => b is null || !b.IsValid()))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDefinitionBreakInvalid);
        }

        return Result.Success();
    }

    /// <summary>Esnek: süre + giriş penceresi zorunlu (en erken ≤ en geç). Diğer tiplerde bu alanlar boş olmalı.</summary>
    private static bool HasValidFlexibleWindow(ShiftDefinitionValues v)
    {
        if (v.Type != ShiftType.Flexible)
        {
            return v.DurationMinutes is null && v.FlexStartEarliestMinute is null && v.FlexStartLatestMinute is null;
        }

        return v.DurationMinutes is > 0 and <= AttendanceLimits.MaxMinutesPerDay
            && v.FlexStartEarliestMinute is { } earliest && IsMinuteOfDay(earliest)
            && v.FlexStartLatestMinute is { } latest && IsMinuteOfDay(latest)
            && earliest <= latest;
    }

    private static bool IsMinuteOfDay(int minute) => minute is >= 0 and < AttendanceLimits.MaxMinutesPerDay;

    private static bool IsDuration(int minutes) => minutes is >= 0 and <= AttendanceLimits.MaxMinutesPerDay;

    private static bool IsOvertimeRange(int? min, int? max) =>
        (min is null || IsDuration(min.Value)) && (max is null || IsDuration(max.Value)) && (min is null || max is null || min <= max);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorHexPattern();
}

/// <summary>
/// Bir vardiya tanımının kuralları <see cref="AppliedFrom"/>'dan itibaren geçmişe uygulandı (Z0.2 "Kaydet &amp; Geçmişe Uygula").
/// Bilgilendirme olayıdır: etkilenen aralığın yeniden hesaplaması komutta zaten kuyruğa alınır, dinleyiciler tekrar kuyruğa almamalı.
/// </summary>
public sealed record ShiftDefinitionChanged(Guid DefinitionId, DateOnly AppliedFrom, Guid TenantId) : DomainEvent;

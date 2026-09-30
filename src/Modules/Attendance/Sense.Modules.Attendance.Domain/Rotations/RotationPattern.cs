using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Rotations;

/// <summary>Hazır rotasyon kalıpları (Z3.3): 2×12, 3×8, 4×6 ya da serbest.</summary>
public enum RotationPreset
{
    Custom = 0,
    TwoByTwelve = 1,
    ThreeByEight = 2,
    FourBySix = 3,
}

/// <summary>Döngünün bir günü (EF-owned, JSON): döngü başından gün ofseti → vardiya tanımı ve gün türü.</summary>
public sealed class RotationStep
{
    public RotationStep()
    {
    }

    private RotationStep(int dayOffset, Guid? shiftDefinitionId, ShiftDayType dayType)
    {
        DayOffset = dayOffset;
        ShiftDefinitionId = shiftDefinitionId;
        DayType = dayType;
    }

    public int DayOffset { get; private set; }

    public Guid? ShiftDefinitionId { get; private set; }

    public ShiftDayType DayType { get; private set; }

    public static RotationStep Of(int dayOffset, Guid? shiftDefinitionId, ShiftDayType dayType) => new(dayOffset, shiftDefinitionId, dayType);
}

/// <summary>
/// Rotasyon kalıbı (zaman-devam Z3.3, spec §4): <see cref="CycleLengthDays"/> günlük döngü; her gün ofseti en çok bir
/// adım taşır (adımı olmayan ofset plansız gündür). Çalışana <see cref="RotationAssignment"/> ile bağlanır; atamadan
/// <see cref="ShiftAssignment"/> üretimi uygulama katmanındadır. Kod tenant içinde tekildir (uygulama katmanı denetler).
/// </summary>
public sealed class RotationPattern : TenantAggregateRoot<Guid>, IAuditLogged
{
    public const int CodeMaxLength = 50;

    /// <summary>En uzun döngü: 8 hafta.</summary>
    public const int MaxCycleLengthDays = 56;

    private readonly List<RotationStep> _steps = [];

    private RotationPattern()
    {
    }

    private RotationPattern(Guid id, Guid tenantId)
        : base(id, tenantId) => IsActive = true;

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public RotationPreset Preset { get; private set; }

    public int CycleLengthDays { get; private set; }

    public IReadOnlyList<RotationStep> Steps => _steps;

    public bool IsActive { get; private set; }

    public static Result<RotationPattern> Create(
        Guid tenantId, string code, string name, RotationPreset preset, int cycleLengthDays, IReadOnlyList<RotationStep> steps)
    {
        var pattern = new RotationPattern(Guid.CreateVersion7(), tenantId);
        var applied = pattern.Update(code, name, preset, cycleLengthDays, steps);
        return applied.IsFailure ? applied.Error : pattern;
    }

    public Result Update(string code, string name, RotationPreset preset, int cycleLengthDays, IReadOnlyList<RotationStep> steps)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > CodeMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.RotationPatternCodeRequired);
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > AttendanceLimits.NameMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.RotationPatternNameRequired);
        }

        if (!Enum.IsDefined(preset) || cycleLengthDays is < 1 or > MaxCycleLengthDays)
        {
            return Error.Validation(AttendanceDomainErrors.RotationCycleInvalid);
        }

        if (steps is null || !AreValid(steps, cycleLengthDays))
        {
            return Error.Validation(AttendanceDomainErrors.RotationStepInvalid);
        }

        Code = code.Trim();
        Name = name.Trim();
        Preset = preset;
        CycleLengthDays = cycleLengthDays;
        _steps.Clear();
        _steps.AddRange(steps.OrderBy(s => s.DayOffset));
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static bool AreValid(IReadOnlyList<RotationStep> steps, int cycleLengthDays) =>
        steps.Count > 0
        && steps.All(s => s is not null && s.DayOffset >= 0 && s.DayOffset < cycleLengthDays && Enum.IsDefined(s.DayType))
        && steps.Select(s => s.DayOffset).Distinct().Count() == steps.Count;
}

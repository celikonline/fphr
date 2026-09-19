using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;

namespace Sense.Modules.OccupationalSafety.Domain.RiskAssessments;

/// <summary>
/// Static "tehlike sınıfı -> azami risk değerlendirmesi yenileme süresi" table ("Risk Değerlendirmesi
/// Yönetmeliği" md.12: Az tehlikeli işyerlerinde en geç 6 yılda, tehlikelide en geç 4 yılda, çok tehlikelide
/// en geç 2 yılda bir yenilenir). MVP: static table per the module's handoff plan §6 - does NOT go through
/// <c>ILegislationParameterReader</c> (that port is for payroll/leave legislation parameters with their own
/// versioning UI; wiring a single-purpose static table into it would be scope creep for this phase).
/// </summary>
public static class RiskAssessmentRenewalPeriods
{
    private const int LowHazardMaxMonths = 72;
    private const int MediumHazardMaxMonths = 48;
    private const int HighHazardMaxMonths = 24;

    /// <summary>The legal maximum renewal period (in months) for the given hazard class.</summary>
    public static int MonthsFor(HazardClass hazardClass) => hazardClass switch
    {
        HazardClass.Low => LowHazardMaxMonths,
        HazardClass.Medium => MediumHazardMaxMonths,
        HazardClass.High => HighHazardMaxMonths,
        _ => throw new ArgumentOutOfRangeException(nameof(hazardClass), hazardClass, message: null),
    };
}

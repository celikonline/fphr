namespace Sense.Modules.OccupationalSafety.Contracts;

/// <summary>Workplace hazard class (İşyeri Tehlike Sınıfları Tebliği) as a Contracts-only shape of the OHS domain enum.</summary>
public enum OhsHazardLevel
{
    /// <summary>Az tehlikeli.</summary>
    Low = 0,

    /// <summary>Tehlikeli.</summary>
    Medium = 1,

    /// <summary>Çok tehlikeli.</summary>
    High = 2,
}

/// <summary>
/// Cross-module read port (senseik-015 Faz 5): the hazard class of the workplace an employee works at, on a date.
/// The chain is employee -> org unit -> effective location (Organization) -> classification in effect (OHS).
/// Null when any link is missing - callers treat that as "no OHS signal", never as an error.
/// </summary>
public interface IOhsProfileLookup
{
    Task<OhsHazardLevel?> GetEmployeeHazardLevelAsync(Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default);
}

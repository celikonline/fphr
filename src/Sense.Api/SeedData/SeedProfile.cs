namespace Sense.Api.SeedData;

/// <summary>Available seed data profiles for development and testing.</summary>
public enum SeedProfile
{
    /// <summary>Minimal profile: 1 tenant, 1 user, 1 unit.</summary>
    Minimal = 0,

    /// <summary>Demo profile: 1 tenant, 50 employees, 5 units, 17 roles, full permission matrix.</summary>
    Demo = 1,
}

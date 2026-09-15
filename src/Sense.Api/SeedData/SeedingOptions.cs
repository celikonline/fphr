namespace Sense.Api.SeedData;

/// <summary>Configuration options for seed data.</summary>
public class SeedingOptions
{
    public const string Section = "Seeding";

    /// <summary>Whether to enable automatic seeding on application startup.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Which seed profile to use (Minimal or Demo).</summary>
    public SeedProfile Profile { get; set; } = SeedProfile.Minimal;
}

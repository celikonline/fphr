namespace Sense.Modules.Identity.Application;

/// <summary>
/// Süper admin (platform) hesabı: yapılandırmadan (env: <c>Platform__SuperAdmin__Email</c> vb.) API açılışında idempotent
/// oluşturulur. E-posta boşsa platform bootstrap'i atlanır. Parola yalnız yapılandırmada durur, asla loglanmaz.
/// </summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform:SuperAdmin";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string FirstName { get; set; } = "Süper";

    public string LastName { get; set; } = "Admin";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

namespace Sense.Modules.Identity.Domain.Tenants;

/// <summary>
/// Platform kiracısı: süper admin (User.IsPlatformStaff) hesabının yaşadığı özel kiracı. Kullanıcı kaydı kiracıya bağlı
/// olduğu ve izin kontrolü kiracısız çalışmadığı için platform personeli bu kiracıda tutulur; müşteri kiracılarından
/// slug'ı ile ayrılır (bkz. ImpersonationCommands: platform kullanıcısı kendi kiracısında doğrulanır).
/// </summary>
public static class PlatformTenant
{
    public const string Slug = "platform";

    public const string Name = "Platform";

    public static bool IsPlatform(string? slug) => string.Equals(slug, Slug, StringComparison.OrdinalIgnoreCase);
}

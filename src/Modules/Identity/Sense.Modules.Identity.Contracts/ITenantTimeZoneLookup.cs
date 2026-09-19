namespace Sense.Modules.Identity.Contracts;

/// <summary>
/// Diğer modüllerin (ör. Attendance self-servis check-in, S-31) kiracının IANA saat dilimi kimliğini
/// (ör. <c>Europe/Istanbul</c>) Identity'nin iç katmanlarına bağımlı olmadan çözmesi. Kiracı bulunamazsa veya
/// saat dilimi tanınmazsa <c>null</c> döner, istisna fırlatmaz; çağıran UTC'ye düşer.
/// </summary>
public interface ITenantTimeZoneLookup
{
    Task<string?> FindTimeZoneIdAsync(Guid tenantId, CancellationToken cancellationToken);
}

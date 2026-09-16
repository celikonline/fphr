namespace Sense.Modules.Identity.Contracts;

/// <summary>Diğer modüllerin (ör. Faz 9 kariyer sayfası) kiracı slug'ından kiracı kimliğini Identity'nin iç
/// katmanlarına bağımlı olmadan çözmesi. Bulunamayan veya pasif (kapalı) kiracı için <c>null</c> döner, istisna
/// fırlatmaz.</summary>
public interface ITenantSlugLookup
{
    Task<Guid?> FindTenantIdAsync(string slug, CancellationToken cancellationToken);
}

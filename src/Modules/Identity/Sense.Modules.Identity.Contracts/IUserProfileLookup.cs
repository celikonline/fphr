namespace Sense.Modules.Identity.Contracts;

/// <summary>Bildirim alıcısı için kullanıcı özeti: giriş e-postası, görünen ad, tercih edilen dil (boşsa null) ve aktif mi.</summary>
public sealed record UserProfileInfo(Guid UserId, Guid? EmployeeId, string Email, string DisplayName, string? PreferredLanguage, bool IsActive);

/// <summary>Diğer modüllerin (ör. Notifications) kullanıcı iletişim/dil bilgisini Identity'nin iç katmanlarına bağımlı olmadan okuması.</summary>
public interface IUserProfileLookup
{
    /// <summary>Bulunamayan kullanıcı kimlikleri sözlükte yer almaz.</summary>
    Task<IReadOnlyDictionary<Guid, UserProfileInfo>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

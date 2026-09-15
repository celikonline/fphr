namespace Sense.Modules.Identity.Contracts;

/// <summary>Çalışan kartı/listesi için kullanıcı hesabı durumu; kullanıcı yoksa <see cref="None"/>.</summary>
public enum UserAccountStatus
{
    None,
    Invited,
    Active,
    Locked,
    Disabled,
}

/// <summary>Kullanıcı hesabı özeti. AccessType: "Owner" | "Admin" | "Employee" | null (Owner &gt; Admin &gt; Employee önceliği).</summary>
public sealed record UserAccountInfo(Guid UserId, UserAccountStatus Status, string? AccessType, DateTime? LastInvitationSentAt);

/// <summary>Diğer modüllerin (ör. Employee) kullanıcı hesabı durumunu Identity'nin iç katmanlarına bağımlı olmadan okuması.</summary>
public interface IUserAccountLookup
{
    /// <summary>Bulunamayan kullanıcı kimlikleri sözlükte yer almaz.</summary>
    Task<IReadOnlyDictionary<Guid, UserAccountInfo>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

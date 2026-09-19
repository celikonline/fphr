using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Application;

public sealed record TenantDto(Guid Id, string Slug, string Name, string? TaxNumber, string Status, DateTime? TrialEndsAt, string Locale, string TimeZone);

public sealed record UserListItemDto(
    Guid Id, string Email, string FirstName, string LastName, string? Phone, UserStatus Status, Guid? EmployeeId,
    bool MfaEnabled, DateTime? LastLoginAt, IReadOnlyList<RoleRefDto> Roles, DateTime CreatedAt);

public sealed record UserDetailDto(
    Guid Id, string Email, bool EmailConfirmed, string FirstName, string LastName, string? Phone, UserStatus Status, Guid? EmployeeId,
    bool MfaEnabled, DateTime? LastLoginAt, DateTime? LockoutEndUtc, string PreferredLanguage,
    IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<PermissionOverrideDto> PermissionOverrides, DateTime CreatedAt, DateTime? ModifiedDate);

/// <summary>Sistem rollerinde Name null döner; API katmanı roles.{code}.name anahtarından yerelleştirir.</summary>
public sealed record RoleRefDto(Guid Id, string Code, string? Name);

public sealed record PermissionOverrideDto(string Permission, bool Allow);

public sealed record RoleDto(Guid Id, string Code, string? Name, string? Description, bool IsSystem, bool IsLocked, int UserCount, IReadOnlyList<RolePermissionDto> Permissions);

public sealed record RolePermissionDto(string Permission, DataScope Scope, IReadOnlyList<Guid>? ScopeIds);

/// <summary>DisplayName/Group kaynak anahtarıdır; API katmanı yerelleştirir.</summary>
public sealed record PermissionDto(string Key, string Module, string DisplayName, string Group, string? Description);

public sealed record PermissionGroupDto(string Module, string Group, IReadOnlyList<PermissionDto> Permissions);

public sealed record InvitationDto(Guid Id, Guid UserId, string Email, string Status, DateTime ExpiresAt, DateTime? AcceptedAt, int SendCount, DateTime CreatedAt);

public sealed record InvitationPreviewDto(string Email, string FirstName, string LastName, string TenantName, string TenantSlug, DateTime ExpiresAt, bool IsUsable);

public sealed record AuthTokensDto(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt)
{
    public string TokenType => AuthTokenTypes.Bearer;
}

public static class AuthTokenTypes
{
    public const string Bearer = "Bearer";
}

public sealed record TenantChoiceDto(Guid TenantId, string Slug, string Name);

public static class LoginStatuses
{
    public const string Ok = "ok";
    public const string SelectTenant = "select_tenant";
    public const string MfaRequired = "mfa_required";
}

/// <summary>Giriş sonucu: token'lar veya (birden fazla kiracı varsa) kiracı seçimi ya da MFA gereksinimi.</summary>
public sealed record LoginResultDto(string Status, AuthTokensDto? Tokens, IReadOnlyList<TenantChoiceDto>? Tenants, string? MfaToken)
{
    public static LoginResultDto Success(AuthTokensDto tokens) => new(LoginStatuses.Ok, tokens, null, null);

    public static LoginResultDto SelectTenant(IReadOnlyList<TenantChoiceDto> tenants) => new(LoginStatuses.SelectTenant, null, tenants, null);

    public static LoginResultDto MfaRequired(string mfaToken) => new(LoginStatuses.MfaRequired, null, null, mfaToken);
}

/// <summary>MFA kaydı başlatma yanıtı: secret (gruplanmış metin olarak gösterilebilir) ve authenticator uygulaması için otpauth:// URI'si.</summary>
public sealed record MfaEnrollmentDto(string Secret, string OtpAuthUri);

/// <summary>Güvenlik ekranı MFA durumu (S-12). <paramref name="PendingEnrollment"/>: secret yazılmış ama kod henüz doğrulanmamış
/// (<see cref="Sense.Modules.Identity.Domain.Users.User.TotpSecretEncrypted"/> dolu, <see cref="Sense.Modules.Identity.Domain.Users.User.MfaEnabled"/> false).</summary>
public sealed record MfaStatusDto(bool Enabled, bool PendingEnrollment);

/// <summary>Destek erişimi (impersonation) grant satırı. Status: Pending|Approved|Active|Expired|Revoked (süresi dolan grant Expired görünür).</summary>
public sealed record ImpersonationGrantDto(
    Guid Id, Guid PlatformUserId, string? PlatformUserEmail, string Reason, string Status, DateTime RequestedAt, DateTime? ApprovedAt,
    Guid? ApprovedByUserId, DateTime? ExpiresAt, DateTime? StartedAt, DateTime? RevokedAt);

public sealed record ImpersonationSessionDto(string AccessToken, DateTime AccessTokenExpiresAt)
{
    public string TokenType => AuthTokenTypes.Bearer;
}

public sealed record MeDto(
    Guid UserId, string Email, string FirstName, string LastName, Guid? EmployeeId, string PreferredLanguage, bool MfaEnabled,
    TenantDto Tenant, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<string> Permissions, IReadOnlyDictionary<string, DataScope> Scopes,
    FeaturesDto Features);

/// <summary>
/// Kiracıya bağlı, izin dışı özellik ayarları; ön yüz menü/widget filtrelemesi için tek yanıtta taşınır.
/// Access = çağırana göre ETKİN değerler (bypass varsa hepsi true).
/// </summary>
/// <param name="MfaSetupRequired">senseik-051: çağıranın rolü güvenlik politikasında 2FA zorunlu listesinde VE MFA'sı
/// kapalı — ön yüz kritik ekranlara erişmeden önce kurulum akışına yönlendirmeli (bkz. Tenants.TenantSecurityPolicy).</param>
public sealed record FeaturesDto(Tenants.AccessSettingsDto Access, bool AccessSettingsBypass, bool MfaSetupRequired);

/// <summary>Birim ağacı satır menüsü "Birim yetkilendirme" listesindeki tek bir grant (bkz. UnitAuthorizations.UnitAuthorizationCommands).</summary>
public sealed record UnitAuthorizationDto(
    Guid Id,
    Guid OrgUnitId,
    Guid UserId,
    bool CanViewSalaryAndPayments,
    bool CanViewUnitReports,
    Guid GrantedByUserId,
    DateTime GrantedAt,
    bool IsActive,
    string UserFullName,
    string UserEmail,
    Guid? EmployeeId)
{
    public static UnitAuthorizationDto From(Domain.UnitAuthorizations.UnitAuthorization a, UserSummaryDto? user) => new(
        a.Id, a.OrgUnitId, a.UserId, a.CanViewSalaryAndPayments, a.CanViewUnitReports, a.GrantedByUserId, a.GrantedAt, a.IsActive,
        user?.FullName ?? string.Empty, user?.Email ?? string.Empty, user?.EmployeeId);
}

/// <summary>"Birim yetkilendirme" kişi seçicisi: aktif veya davet edilmiş kullanıcılar.</summary>
public sealed record UnitAuthorizationCandidateDto(Guid UserId, string FullName, string Email, Guid? EmployeeId);

/// <summary>Kullanıcı özet satırı (liste zenginleştirme).</summary>
public sealed record UserSummaryDto(Guid UserId, string FullName, string Email, Guid? EmployeeId);

/// <summary>
/// TB-2 (S-1 sözleşmesi, FE ile dondurulmuş): denetim izi satırı. <c>Action</c> sabit string'lerden
/// biridir (create|update|delete|login|invite|role_change). <c>Details</c>, çağıranın
/// <c>identity.audit.sensitive_view</c> izni yoksa API katmanında maskelenmiş döner.
/// </summary>
public sealed record AuditLogEntryDto(Guid Id, DateTimeOffset OccurredAt, string Actor, string Action, string Resource, string? ResourceId, string Details, string? Source);

/// <summary>Çalışana davet sonucu. Outcome: Sent|Resent|AlreadyActive; EmailSource: Work|Personal|Account.</summary>
public sealed record EmployeeInvitationResultDto(Guid EmployeeId, string Outcome, string? SentToEmail, string? EmailSource);

public static class EmployeeInvitationOutcomes
{
    public const string Sent = "Sent";
    public const string Resent = "Resent";
    public const string AlreadyActive = "AlreadyActive";
}

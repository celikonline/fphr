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

public sealed record ImpersonationGrantDto(
    Guid Id, Guid TenantId, Guid PlatformUserId, Guid? ApprovedByUserId, string Reason, string Status,
    DateTime RequestedAt, DateTime? ExpiresAt, DateTime? UsedAt, DateTime? RevokedAt);

public sealed record ImpersonationSessionDto(string AccessToken, DateTime AccessTokenExpiresAt)
{
    public string TokenType => AuthTokenTypes.Bearer;
}

public sealed record MeDto(
    Guid UserId, string Email, string FirstName, string LastName, Guid? EmployeeId, string PreferredLanguage, bool MfaEnabled,
    TenantDto Tenant, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<string> Permissions, IReadOnlyDictionary<string, DataScope> Scopes,
    FeaturesDto Features);

/// <summary>Kiracıya bağlı, izin dışı özellik ayarları; ön yüz menü/widget filtrelemesi için tek yanıtta taşınır.</summary>
public sealed record FeaturesDto(Tenants.AccessSettingsDto Access);

/// <summary>Birim ağacı satır menüsü "Birim yetkilendirme" listesindeki tek bir grant (bkz. UnitAuthorizations.UnitAuthorizationCommands).</summary>
public sealed record UnitAuthorizationDto(
    Guid Id,
    Guid OrgUnitId,
    Guid UserId,
    bool CanViewSalaryAndPayments,
    bool CanViewUnitReports,
    Guid GrantedByUserId,
    DateTime GrantedAt,
    bool IsActive)
{
    public static UnitAuthorizationDto From(Domain.UnitAuthorizations.UnitAuthorization a) => new(
        a.Id, a.OrgUnitId, a.UserId, a.CanViewSalaryAndPayments, a.CanViewUnitReports, a.GrantedByUserId, a.GrantedAt, a.IsActive);
}

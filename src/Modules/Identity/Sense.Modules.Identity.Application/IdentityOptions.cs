namespace Sense.Modules.Identity.Application;

/// <summary>Identity ayarları (appsettings "Identity" bölümü). Domain bu değerleri parametre olarak alır.</summary>
public sealed class IdentityOptions
{
    public int TrialDays { get; set; } = IdentityDefaults.TrialDays;

    public int MaxFailedAccessAttempts { get; set; } = IdentityDefaults.MaxFailedAccessAttempts;

    public int LockoutMinutes { get; set; } = IdentityDefaults.LockoutMinutes;

    public int InvitationValidityHours { get; set; } = IdentityDefaults.InvitationValidityHours;

    public int PasswordResetValidityMinutes { get; set; } = IdentityDefaults.PasswordResetValidityMinutes;

    public int MinPasswordLength { get; set; } = IdentityDefaults.MinPasswordLength;

    public int AccessTokenMinutes { get; set; } = IdentityDefaults.AccessTokenMinutes;

    public int RefreshTokenDays { get; set; } = IdentityDefaults.RefreshTokenDays;

    public int MfaTokenMinutes { get; set; } = IdentityDefaults.MfaTokenMinutes;

    public int TrustedDeviceDays { get; set; } = IdentityDefaults.TrustedDeviceDays;

    /// <summary>Onaylanan impersonation grantının kullanılabilir kalma süresi (saat).</summary>
    public int ImpersonationWindowHours { get; set; } = IdentityDefaults.ImpersonationWindowHours;

    public string DefaultLanguage { get; set; } = IdentityDefaults.DefaultLanguage;

    public string DefaultLocale { get; set; } = IdentityDefaults.DefaultLocale;

    public string DefaultTimeZone { get; set; } = IdentityDefaults.DefaultTimeZone;

    /// <summary>Self-signup açık mı (kapalıysa yalnız platform admin kiracı açar).</summary>
    public bool AllowSelfRegistration { get; set; } = true;
}

public static class IdentityDefaults
{
    public const int TrialDays = 15;
    public const int MaxFailedAccessAttempts = 5;
    public const int LockoutMinutes = 15;
    public const int InvitationValidityHours = 72;
    public const int PasswordResetValidityMinutes = 60;
    public const int MinPasswordLength = 10;
    public const int AccessTokenMinutes = 15;
    public const int RefreshTokenDays = 30;
    public const int MfaTokenMinutes = 5;
    public const int TrustedDeviceDays = 30;
    public const int ImpersonationWindowHours = 4;
    public const string DefaultLanguage = "tr";
    public const string DefaultLocale = "tr-TR";
    public const string DefaultTimeZone = "Europe/Istanbul";
    public const int TokenBytes = 32;
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 40;
    public const int NameMaxLength = 100;
    public const int CompanyNameMaxLength = 200;
    public const int RoleCodeMaxLength = 50;
    public const int DeviceInfoMaxLength = 300;
    public const int ReasonMaxLength = 500;
}

/// <summary>Identity hata kodları (= yerelleştirme anahtarları).</summary>
public static class IdentityErrors
{
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string UserDisabled = "auth.user_disabled";
    public const string UserNotActivated = "auth.user_not_activated";
    public const string LockedOut = "auth.locked_out";
    public const string TenantInactive = "auth.tenant_inactive";
    public const string RefreshTokenInvalid = "auth.refresh_token_invalid";
    public const string RefreshTokenReused = "auth.refresh_token_reused";
    public const string TenantRequired = "auth.tenant_required";
    public const string MfaRequired = "auth.mfa_required";
    public const string MfaInvalid = "auth.mfa_invalid";

    public const string TenantInvalidSlug = "tenant.invalid_slug";
    public const string TenantSlugTaken = "tenant.slug_taken";
    public const string TenantNotFound = "tenant.not_found";
    public const string TenantClosed = "tenant.closed";

    public const string UserInvalidEmail = "user.invalid_email";
    public const string UserEmailTaken = "user.email_taken";
    public const string UserNotFound = "user.not_found";
    public const string UserNoPassword = "user.no_password";
    public const string UserLastOwner = "user.last_owner";
    public const string UserCannotModifySelf = "user.cannot_modify_self";
    public const string PasswordTooShort = "user.password_too_short";
    public const string PasswordMismatch = "user.password_mismatch";

    public const string PasswordResetTokenNotFound = "password_reset.not_found";
    public const string PasswordResetTokenExpired = "password_reset.expired";

    public const string RoleInvalidCode = "role.invalid_code";
    public const string RoleSystemCode = "role.system_code";
    public const string RoleCodeTaken = "role.code_taken";
    public const string RoleNotFound = "role.not_found";
    public const string RoleSystemReadOnly = "role.system_readonly";
    public const string RoleLocked = "role.locked";
    public const string RoleInUse = "role.in_use";
    public const string RoleUnknownPermission = "role.unknown_permission";

    public const string InvitationNotFound = "invitation.not_found";
    public const string InvitationAlreadyAccepted = "invitation.already_accepted";
    public const string InvitationExpired = "invitation.expired";
    public const string InvitationUserActive = "invitation.user_active";

    public const string AccessTypeInvalid = "user.access_type_invalid";

    /// <summary>Owner rolünü yalnız bir Owner verebilir/kaldırabilir.</summary>
    public const string AccessTypeOwnerOnly = "user.access_type_owner_only";

    /// <summary>Admin rolünü yalnız Owner veya Admin verebilir/kaldırabilir.</summary>
    public const string AccessTypeForbidden = "user.access_type_forbidden";

    public const string InvitationEmailRequired = "identity.invitation.email_required";
    public const string InvitationAlreadyActive = "identity.invitation.already_active";
    public const string InvitationEmployeeNotFound = "identity.invitation.employee_not_found";

    /// <summary>Hedef e-posta, başka bir çalışana bağlı bir kullanıcıya ait.</summary>
    public const string InvitationEmailInUse = "identity.invitation.email_in_use";

    public const string InvitationUserDisabled = "identity.invitation.user_disabled";

    public const string ImpersonationNotFound = "impersonation.not_found";
    public const string ImpersonationNotPending = "impersonation.not_pending";
    public const string ImpersonationNotApproved = "impersonation.not_approved";
    public const string ImpersonationExpired = "impersonation.expired";
    public const string ImpersonationNotRevocable = "impersonation.not_revocable";
    public const string ImpersonationRequiresPlatformStaff = "impersonation.requires_platform_staff";
    public const string ImpersonationForbidden = "impersonation.forbidden";
    public const string ImpersonationReasonRequired = "impersonation.reason_required";
    public const string ImpersonationStatusInvalid = "impersonation.status_invalid";

    /// <summary>Organization.Contracts.IOrgUnitLookup ile doğrulanamayan OrgUnitId (Birim yetkilendirme grant'i).</summary>
    public const string OrgUnitNotFound = "identity.org_unit_not_found";
    public const string UnitAuthorizationNotFound = "unit_authorization.not_found";
    public const string UnitAuthorizationAlreadyRevoked = "unit_authorization.already_revoked";
    public const string UnitAuthorizationNoGrantsSelected = "unit_authorization.no_grants_selected";

    /// <summary>Mesaj parametre adları.</summary>
    public static class Args
    {
        public const string Minutes = "minutes";
        public const string Min = "min";
        public const string Count = "count";
        public const string Permission = "permission";
    }
}

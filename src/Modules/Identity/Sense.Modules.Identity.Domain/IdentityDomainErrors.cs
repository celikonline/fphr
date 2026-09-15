namespace Sense.Modules.Identity.Domain;

/// <summary>Domain'in ürettiği hata kodları (Application/Web yerelleştirir). Application'daki IdentityErrors ile aynı anahtarlar.</summary>
public static class IdentityDomainErrors
{
    public const string TenantInvalidSlug = "tenant.invalid_slug";
    public const string TenantClosed = "tenant.closed";
    public const string UserInvalidEmail = "user.invalid_email";
    public const string UserDisabled = "auth.user_disabled";
    public const string UserNotActivated = "auth.user_not_activated";
    public const string LockedOut = "auth.locked_out";
    public const string UserNoPassword = "user.no_password";
    public const string RoleInvalidCode = "role.invalid_code";
    public const string RoleSystemCode = "role.system_code";
    public const string RoleSystemReadOnly = "role.system_readonly";
    public const string RoleLocked = "role.locked";
    public const string RoleInUse = "role.in_use";
    public const string InvitationAlreadyAccepted = "invitation.already_accepted";
    public const string InvitationExpired = "invitation.expired";

    public const string AccessTypeInvalid = "user.access_type_invalid";

    public const string ImpersonationNotPending = "impersonation.not_pending";
    public const string ImpersonationNotApproved = "impersonation.not_approved";
    public const string ImpersonationExpired = "impersonation.expired";
    public const string ImpersonationNotRevocable = "impersonation.not_revocable";

    public const string UnitAuthorizationAlreadyRevoked = "unit_authorization.already_revoked";
    public const string UnitAuthorizationNoGrantsSelected = "unit_authorization.no_grants_selected";

    public static class Args
    {
        public const string Minutes = "minutes";
        public const string Count = "count";
    }
}

/// <summary>Domain sınırları (uzunluklar vb.). Ayar değil, veri modeli kısıtı.</summary>
public static class IdentityLimits
{
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 40;
    public const int NameMaxLength = 100;
    public const int CompanyNameMaxLength = 200;
    public const int RoleCodeMaxLength = 50;
    public const int DeviceInfoMaxLength = 300;
    public const int ReasonMaxLength = 500;
    public const char SlugSeparator = '-';
    public const string EmptyJsonObject = "{}";
    public const string AnonymizedEmailFormat = "silinmis-{0}@anonim.local";
    public const int AnonymizedSuffixLength = 8;
}

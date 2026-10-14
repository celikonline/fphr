using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Application;

/// <summary>Identity modülünün izin kataloğu. DisplayName/Group değerleri kaynak anahtarıdır; API katmanı yerelleştirir.</summary>
public static class IdentityPermissions
{
    public const string Module = "identity";

    public const string UsersView = "identity.users.view";
    public const string UsersManage = "identity.users.manage";
    public const string UsersInvite = "identity.users.invite";
    public const string UsersSuspend = "identity.users.suspend";

    // Kullanılmıyor — yönetici MFA sıfırlama identity.users.manage (tur 1). Kaldırma kararı backlog B-3-01.
    public const string UsersResetMfa = "identity.users.reset_mfa";
    public const string UsersImpersonate = "identity.users.impersonate";
    public const string RolesView = "identity.roles.view";
    public const string RolesManage = "identity.roles.manage";
    public const string TenantView = "identity.tenant.view";
    public const string TenantManage = "identity.tenant.manage";
    public const string SecurityPolicyManage = "identity.security.manage";

    /// <summary>Ayarlar &gt; Modüller: kiracı için ürün modüllerini aç/kapa (senseik-057). Owner/Admin joker "*" ile alır.</summary>
    public const string ModulesManage = "identity.modules.manage";
    public const string ApiKeysManage = "identity.api_keys.manage";
    public const string AuditLogView = "identity.audit.view";
    public const string AuditSensitiveView = "identity.audit.sensitive_view";

    /// <summary>Yalnız kiracı tarafı Owner/Admin (Owner/Admin joker "*" izin setine dahildir; diğer rollere verilmez).</summary>
    public const string ImpersonationApprove = "identity.impersonation.approve";

    /// <summary>Birim ağacı satır menüsü "Birim yetkilendirme": grant/revoke (Faz 0 madde 3).</summary>
    public const string UnitAuthorizationsManage = "identity.unit_authorizations.manage";

    /// <summary>Bir birim için mevcut yetkilendirme listesini görüntüleme.</summary>
    public const string UnitAuthorizationsRead = "identity.unit_authorizations.read";

    /// <summary>Erişim ayarları kısıtlarını aşar (Owner/Admin joker ile, HR açıkça); çalışan tipi kullanıcılara verilmez.</summary>
    public const string AccessSettingsBypass = AccessSettingPermissions.Bypass;

    /// <summary>
    /// Platform (süper admin): yeni şirket (kiracı) oluşturma ve kiracı listesi. Joker ("*") ve Owner kilitli rolü bu
    /// anahtarı ALMAZ (bkz. <see cref="PlatformPermissions"/>); yalnız açıkça "platform.*" içeren SuperAdmin rolü alır.
    /// </summary>
    public const string PlatformTenantsManage = PlatformPermissions.TenantsManage;

    public static class Groups
    {
        public const string Users = "identity.group.users";
        public const string Roles = "identity.group.roles";
        public const string Account = "identity.group.account";
        public const string Security = "identity.group.security";
        public const string Audit = "identity.group.audit";
        public const string Impersonation = "identity.group.impersonation";
        public const string UnitAuthorizations = "identity.group.unit_authorizations";
        public const string AccessSettings = "identity.group.access_settings";
        public const string Platform = "identity.group.platform";
    }

    public static readonly IReadOnlyList<Permission> All =
    [
        new(UsersView, Module, UsersView, Groups.Users),
        new(UsersManage, Module, UsersManage, Groups.Users),
        new(UsersInvite, Module, UsersInvite, Groups.Users),
        new(UsersSuspend, Module, UsersSuspend, Groups.Users),
        new(UsersResetMfa, Module, UsersResetMfa, Groups.Users),
        new(UsersImpersonate, Module, UsersImpersonate, Groups.Users),
        new(RolesView, Module, RolesView, Groups.Roles),
        new(RolesManage, Module, RolesManage, Groups.Roles),
        new(TenantView, Module, TenantView, Groups.Account),
        new(TenantManage, Module, TenantManage, Groups.Account),
        new(SecurityPolicyManage, Module, SecurityPolicyManage, Groups.Security),
        new(ApiKeysManage, Module, ApiKeysManage, Groups.Security),
        new(AuditLogView, Module, AuditLogView, Groups.Audit),
        new(AuditSensitiveView, Module, AuditSensitiveView, Groups.Audit),
        new(ImpersonationApprove, Module, ImpersonationApprove, Groups.Impersonation),
        new(UnitAuthorizationsManage, Module, UnitAuthorizationsManage, Groups.UnitAuthorizations),
        new(UnitAuthorizationsRead, Module, UnitAuthorizationsRead, Groups.UnitAuthorizations),
        new(AccessSettingsBypass, Module, AccessSettingsBypass, Groups.AccessSettings),
        new(ModulesManage, Module, ModulesManage, Groups.Account),
        new(PlatformTenantsManage, Module, PlatformTenantsManage, Groups.Platform),
        new(PlatformPermissions.OperationsView, Module, PlatformPermissions.OperationsView, Groups.Platform),
        new(PlatformPermissions.BackupsView, Module, PlatformPermissions.BackupsView, Groups.Platform),
        new(PlatformPermissions.BackupsRun, Module, PlatformPermissions.BackupsRun, Groups.Platform),
        new(PlatformPermissions.BackupsRestore, Module, PlatformPermissions.BackupsRestore, Groups.Platform),
        new(PlatformPermissions.JobsView, Module, PlatformPermissions.JobsView, Groups.Platform),
        new(PlatformPermissions.JobsTrigger, Module, PlatformPermissions.JobsTrigger, Groups.Platform),
        new(PlatformPermissions.JobsManage, Module, PlatformPermissions.JobsManage, Groups.Platform),
        new(PlatformPermissions.MaintenanceView, Module, PlatformPermissions.MaintenanceView, Groups.Platform),
        new(PlatformPermissions.MaintenanceManage, Module, PlatformPermissions.MaintenanceManage, Groups.Platform),
        new(PlatformPermissions.DiagnosticsView, Module, PlatformPermissions.DiagnosticsView, Groups.Platform),
        new(PlatformPermissions.AuditView, Module, PlatformPermissions.AuditView, Groups.Platform),
        new(PlatformPermissions.AuditExport, Module, PlatformPermissions.AuditExport, Groups.Platform),
    ];
}

/// <summary>
/// Platform düzeyi izinler: müşteri kiracılarının hiçbir rolüne (Owner/Admin jokeri dahil) verilmez. Rol çözümlemesi
/// (<see cref="Roles.SystemRolePermissionResolver"/>) bu anahtarları yalnız "platform." ile başlayan açık bir desen eşlerse verir.
/// </summary>
public static class PlatformPermissions
{
    public const string Prefix = "platform.";

    public const string Wildcard = Prefix + "*";

    public const string TenantsManage = "platform.tenants.manage";
    public const string OperationsView = "platform.operations.view";
    public const string BackupsView = "platform.backups.view";
    public const string BackupsRun = "platform.backups.run";
    public const string BackupsRestore = "platform.backups.restore";
    public const string JobsView = "platform.jobs.view";
    public const string JobsTrigger = "platform.jobs.trigger";
    public const string JobsManage = "platform.jobs.manage";
    public const string MaintenanceView = "platform.maintenance.view";
    public const string MaintenanceManage = "platform.maintenance.manage";
    public const string DiagnosticsView = "platform.diagnostics.view";
    public const string AuditView = "platform.audit.view";
    public const string AuditExport = "platform.audit.export";

    public static bool IsPlatformKey(string permissionKey) => permissionKey.StartsWith(Prefix, StringComparison.Ordinal);
}

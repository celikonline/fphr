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
    public const string UsersResetMfa = "identity.users.reset_mfa";
    public const string UsersImpersonate = "identity.users.impersonate";
    public const string RolesView = "identity.roles.view";
    public const string RolesManage = "identity.roles.manage";
    public const string TenantView = "identity.tenant.view";
    public const string TenantManage = "identity.tenant.manage";
    public const string SecurityPolicyManage = "identity.security.manage";
    public const string ApiKeysManage = "identity.api_keys.manage";
    public const string AuditLogView = "identity.audit.view";
    public const string AuditSensitiveView = "identity.audit.sensitive_view";

    /// <summary>Yalnız kiracı tarafı Owner/Admin (Owner/Admin joker "*" izin setine dahildir; diğer rollere verilmez).</summary>
    public const string ImpersonationApprove = "identity.impersonation.approve";

    public static class Groups
    {
        public const string Users = "identity.group.users";
        public const string Roles = "identity.group.roles";
        public const string Account = "identity.group.account";
        public const string Security = "identity.group.security";
        public const string Audit = "identity.group.audit";
        public const string Impersonation = "identity.group.impersonation";
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
    ];
}

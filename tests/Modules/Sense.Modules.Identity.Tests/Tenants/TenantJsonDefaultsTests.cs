using System.Runtime.CompilerServices;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Tenants;

/// <summary>
/// Tenants that existed before the security-policy/module-settings columns carry the migration default '{}'. EF Core
/// materializes that jsonb value with null lists (the property initializers do not run), which used to crash
/// GET /identity/me with a NullReferenceException for every user of such a tenant.
/// </summary>
public sealed class TenantJsonDefaultsTests
{
    [Fact]
    public void SecurityPolicy_MaterializedWithoutList_TreatsRolesAsEmpty()
    {
        var policy = (TenantSecurityPolicy)RuntimeHelpers.GetUninitializedObject(typeof(TenantSecurityPolicy));

        policy.RequireMfaForRoles.ShouldBeEmpty();
        policy.RequiresMfaSetup([SystemRoleCodes.Owner], mfaEnabled: false).ShouldBeFalse();
    }

    [Fact]
    public void ModuleSettings_MaterializedWithoutList_TreatsAllModulesAsEnabled()
    {
        var settings = (TenantModuleSettings)RuntimeHelpers.GetUninitializedObject(typeof(TenantModuleSettings));

        settings.DisabledModules.ShouldBeEmpty();
        settings.IsDisabled("performance").ShouldBeFalse();
    }
}

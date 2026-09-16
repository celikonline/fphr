using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Assets.Contracts;

/// <summary>
/// Assets module permission definitions (zimmet - roadmap #65). Sense.Modules.Identity.Application.Roles.
/// SystemRoleDefinitions already anticipated this module's key shape before any Assets code existed ("assets.*" for
/// HR/HRSpecialist), so SystemRoleDefinitions.cs needs no change for this package to grant correctly; Employee's
/// role already grants "assets.my.assets" through the general "*.my.*" self-service wildcard
/// (PermissionPatterns.SelfService). Keys are exactly module.resource.action (3 segments, snake_case) per
/// Sense.Tests.Architecture.OnionArchitectureTests.PermissionKeys_FollowNamingConvention.
/// </summary>
public static class AssetsPermissions
{
    private const string Module = "assets";
    private const string Group = "Assets";

    public static readonly Permission AssetsView = new(
        "assets.assets.view", Module, "View Assets", Group, "View the company's assigned/unassigned equipment");

    public static readonly Permission AssetsManage = new(
        "assets.assets.manage", Module, "Manage Assets", Group, "Create, edit, assign, return and retire equipment");

    public static readonly Permission CategoriesView = new(
        "assets.categories.view", Module, "View Asset Categories", Group, "View equipment categories");

    public static readonly Permission CategoriesManage = new(
        "assets.categories.manage", Module, "Manage Asset Categories", Group, "Create or rename equipment categories");

    public static readonly Permission MyAssets = new(
        "assets.my.assets", Module, "Own Assets", Group, "View the equipment currently assigned to you");

    public static IEnumerable<Permission> All =>
    [
        AssetsView, AssetsManage,
        CategoriesView, CategoriesManage,
        MyAssets,
    ];
}

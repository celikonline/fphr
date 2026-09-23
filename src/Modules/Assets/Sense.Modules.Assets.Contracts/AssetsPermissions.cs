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
    private const string Group = "Zimmet";

    public static readonly Permission AssetsView = new(
        "assets.assets.view", Module, "Zimmetleri Görüntüleme", Group, "Şirketin zimmetli/zimmetsiz ekipmanlarını görüntüleme");

    public static readonly Permission AssetsManage = new(
        "assets.assets.manage", Module, "Zimmet Yönetimi", Group, "Ekipman oluşturma, düzenleme, zimmetleme, iade alma ve hizmet dışı bırakma");

    public static readonly Permission CategoriesView = new(
        "assets.categories.view", Module, "Zimmet Kategorilerini Görüntüleme", Group, "Ekipman kategorilerini görüntüleme");

    public static readonly Permission CategoriesManage = new(
        "assets.categories.manage", Module, "Zimmet Kategorisi Yönetimi", Group, "Ekipman kategorisi oluşturma veya yeniden adlandırma");

    public static readonly Permission MyAssets = new(
        "assets.my.assets", Module, "Kendi Zimmetlerim", Group, "Üzerinize zimmetli ekipmanları görüntüleme");

    public static readonly Permission AssetsImport = new(
        "assets.assets.import", Module, "Zimmet İçe Aktarma", Group, "Zimmet Excel dosyasından toplu içe aktarma (Ayarlar > Veri aktar > Zimmet)");

    public static IEnumerable<Permission> All =>
    [
        AssetsView, AssetsManage,
        CategoriesView, CategoriesManage,
        MyAssets,
        AssetsImport,
    ];
}

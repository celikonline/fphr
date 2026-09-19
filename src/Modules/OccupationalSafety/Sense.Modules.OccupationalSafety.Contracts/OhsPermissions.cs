using Sense.Shared.Contracts.Security;

namespace Sense.Modules.OccupationalSafety.Contracts;

/// <summary>
/// Occupational Safety (İSG, senseik-015) module permission definitions. Keys are exactly
/// module.resource.action (3 segments, snake_case) per Sense.Tests.Architecture.OnionArchitectureTests.
/// PermissionKeys_FollowNamingConvention. Phase 0-1 scope only (hazard classification + risk assessments) -
/// committee/accident/PPE permissions are added in their own later phases (see the handoff doc).
/// </summary>
public static class OhsPermissions
{
    private const string Module = "ohs";
    private const string Group = "İSG";

    public static readonly Permission HazardClassificationsView = new(
        "ohs.hazard_classifications.view", Module, "Tehlike Sınıflarını Görüntüleme", Group,
        "Her lokasyona atanan işyeri tehlike sınıfını görüntüleme");

    public static readonly Permission HazardClassificationsManage = new(
        "ohs.hazard_classifications.manage", Module, "Tehlike Sınıflarını Yönetme", Group,
        "Bir lokasyonun işyeri tehlike sınıfını atama veya güncelleme");

    public static readonly Permission RiskAssessmentsView = new(
        "ohs.risk_assessments.view", Module, "Risk Değerlendirmelerini Görüntüleme", Group,
        "Kayıtlı risk değerlendirmelerini ve yenileme son tarihlerini görüntüleme");

    public static readonly Permission RiskAssessmentsManage = new(
        "ohs.risk_assessments.manage", Module, "Risk Değerlendirmelerini Yönetme", Group,
        "Yeni risk değerlendirmesi kaydetme");

    public static IEnumerable<Permission> All =>
    [
        HazardClassificationsView, HazardClassificationsManage,
        RiskAssessmentsView, RiskAssessmentsManage,
    ];
}

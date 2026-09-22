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

    /// <summary>Faz 3: iş kazası / ramak kala kayıtları (HR/HRSpecialist "ohs.*" ile alır).</summary>
    public static readonly Permission IncidentsView = new(
        "ohs.incidents.view", Module, "İş Kazası ve Ramak Kala Kayıtlarını Görüntüleme", Group,
        "İş kazası ve ramak kala kayıtlarını, SGK bildirim son günlerini görüntüleme");

    public static readonly Permission IncidentsManage = new(
        "ohs.incidents.manage", Module, "İş Kazası ve Ramak Kala Kayıtlarını Yönetme", Group,
        "Olay kaydetme, SGK bildirimini işaretleme ve düzeltici önlemle kapatma");

    /// <summary>Self-servis ramak kala bildirimi; "*.my.*" deseniyle her temel role otomatik ulaşır.</summary>
    public static readonly Permission MyIncidentReports = new(
        "ohs.my.incident_reports", Module, "Ramak Kala Bildirimlerim", Group,
        "Ramak kala olayı bildirme ve kendi bildirimlerini görüntüleme");

    /// <summary>Faz 2: İSG uzmanı/işyeri hekimi görevlendirmeleri ve İSG kurulu (HR/HRSpecialist "ohs.*" ile alır).</summary>
    public static readonly Permission ServicesView = new(
        "ohs.services.view", Module, "İSG Hizmetlerini Görüntüleme", Group,
        "Uzman/hekim görevlendirmelerini, İSG kurulunu ve lokasyon uyum durumunu görüntüleme");

    public static readonly Permission ServicesManage = new(
        "ohs.services.manage", Module, "İSG Hizmetlerini Yönetme", Group,
        "Uzman/hekim görevlendirmesi ve İSG kurulu üyeliği ekleme/sonlandırma");

    public static IEnumerable<Permission> All =>
    [
        HazardClassificationsView, HazardClassificationsManage,
        RiskAssessmentsView, RiskAssessmentsManage,
        IncidentsView, IncidentsManage, MyIncidentReports,
        ServicesView, ServicesManage,
    ];
}

using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Performance.Contracts;

/// <summary>
/// Performance module permission definitions (R0 skeleton: ReviewCycle/ReviewForm/Goal only - 360 derece geri
/// bildirim, birebir görüşme notu ve yetkinlik matrisi sonraki paketler). SystemRoleDefinitions.cs already reserves
/// "performance.*" (HR), "performance.team.*" + "performance.goals.*" (Manager), and the SelfService "*.my.*" joker
/// (Employee/Intern/Contractor) - the keys below are chosen to match those jokers exactly, so no
/// SystemRoleDefinitions.cs change is needed for this package.
/// </summary>
public static class PerformancePermissions
{
    private const string Module = "performance";
    private const string Group = "Performans";

    public static readonly Permission ReviewsView = new(
        "performance.reviews.view", Module, "Değerlendirme Dönemlerini Görüntüleme", Group, "Değerlendirme dönemlerini ve formlarını görüntüleme");

    public static readonly Permission ReviewsManage = new(
        "performance.reviews.manage", Module, "Değerlendirme Dönemi Yönetimi", Group, "Değerlendirme dönemi/form oluşturma, düzenleme, etkinleştirme");

    public static readonly Permission ReviewsApprove = new(
        "performance.reviews.approve", Module, "Değerlendirme Dönemini Kapatma", Group, "Bir değerlendirme dönemini kapatarak sonuçları kesinleştirme (geri alınamaz)");

    public static readonly Permission GoalsView = new(
        "performance.goals.view", Module, "Hedefleri Görüntüleme", Group, "Herhangi bir çalışanın hedeflerini/OKR'lerini görüntüleme");

    public static readonly Permission GoalsManage = new(
        "performance.goals.manage", Module, "Hedef Yönetimi", Group, "Çalışan hedefi oluşturma, ilerleme güncelleme, iptal etme");

    public static readonly Permission MyReviews = new(
        "performance.my.reviews", Module, "Kendi Değerlendirmelerim", Group, "Kendi değerlendirme dönemlerini görüntüleme");

    public static readonly Permission MyGoals = new(
        "performance.my.goals", Module, "Kendi Hedeflerim", Group, "Kendi hedefini oluşturma, görüntüleme ve ilerleme güncelleme");

    public static readonly Permission TeamGoals = new(
        "performance.team.goals", Module, "Ekip Hedefleri", Group, "Yöneticinin ekibindeki çalışanların hedeflerini görüntüleme");

    public static IEnumerable<Permission> All =>
        [ReviewsView, ReviewsManage, ReviewsApprove, GoalsView, GoalsManage, MyReviews, MyGoals, TeamGoals];
}

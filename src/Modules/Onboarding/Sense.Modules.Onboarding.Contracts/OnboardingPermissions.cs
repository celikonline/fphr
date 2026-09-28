using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Onboarding.Contracts;

/// <summary>Onboarding module permission definitions (HR/HRSpecialist get them through "onboarding.*").</summary>
public static class OnboardingPermissions
{
    private const string Module = "onboarding";

    /// <summary>Create/edit/(de)activate onboarding and offboarding task templates.</summary>
    public static readonly Permission TemplatesManage = new(
        "onboarding.templates.manage",
        Module,
        "Oryantasyon Şablonu Yönetimi",
        "Oryantasyon",
        "İşe alım/işten ayrılış görev şablonları oluşturma, düzenleme ve etkinleştirme/pasifleştirme");

    /// <summary>View employees' materialized onboarding/offboarding task checklists.</summary>
    public static readonly Permission TasksView = new(
        "onboarding.tasks.view",
        Module,
        "Oryantasyon Görevlerini Görüntüleme",
        "Oryantasyon",
        "Çalışanların işe alım/işten ayrılış görev listelerini görüntüleme");

    /// <summary>Materialize, complete and waive employees' onboarding/offboarding tasks.</summary>
    public static readonly Permission TasksManage = new(
        "onboarding.tasks.manage",
        Module,
        "Oryantasyon Görevi Yönetimi",
        "Oryantasyon",
        "Çalışanların işe alım/işten ayrılış görevlerini oluşturma, tamamlama ve muaf tutma");

    /// <summary>Self-service: view and complete the caller's own onboarding/offboarding tasks. Matches the
    /// "*.my.*" self-service wildcard, so every tenant's Employee/Manager system role grants it automatically.</summary>
    public static readonly Permission TasksMine = new(
        "onboarding.my.tasks",
        Module,
        "Kendi Oryantasyon Görevlerim",
        "Oryantasyon",
        "Çalışanın kendi işe alım/işten ayrılış görevlerini görüntüleme ve tamamlama");

    /// <summary>All Onboarding module permissions.</summary>
    public static IEnumerable<Permission> All =>
    [
        TemplatesManage,
        TasksView,
        TasksManage,
        TasksMine,
    ];
}

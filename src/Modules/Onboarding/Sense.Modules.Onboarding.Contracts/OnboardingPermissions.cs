using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Onboarding.Contracts;

/// <summary>Onboarding module permission definitions. Not yet wired into <c>SystemRoleDefinitions</c> - ADR-0012
/// Kapı A is Domain/Application/Contracts only, no host registration this turn.</summary>
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

    /// <summary>All Onboarding module permissions.</summary>
    public static IEnumerable<Permission> All =>
    [
        TemplatesManage,
        TasksView,
        TasksManage,
    ];
}

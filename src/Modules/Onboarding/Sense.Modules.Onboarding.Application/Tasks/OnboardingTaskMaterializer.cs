using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;

namespace Sense.Modules.Onboarding.Application.Tasks;

/// <summary>
/// Creates one <see cref="EmployeeOnboardingTask"/> per active template of a stage for an employee. Shared by the
/// HR command (<see cref="MaterializeTasksForEmployeeHandler"/>) and the hire/termination integration handlers.
/// Idempotent: a template already materialized for the employee is skipped, so a redelivered event never duplicates rows.
/// </summary>
public static class OnboardingTaskMaterializer
{
    public static async Task<IReadOnlyList<EmployeeOnboardingTask>> MaterializeAsync(
        Guid tenantId,
        IOnboardingTaskTemplateRepository templates,
        IEmployeeOnboardingTaskRepository tasks,
        Guid employeeId,
        OnboardingStage stage,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        var created = new List<EmployeeOnboardingTask>();
        foreach (var template in await templates.ListActiveByStageAsync(stage, cancellationToken))
        {
            if (await tasks.FindAsync(employeeId, template.Id, cancellationToken) is not null)
            {
                continue;
            }

            var task = EmployeeOnboardingTask.Materialize(
                tenantId, employeeId, template.Id, stage, referenceDate.AddDays(template.DueDateOffsetDays));
            tasks.Add(task);
            created.Add(task);
        }

        return created;
    }
}

/// <summary>
/// Önerilen görev şablonları (Türkiye mevzuatı): kiracının hiç şablonu yokken bir kez yüklenir, böylece ilk işe
/// alım/çıkışta boş liste oluşmaz. İK sonradan düzenler ya da pasifleştirir; silme olmadığı için bir daha yüklenmez.
/// Responsible role codes are the free-text codes the web labels ("hr", "it", "manager", "payroll").
/// </summary>
public static class DefaultOnboardingTemplates
{
    private static readonly (OnboardingStage Stage, string Title, string Role, int OffsetDays, bool Mandatory)[] Defaults =
    [
        (OnboardingStage.Onboarding, "SGK işe giriş bildirgesinin verilmesi", "hr", -1, true),
        (OnboardingStage.Onboarding, "İş sözleşmesinin imzalatılması", "hr", 0, true),
        (OnboardingStage.Onboarding, "Özlük dosyası belgelerinin toplanması", "hr", 7, true),
        (OnboardingStage.Onboarding, "İSG temel eğitiminin verilmesi", "hr", 0, true),
        (OnboardingStage.Onboarding, "E-posta ve sistem hesaplarının açılması", "it", 0, false),
        (OnboardingStage.Onboarding, "Bilgisayar ve ekipman zimmetinin teslimi", "it", 0, false),
        (OnboardingStage.Onboarding, "Oryantasyon ve ekip tanışma görüşmesi", "manager", 7, false),
        (OnboardingStage.Onboarding, "Deneme süresi değerlendirmesi", "manager", 55, false),
        (OnboardingStage.Offboarding, "Zimmetlerin iade alınması", "it", 0, true),
        (OnboardingStage.Offboarding, "Sistem hesaplarının kapatılması", "it", 0, true),
        (OnboardingStage.Offboarding, "Son maaş, kıdem ve ihbar hesabının yapılması", "payroll", 0, true),
        (OnboardingStage.Offboarding, "Çalışma belgesinin verilmesi", "hr", 0, true),
        (OnboardingStage.Offboarding, "SGK işten ayrılış bildirgesinin verilmesi", "hr", 10, true),
        (OnboardingStage.Offboarding, "Çıkış görüşmesinin yapılması", "hr", 0, false),
    ];

    /// <summary>Adds the defaults when the tenant has no template at all; returns how many were added.</summary>
    public static async Task<int> EnsureSeededAsync(
        Guid tenantId, IOnboardingTaskTemplateRepository templates, CancellationToken cancellationToken)
    {
        if (await templates.AnyAsync(cancellationToken))
        {
            return 0;
        }

        foreach (var (stage, title, role, offset, mandatory) in Defaults)
        {
            templates.Add(OnboardingTaskTemplate.Create(tenantId, stage, title, role, offset, mandatory).Value);
        }

        return Defaults.Length;
    }
}

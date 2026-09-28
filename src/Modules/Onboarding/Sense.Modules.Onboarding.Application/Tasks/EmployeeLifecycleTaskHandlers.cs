using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Onboarding.Application.Tasks;

/// <summary>
/// EmployeeHired → the new hire's onboarding checklist (due dates relative to HireDate); seeds the recommended
/// templates the first time a tenant has none. Integration handlers run outside the command pipeline, so this saves
/// explicitly (same as Documents' EmployeeHiredDocumentRequirementHandler); idempotent via OnboardingTaskMaterializer.
/// </summary>
public sealed class EmployeeHiredOnboardingHandler(
    IOnboardingTaskTemplateRepository templates,
    IEmployeeOnboardingTaskRepository tasks,
    IOnboardingUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox) : IIntegrationEventHandler<EmployeeHired>
{
    public async Task Handle(EmployeeHired integrationEvent, CancellationToken cancellationToken)
    {
        await EmployeeLifecycleTasks.CreateAsync(
            templates, tasks, unitOfWork, outbox, integrationEvent.TenantId, integrationEvent.EmployeeId,
            OnboardingStage.Onboarding, integrationEvent.HireDate, cancellationToken);
    }
}

/// <summary>EmployeeTerminated → the offboarding checklist, relative to the termination date (event date when absent).</summary>
public sealed class EmployeeTerminatedOffboardingHandler(
    IOnboardingTaskTemplateRepository templates,
    IEmployeeOnboardingTaskRepository tasks,
    IOnboardingUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : IIntegrationEventHandler<EmployeeTerminated>
{
    public async Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken)
    {
        var referenceDate = integrationEvent.TerminationDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await EmployeeLifecycleTasks.CreateAsync(
            templates, tasks, unitOfWork, outbox, integrationEvent.TenantId, integrationEvent.EmployeeId,
            OnboardingStage.Offboarding, referenceDate, cancellationToken);
    }
}

internal static class EmployeeLifecycleTasks
{
    public static async Task CreateAsync(
        IOnboardingTaskTemplateRepository templates,
        IEmployeeOnboardingTaskRepository tasks,
        IOnboardingUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        Guid tenantId,
        Guid employeeId,
        OnboardingStage stage,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        // Seeded templates must be persisted before they can be listed as "active" by the materializer's query.
        if (await DefaultOnboardingTemplates.EnsureSeededAsync(tenantId, templates, cancellationToken) > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await OnboardingTaskMaterializer.MaterializeAsync(
            tenantId, templates, tasks, outbox, employeeId, stage, referenceDate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

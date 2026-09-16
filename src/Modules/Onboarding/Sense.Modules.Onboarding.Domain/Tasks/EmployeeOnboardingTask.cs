using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Onboarding.Domain.Tasks;

public enum EmployeeOnboardingTaskStatus
{
    Pending = 0,
    Completed = 1,
    Waived = 2,
}

/// <summary>
/// One materialized checklist row for a given employee, produced from an <see cref="OnboardingTaskTemplate"/> at
/// hire/termination time. Once <see cref="Status"/> leaves <see cref="EmployeeOnboardingTaskStatus.Pending"/> the
/// row is final - completed/waived tasks cannot be reopened (G-4-13: same Waive-triple discipline as
/// Documents' <c>EmployeeDocumentRequirement</c>).
/// </summary>
public sealed class EmployeeOnboardingTask : TenantAggregateRoot<Guid>, IAuditLogged
{
    private EmployeeOnboardingTask()
    {
    }

    private EmployeeOnboardingTask(
        Guid id, Guid tenantId, Guid employeeId, Guid templateId, OnboardingStage stage, DateOnly dueDate)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        TemplateId = templateId;
        Stage = stage;
        DueDate = dueDate;
        Status = EmployeeOnboardingTaskStatus.Pending;
    }

    public Guid EmployeeId { get; private set; }

    public Guid TemplateId { get; private set; }

    public OnboardingStage Stage { get; private set; }

    public DateOnly DueDate { get; private set; }

    public EmployeeOnboardingTaskStatus Status { get; private set; }

    public Guid? CompletedBy { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public string? WaivedReason { get; private set; }

    public Guid? WaivedBy { get; private set; }

    public DateTime? WaivedAt { get; private set; }

    public static EmployeeOnboardingTask Materialize(
        Guid tenantId, Guid employeeId, Guid templateId, OnboardingStage stage, DateOnly dueDate) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, templateId, stage, dueDate);

    public Result Complete(Guid completedBy)
    {
        if (Status != EmployeeOnboardingTaskStatus.Pending)
        {
            return Error.Conflict(OnboardingDomainErrors.EmployeeTaskAlreadyFinalized);
        }

        Status = EmployeeOnboardingTaskStatus.Completed;
        CompletedBy = completedBy;
        CompletedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Waive(string reason, Guid waivedBy)
    {
        if (Status != EmployeeOnboardingTaskStatus.Pending)
        {
            return Error.Conflict(OnboardingDomainErrors.EmployeeTaskAlreadyFinalized);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation(OnboardingDomainErrors.EmployeeTaskWaiveReasonRequired);
        }

        Status = EmployeeOnboardingTaskStatus.Waived;
        WaivedReason = reason.Trim();
        WaivedBy = waivedBy;
        WaivedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// True when the task is still <see cref="EmployeeOnboardingTaskStatus.Pending"/> and its <see cref="DueDate"/>
    /// is strictly before <paramref name="today"/>. Derived on read - there is no separate stored "overdue" status.
    /// </summary>
    public bool IsOverdue(DateOnly today) =>
        Status == EmployeeOnboardingTaskStatus.Pending && DueDate < today;
}

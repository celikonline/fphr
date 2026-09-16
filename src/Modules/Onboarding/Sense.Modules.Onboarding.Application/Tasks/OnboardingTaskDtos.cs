using Sense.Modules.Onboarding.Domain.Tasks;

namespace Sense.Modules.Onboarding.Application.Tasks;

public sealed record OnboardingTaskTemplateDto(
    Guid Id,
    OnboardingStage Stage,
    string Title,
    string ResponsibleRoleCode,
    int DueDateOffsetDays,
    bool IsMandatory,
    bool IsActive)
{
    public static OnboardingTaskTemplateDto From(OnboardingTaskTemplate template) => new(
        template.Id, template.Stage, template.Title, template.ResponsibleRoleCode,
        template.DueDateOffsetDays, template.IsMandatory, template.IsActive);
}

public sealed record EmployeeOnboardingTaskDto(
    Guid Id,
    Guid EmployeeId,
    Guid TemplateId,
    OnboardingStage Stage,
    DateOnly DueDate,
    EmployeeOnboardingTaskStatus Status,
    Guid? CompletedBy,
    DateTime? CompletedAt,
    string? WaivedReason,
    Guid? WaivedBy,
    DateTime? WaivedAt)
{
    public static EmployeeOnboardingTaskDto From(EmployeeOnboardingTask task) => new(
        task.Id, task.EmployeeId, task.TemplateId, task.Stage, task.DueDate, task.Status,
        task.CompletedBy, task.CompletedAt, task.WaivedReason, task.WaivedBy, task.WaivedAt);
}

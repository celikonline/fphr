using Sense.Modules.Onboarding.Domain.Tasks;

namespace Sense.Modules.Onboarding.Application.Tasks;

public sealed record OnboardingTaskTemplateDto(
    Guid Id,
    OnboardingStage Stage,
    string Title,
    string ResponsibleRoleCode,
    int DueDateOffsetDays,
    bool IsMandatory,
    bool IsActive,
    Guid? CategoryId = null,
    OnboardingImportance Importance = OnboardingImportance.Normal,
    bool RequiresEDocument = false)
{
    public static OnboardingTaskTemplateDto From(OnboardingTaskTemplate template) => new(
        template.Id, template.Stage, template.Title, template.ResponsibleRoleCode,
        template.DueDateOffsetDays, template.IsMandatory, template.IsActive,
        template.CategoryId, template.Importance, template.RequiresEDocument);
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
    DateTime? WaivedAt,
    Guid? CategoryId = null,
    OnboardingImportance Importance = OnboardingImportance.Normal,
    bool RequiresEDocument = false,
    Guid? TemplateSetId = null)
{
    public static EmployeeOnboardingTaskDto From(EmployeeOnboardingTask task) => new(
        task.Id, task.EmployeeId, task.TemplateId, task.Stage, task.DueDate, task.Status,
        task.CompletedBy, task.CompletedAt, task.WaivedReason, task.WaivedBy, task.WaivedAt,
        task.CategoryId, task.Importance, task.RequiresEDocument, task.TemplateSetId);
}

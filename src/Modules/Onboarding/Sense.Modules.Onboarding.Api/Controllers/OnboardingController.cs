using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Onboarding.Api.Controllers;

public sealed record SaveTaskTemplateRequest(
    OnboardingStage Stage, string Title, string ResponsibleRoleCode, int DueDateOffsetDays, bool IsMandatory,
    Guid? CategoryId = null, OnboardingImportance Importance = OnboardingImportance.Normal, bool RequiresEDocument = false);

public sealed record SaveCategoryRequest(string Name, int SortOrder, bool IsActive = true);

public sealed record CreateTemplateSetRequest(OnboardingStage Stage, string Name, IReadOnlyList<Guid> TemplateIds);

public sealed record UpdateTemplateSetRequest(string Name, IReadOnlyList<Guid> TemplateIds, bool IsActive = true);

public sealed record StartProcessRequest(Guid TemplateSetId, DateOnly ReferenceDate);

public sealed record MaterializeTasksRequest(OnboardingStage Stage, DateOnly ReferenceDate);

public sealed record WaiveTaskRequest(string Reason);
public sealed record SaveOnboardingBuddyRequest(Guid BuddyEmployeeId, DateOnly StartDate, DateOnly? EndDate);

/// <summary>İşe alış / işten çıkış görev şablonları ve çalışan görev listeleri.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/onboarding")]
[Authorize]
public sealed class OnboardingController : ApiControllerBase
{
    [HttpGet("templates")]
    [ProducesResponseType<IReadOnlyList<OnboardingTaskTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTemplates(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListOnboardingTaskTemplatesQuery(), ct));

    [HttpPost("templates")]
    [ProducesResponseType<OnboardingTaskTemplateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveTaskTemplateRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateOnboardingTaskTemplateCommand(
            request.Stage, request.Title, request.ResponsibleRoleCode, request.DueDateOffsetDays, request.IsMandatory,
            request.CategoryId, request.Importance, request.RequiresEDocument), ct));

    /// <summary>Stage is fixed at creation; the request's Stage is ignored here.</summary>
    [HttpPut("templates/" + ApiRoutes.IdParam)]
    [ProducesResponseType<OnboardingTaskTemplateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] SaveTaskTemplateRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateOnboardingTaskTemplateCommand(
            id, request.Title, request.ResponsibleRoleCode, request.DueDateOffsetDays, request.IsMandatory,
            request.CategoryId, request.Importance, request.RequiresEDocument), ct));

    [HttpPost("templates/" + ApiRoutes.IdParam + "/activate")]
    [ProducesResponseType<OnboardingTaskTemplateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateTemplate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetOnboardingTaskTemplateActiveCommand(id, IsActive: true), ct));

    [HttpPost("templates/" + ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType<OnboardingTaskTemplateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateTemplate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetOnboardingTaskTemplateActiveCommand(id, IsActive: false), ct));

    [HttpPost("templates/seed-defaults")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SeedDefaultTemplates(CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SeedDefaultOnboardingTemplatesCommand(), ct));

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<OnboardingCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListOnboardingCategoriesQuery(), ct));

    [HttpPost("categories")]
    [ProducesResponseType<OnboardingCategoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCategory([FromBody] SaveCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateOnboardingCategoryCommand(request.Name, request.SortOrder), ct));

    [HttpPut("categories/" + ApiRoutes.IdParam)]
    [ProducesResponseType<OnboardingCategoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] SaveCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateOnboardingCategoryCommand(id, request.Name, request.SortOrder, request.IsActive), ct));

    [HttpDelete("categories/" + ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteOnboardingCategoryCommand(id), ct));

    [HttpGet("template-sets")]
    [ProducesResponseType<IReadOnlyList<OnboardingTemplateSetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTemplateSets(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListOnboardingTemplateSetsQuery(), ct));

    [HttpPost("template-sets")]
    [ProducesResponseType<OnboardingTemplateSetDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTemplateSet([FromBody] CreateTemplateSetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateOnboardingTemplateSetCommand(request.Stage, request.Name, request.TemplateIds), ct));

    [HttpPut("template-sets/" + ApiRoutes.IdParam)]
    [ProducesResponseType<OnboardingTemplateSetDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTemplateSet(Guid id, [FromBody] UpdateTemplateSetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateOnboardingTemplateSetCommand(id, request.Name, request.TemplateIds, request.IsActive), ct));

    /// <summary>Starts a named template set for an employee from their profile (once per set).</summary>
    [HttpPost("employees/{employeeId:guid}/start-process")]
    [ProducesResponseType<IReadOnlyList<EmployeeOnboardingTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartProcess(Guid employeeId, [FromBody] StartProcessRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new StartProcessCommand(employeeId, request.TemplateSetId, request.ReferenceDate), ct));

    [HttpGet("employees/{employeeId:guid}/category-progress")]
    [ProducesResponseType<IReadOnlyList<OnboardingCategoryProgressDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CategoryProgress(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeCategoryProgressQuery(employeeId), ct));

    [HttpGet("employees/{employeeId:guid}/buddy")]
    [ProducesResponseType<OnboardingBuddyAssignmentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBuddy(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetOnboardingBuddyQuery(employeeId), ct));

    [HttpPut("employees/{employeeId:guid}/buddy")]
    [ProducesResponseType<OnboardingBuddyAssignmentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveBuddy(Guid employeeId, [FromBody] SaveOnboardingBuddyRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SaveOnboardingBuddyCommand(employeeId, request.BuddyEmployeeId, request.StartDate, request.EndDate), ct));

    /// <summary>Every pending task across the tenant, soonest due first.</summary>
    [HttpGet("tasks/pending")]
    [ProducesResponseType<IReadOnlyList<OnboardingChecklistItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPendingOnboardingTasksQuery(), ct));

    [HttpGet("employees/{employeeId:guid}/tasks")]
    [ProducesResponseType<IReadOnlyList<OnboardingChecklistItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForEmployee(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeOnboardingTasksQuery(employeeId), ct));

    /// <summary>Manually (re)creates a stage's checklist for an employee - e.g. employees hired before the module existed.</summary>
    [HttpPost("employees/{employeeId:guid}/tasks")]
    [ProducesResponseType<IReadOnlyList<EmployeeOnboardingTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Materialize(Guid employeeId, [FromBody] MaterializeTasksRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MaterializeTasksForEmployeeCommand(employeeId, request.Stage, request.ReferenceDate), ct));

    [HttpPost("tasks/" + ApiRoutes.IdParam + "/complete")]
    [ProducesResponseType<EmployeeOnboardingTaskDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CompleteTaskCommand(id), ct));

    [HttpPost("tasks/" + ApiRoutes.IdParam + "/waive")]
    [ProducesResponseType<EmployeeOnboardingTaskDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Waive(Guid id, [FromBody] WaiveTaskRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WaiveTaskCommand(id, request.Reason), ct));
}

/// <summary>Self-service: the caller's own onboarding/offboarding checklist ("Kendi görevlerim").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/onboarding/me")]
[Authorize]
public sealed class MyOnboardingController : ApiControllerBase
{
    [HttpGet("tasks")]
    [ProducesResponseType<IReadOnlyList<OnboardingChecklistItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyTasks(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyOnboardingTasksQuery(), ct));

    [HttpPost("tasks/" + ApiRoutes.IdParam + "/complete")]
    [ProducesResponseType<EmployeeOnboardingTaskDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteMyTask(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CompleteMyTaskCommand(id), ct));
}

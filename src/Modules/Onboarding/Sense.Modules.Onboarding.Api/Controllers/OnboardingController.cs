using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Onboarding.Api.Controllers;

public sealed record SaveTaskTemplateRequest(OnboardingStage Stage, string Title, string ResponsibleRoleCode, int DueDateOffsetDays, bool IsMandatory);

public sealed record MaterializeTasksRequest(OnboardingStage Stage, DateOnly ReferenceDate);

public sealed record WaiveTaskRequest(string Reason);

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
            request.Stage, request.Title, request.ResponsibleRoleCode, request.DueDateOffsetDays, request.IsMandatory), ct));

    /// <summary>Stage is fixed at creation; the request's Stage is ignored here.</summary>
    [HttpPut("templates/" + ApiRoutes.IdParam)]
    [ProducesResponseType<OnboardingTaskTemplateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] SaveTaskTemplateRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateOnboardingTaskTemplateCommand(
            id, request.Title, request.ResponsibleRoleCode, request.DueDateOffsetDays, request.IsMandatory), ct));

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

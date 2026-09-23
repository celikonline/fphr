using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Training.Application.Enrollments;
using Sense.Modules.Training.Contracts;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Training.Api;

public sealed record CreateEnrollmentRequest(Guid TrainingProgramId, Guid EmployeeId);

public sealed record CompleteEnrollmentRequest(string? CertificateNumber);

public sealed record CancelEnrollmentRequest(string? Reason);

/// <summary>Eğitim programı katılımcı kayıtları (roadmap #72: ön koşul tamamlanmadan kayıt reddedilir - bkz. CreateEnrollmentHandler).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/training/enrollments")]
[Authorize]
public sealed class EnrollmentsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<EnrollmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? trainingProgramId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEnrollmentsQuery(trainingProgramId), ct));

    /// <summary>"Eğitimlerim": çağıranın kendi katılım kayıtları (training.my.enrollments).</summary>
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<MyTrainingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMy(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyTrainingsQuery(), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<EnrollmentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEnrollmentQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateEnrollmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateEnrollmentCommand(request.TrainingProgramId, request.EmployeeId), ct));

    [HttpPost(ApiRoutes.IdParam + "/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteEnrollmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CompleteEnrollmentCommand(id, request.CertificateNumber), ct));

    [HttpPost(ApiRoutes.IdParam + "/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelEnrollmentRequest? request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CancelEnrollmentCommand(id, request?.Reason), ct));
}

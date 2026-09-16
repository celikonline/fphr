using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

public sealed record CreateReviewCycleRequest(
    string Name, DateOnly PeriodStart, DateOnly PeriodEnd, ReviewKind Kind, RatingScale RatingScale, CommentMode DefaultCommentMode,
    EvaluatorLabelMode EvaluatorLabelMode, bool ReportShowPhoto, bool EmployeeCanViewResults, bool EmployeeCanDownloadPdf,
    GoalEntryBy GoalEntryBy, decimal? CompetencyWeight,
    IReadOnlyList<RelationConfigInputDto> RelationConfigs, IReadOnlyList<CompetencyInputDto> Competencies);

public sealed record UpdateReviewCycleDraftRequest(
    string Name, DateOnly PeriodStart, DateOnly PeriodEnd, RatingScale RatingScale, CommentMode DefaultCommentMode,
    EvaluatorLabelMode EvaluatorLabelMode, bool ReportShowPhoto, bool EmployeeCanViewResults, bool EmployeeCanDownloadPdf,
    GoalEntryBy GoalEntryBy, decimal? CompetencyWeight,
    IReadOnlyList<RelationConfigInputDto> RelationConfigs, IReadOnlyList<CompetencyInputDto> Competencies);

public sealed record AddParticipantsRequest(IReadOnlyList<AddParticipantInputDto> Participants);

/// <summary>Performans değerlendirme dönemleri (Uygulamalar → Performans, admin taraf).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/cycles")]
[Authorize]
public sealed class ReviewCyclesController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>Lists review cycles (grid standardı).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReviewCycleSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => FromResult(await Dispatcher.Query(new ListReviewCyclesQuery(), ct));

    /// <summary>Değerlendirme detayı (config + katılımcı grid).</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<ReviewCycleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Query(new GetReviewCycleQuery(id), ct));

    /// <summary>Sihirbaz "Kaydet ve bitir": Genel bilgiler + Yetkinlik + Sorular + Erişim tek çağrıda.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateReviewCycleRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateReviewCycleCommand(
                request.Name, request.PeriodStart, request.PeriodEnd, request.Kind, request.RatingScale, request.DefaultCommentMode,
                request.EvaluatorLabelMode, request.ReportShowPhoto, request.EmployeeCanViewResults, request.EmployeeCanDownloadPdf,
                request.GoalEntryBy, request.CompetencyWeight, request.RelationConfigs, request.Competencies),
            ct));

    /// <summary>Taslak durumdayken sihirbaz adımlarını yeniden düzenler.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] UpdateReviewCycleDraftRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateReviewCycleDraftCommand(
                id, request.Name, request.PeriodStart, request.PeriodEnd, request.RatingScale, request.DefaultCommentMode,
                request.EvaluatorLabelMode, request.ReportShowPhoto, request.EmployeeCanViewResults, request.EmployeeCanDownloadPdf,
                request.GoalEntryBy, request.CompetencyWeight, request.RelationConfigs, request.Competencies),
            ct));

    /// <summary>Katılımcılar adımı: çalışan + kategori bazlı değerlendirici ekler.</summary>
    [HttpPost(ApiRoutes.IdParam + "/participants")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddParticipants(Guid id, [FromBody] AddParticipantsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddParticipantsCommand(id, request.Participants), ct));

    /// <summary>Taslak → Yayınlandı: formları değerlendiricilere açar.</summary>
    [HttpPost(ApiRoutes.IdParam + "/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new PublishReviewCycleCommand(id), ct));

    /// <summary>"Değerlendirmeyi tamamla": minimumu sağlayan katılımcıları kapatır.</summary>
    [HttpPost(ApiRoutes.IdParam + "/force-complete")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForceComplete(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new ForceCompleteReviewCycleCommand(id), ct));

    /// <summary>"Detaylı rapor" / "Özet rapor" / "Bireysel analiz raporunu indir" - canlı üretilen PDF.</summary>
    [HttpGet(ApiRoutes.IdParam + "/participants/{participantId:guid}/report")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    public async Task<IActionResult> DownloadReport(
        Guid id, Guid participantId, [FromQuery] Guid participantEmployeeId, [FromQuery] ReviewReportKind kind, CancellationToken ct)
    {
        var result = await Dispatcher.Query(
            new GenerateReviewReportQuery(id, participantId, participantEmployeeId, kind, CurrentUser.EmployeeId ?? Guid.Empty), ct);
        return result.IsSuccess ? File(result.Value.Content, MediaTypes.Pdf, result.Value.FileName) : Problem(result.Error);
    }
}

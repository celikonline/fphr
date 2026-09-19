using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Absences;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

/// <summary>Self-service leave ("Kendi izinlerim").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/me")]
[Authorize]
public sealed class MyLeaveController : ApiControllerBase
{
    [HttpGet("balances")]
    [ProducesResponseType<EmployeeLeaveBalancesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Balances(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyLeaveBalancesQuery(), ct));

    [HttpGet("ledger")]
    [ProducesResponseType<IReadOnlyList<LeaveLedgerEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ledger([FromQuery] Guid? leaveTypeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyLeaveLedgerQuery(leaveTypeId), ct));

    // senseik-039: burada artık .Localize(localizer) ÇAĞRILMIYOR. Localizer, HTTP Accept-Language başlığından
    // (tarayıcı/işletim sistemi diline göre) kültür belirliyor — bu, kullanıcının uygulama içi dil tercihiyle
    // (react-i18next, localStorage'da tutulur) ASLA garanti bağlantılı değil. İki tercih uyuşmadığında bu uç nokta
    // "+ Talep" açılır listesinde İngilizce isim döndürüyordu, oysa aynı ham anahtarlar (leave.type.annual vb.)
    // Ayarlar/takvim/izin defteri gibi her yerde frontend'in kendi `useLeaveName()` hook'uyla (uygulama dili baz
    // alınarak) doğru şekilde çözülüyordu. Kalıcı düzeltme: isimleri ham anahtar olarak bırak — tıpkı
    // LeaveRuleSet.types[].name ve EmployeeLeaveBalances.balances[].leaveTypeName gibi — ve çözümlemeyi tek
    // otorite olan frontend i18n'e devret (bkz. web/src/pages/leave/my-leave.tsx buildRequestableTypes).

    /// <summary>senseik-036 md.7: hastalık izni 6 hafta devamsızlık eşiği sayacı (4857 md.25/I-b, bilgi amaçlı).</summary>
    [HttpGet("sick-leave-absence-status")]
    [ProducesResponseType<SickLeaveAbsenceStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SickLeaveAbsenceStatus(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMySickLeaveAbsenceStatusQuery(), ct));
    [HttpGet("leave-types")]
    [ProducesResponseType<IReadOnlyList<RequestableLeaveTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LeaveTypes(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyRequestableLeaveTypesQuery(), ct));

    [HttpGet("requests")]
    [ProducesResponseType<IReadOnlyList<LeaveRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests([FromServices] IErrorLocalizer localizer, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult((await Dispatcher.Query(new ListMyLeaveRequestsQuery(take), ct)).Localize(localizer));

    [HttpGet("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType<LeaveRequestDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestDetail(Guid id, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetMyLeaveRequestQuery(id), ct)).Localize(localizer));

    [HttpGet("requests/preview")]
    [ProducesResponseType<LeaveRequestPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromQuery] Guid leaveTypeId, [FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromQuery] Guid? editedRequestId,
        [FromQuery] TimeOnly? startTime, [FromQuery] TimeOnly? endTime, [FromQuery] HalfDayPeriod? halfDayPeriod, [FromServices] IErrorLocalizer localizer,
        CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new PreviewMyLeaveRequestQuery(leaveTypeId, start, end, editedRequestId, startTime, endTime, halfDayPeriod), ct))
            .Localize(localizer));

    [HttpPost("requests")]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] LeaveRequestBody request, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Send(
            new SubmitLeaveRequestCommand(
                request.LeaveTypeId, request.StartDate, request.EndDate, request.Description, request.PreviousRequestId,
                request.StartTime, request.EndTime, request.HalfDayPeriod), ct))
            .Localize(localizer));

    [HttpPut("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePending(Guid id, [FromBody] LeaveRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdatePendingLeaveRequestCommand(
                id, request.LeaveTypeId, request.StartDate, request.EndDate, request.Description, request.StartTime, request.EndTime, request.HalfDayPeriod),
            ct));

    [HttpPost("requests/" + ApiRoutes.IdParam + "/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WithdrawLeaveRequestCommand(id), ct));
}

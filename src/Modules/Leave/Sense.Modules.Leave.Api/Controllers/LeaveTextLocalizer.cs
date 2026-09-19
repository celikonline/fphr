using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

public sealed record LeavePreviewWarning(string Code, string Message);

public sealed record LeaveRequestPreviewResponse(
    decimal Days, int WeeklyRestDays, int PublicHolidayDays, IReadOnlyList<LeavePreviewWarning> Warnings, TeamOverlapDto? TeamOverlap = null);

/// <summary>Seeded rule sets/types store resource keys as names ("leave.type.annual"); the API returns them translated.</summary>
internal static class LeaveTextLocalizer
{
    public static Result<IReadOnlyList<LeaveRequestDto>> Localize(this Result<IReadOnlyList<LeaveRequestDto>> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result
            : Result.Success<IReadOnlyList<LeaveRequestDto>>(
                result.Value.Select(r => r with { LeaveTypeName = r.LeaveTypeName is null ? null : localizer.MessageOrText(r.LeaveTypeName) }).ToList());

    public static Result<LeaveRequestDto> Localize(this Result<LeaveRequestDto> result, IErrorLocalizer localizer) =>
        result.IsFailure || result.Value.LeaveTypeName is null
            ? result
            : result.Value with { LeaveTypeName = localizer.MessageOrText(result.Value.LeaveTypeName) };

    // senseik-039: RequestableLeaveTypeDto için burada bir Localize() overload'u VARDI ve kaldırıldı — Accept-Language
    // başlığına göre (kullanıcının uygulama içi dil tercihinden bağımsız) sunucu tarafında çeviriyordu, bu da
    // "+ Talep" açılır listesinin diğer izin ekranlarıyla (frontend i18n'e dayanan) tutarsız görünmesine yol
    // açıyordu. Bkz. MyLeaveController.LeaveTypes ve web/src/pages/leave/my-leave.tsx buildRequestableTypes.

    public static Result<LeaveRequestDetailDto> Localize(this Result<LeaveRequestDetailDto> result, IErrorLocalizer localizer) =>
        result.IsFailure || result.Value.LeaveTypeName is null
            ? result
            : result.Value with { LeaveTypeName = localizer.MessageOrText(result.Value.LeaveTypeName) };

    public static Result<IReadOnlyList<LeaveCalendarEntryDto>> Localize(this Result<IReadOnlyList<LeaveCalendarEntryDto>> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result
            : Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>(
                result.Value.Select(e => e with { LeaveTypeName = e.LeaveTypeName is null ? null : localizer.MessageOrText(e.LeaveTypeName) }).ToList());

    public static Result<LeaveRuleSetAssignmentResponse> Localize(this Result<LeaveRuleSetAssignmentResult> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result.Error
            : new LeaveRuleSetAssignmentResponse(
                result.Value.AssignedCount,
                result.Value.Warnings
                    .Select(w => new LeaveRuleSetAssignmentWarningResponse(
                        w.EmployeeId, w.Code, localizer.Message(Error.Validation(w.Code, ("leaveType", localizer.MessageOrText(w.LeaveTypeName))))))
                    .ToList());

    /// <summary>Warnings stay codes (web can branch on them) but the message is localized alongside.</summary>
    public static Result<LeaveRequestPreviewResponse> Localize(this Result<LeaveRequestPreviewDto> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result.Error
            : new LeaveRequestPreviewResponse(
                result.Value.Days, result.Value.WeeklyRestDays, result.Value.PublicHolidayDays,
                result.Value.Warnings.Select(error => new LeavePreviewWarning(error.Code, localizer.Message(error))).ToList(), result.Value.TeamOverlap);
}

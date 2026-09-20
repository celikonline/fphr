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

public sealed record CloneLeaveRuleSetRequest(string Name);

public sealed record RenameLeaveRuleSetRequest(string Name);

public sealed record AssignLeaveRuleSetRequest(IReadOnlyList<Guid> EmployeeIds);

public sealed record LeaveRuleSetAssignmentWarningResponse(Guid EmployeeId, string Code, string Message);

public sealed record LeaveRuleSetAssignmentResponse(int AssignedCount, IReadOnlyList<LeaveRuleSetAssignmentWarningResponse> Warnings);

public sealed record SetLeaveTypeDisabledRequest(bool Disabled);

/// <summary>Self-service submit/edit body; <c>PreviousRequestId</c> (submit only) re-submits the caller's rejected request.</summary>
public sealed record LeaveRequestBody(
    Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, Guid? PreviousRequestId = null,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null, Guid? SubstituteEmployeeId = null);

public sealed record CancelLeaveRequestBody(string Reason);

public sealed record LeaveRequestOnBehalfBody(
    Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, bool BypassApproval,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null, Guid? SubstituteEmployeeId = null);

public sealed record AdjustLeaveBalanceRequest(Guid LeaveTypeId, decimal Days, DateOnly EffectiveDate, string Reason);

public sealed record OvertimeRequestBody(
    DateOnly WorkDate, TimeOnly StartTime, int DurationMinutes, string? Description, OvertimePayRateKind PayRateKind = OvertimePayRateKind.Rate50);

public sealed record ChangeOvertimePayRateKindRequest(OvertimePayRateKind PayRateKind);

public sealed record RecordOvertimeConsentRequest(DateOnly GivenAt, OvertimeConsentSource Source, bool CoversNightWork, Guid? DocumentId);

public sealed record RevokeOvertimeConsentRequest(DateOnly NoticeAt);

public sealed record ConvertOvertimeToTimeOffRequest(decimal? Rate, OvertimeMinutesBasis MinutesBasis, Guid? LeaveTypeId, string? Note);

public sealed record ConvertOvertimeToPayRequest(OvertimeMinutesBasis MinutesBasis, string? Note);

public sealed record AbsenceIdResponse(Guid Id);

public sealed record RecordAbsenceRequest(DateOnly Date, AbsenceKind Kind, decimal? Hours, string? Note);

public sealed record ReverseAbsenceRequest(string Reason);

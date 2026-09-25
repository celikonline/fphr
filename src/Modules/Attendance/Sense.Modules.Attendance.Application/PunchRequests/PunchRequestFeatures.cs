using FluentValidation;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.PunchRequests;

internal static class PunchRequestPermissions
{
    public const string My = "attendance.my.punchrequest";
    public const string Manage = "attendance.punchrequest.manage";
}

/// <summary>Eksik kayıt talebi satırı (web/mobil sözleşmesi); <see cref="EmployeeName"/> sunucuda çalışan dizininden her zaman doldurulur.</summary>
public sealed record PunchRequestDto(
    Guid Id, Guid EmployeeId, string? EmployeeName, DateOnly WorkDate, DateTime OccurredAtUtc, PunchDirection Direction, string Reason,
    PunchRequestStatus Status, DateTime? DecidedAtUtc, Guid? ApprovalRequestId);

internal static class PunchRequestRows
{
    public static async Task<IReadOnlyList<PunchRequestDto>> BuildAsync(
        Guid tenantId, IEmployeeDirectory employees, IReadOnlyList<PunchRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var contacts = await employees.GetContactsAsync(tenantId, requests.Select(r => r.EmployeeId).Distinct().ToList(), cancellationToken);
        var names = contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());
        return requests.Select(r => ToDto(r, names.GetValueOrDefault(r.EmployeeId))).ToList();
    }

    public static PunchRequestDto ToDto(PunchRequest r, string? employeeName) =>
        new(r.Id, r.EmployeeId, employeeName, r.WorkDate, r.OccurredAtUtc, r.Direction, r.Reason, r.Status, r.DecidedAtUtc, r.ApprovalRequestId);
}

/// <summary>Çalışan unuttuğu giriş/çıkışı için gerekçeli talep açar ve "AttendancePunch" onay akışını başlatır.</summary>
[RequiresPermission(PunchRequestPermissions.My)]
public sealed record CreatePunchRequestCommand(DateOnly WorkDate, DateTime OccurredAtUtc, PunchDirection Direction, string Reason) : ICommand<PunchRequestDto>;

public sealed class CreatePunchRequestValidator : AbstractValidator<CreatePunchRequestCommand>
{
    public CreatePunchRequestValidator()
    {
        RuleFor(x => x.Direction).Must(d => d is PunchDirection.In or PunchDirection.Out).WithMessage(AttendanceDomainErrors.PunchRequestDirectionInvalid);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PunchRequest.ReasonMaxLength);
    }
}

public sealed class CreatePunchRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, IPunchRequestRepository requests, IMonthlyTimesheetApprovalRepository approvals,
    IWorkflowService workflow, ITenantLocalClock clock, IEmployeeDirectory employees) : ICommandHandler<CreatePunchRequestCommand, PunchRequestDto>
{
    public async Task<Result<PunchRequestDto>> Handle(CreatePunchRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var occurred = command.OccurredAtUtc.Kind == DateTimeKind.Utc ? command.OccurredAtUtc : DateTime.SpecifyKind(command.OccurredAtUtc, DateTimeKind.Utc);
        var now = await clock.NowAsync(tenant.TenantId, cancellationToken);
        if (occurred > now.Utc)
        {
            return Error.Validation(AttendanceDomainErrors.PunchInFuture);
        }

        // K6: onaylı aylık puantajı olan ay kilitlidir.
        var month = MonthlyTimesheetApproval.NormalizeToMonthStart(command.WorkDate);
        if (await approvals.FindAsync(employeeId, month, cancellationToken) is not null)
        {
            return Error.Conflict(AttendanceErrors.PunchRequestPeriodLocked);
        }

        var sameDay = await requests.ListForEmployeeAsync(employeeId, command.WorkDate, command.WorkDate, cancellationToken);
        if (sameDay.Any(r => r.Status == PunchRequestStatus.Pending && r.OccurredAtUtc == occurred && r.Direction == command.Direction))
        {
            return Error.Conflict(AttendanceErrors.PunchRequestDuplicate);
        }

        var created = PunchRequest.Create(tenant.TenantId, employeeId, command.WorkDate, occurred, command.Direction, command.Reason);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var request = created.Value;
        requests.Add(request);
        var approval = await workflow.StartAsync(
            ApprovalRequestTypes.AttendancePunch, request.Id, employeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        request.AttachApproval(approval.Value);
        return (await PunchRequestRows.BuildAsync(tenant.TenantId, employees, [request], cancellationToken))[0];
    }
}

/// <summary>Çalışan, yalnız bekleyen kendi talebini iptal eder; bağlı onay süreci de iptal edilir.</summary>
[RequiresPermission(PunchRequestPermissions.My)]
public sealed record CancelPunchRequestCommand(Guid Id) : ICommand;

public sealed class CancelPunchRequestHandler(ICurrentUser currentUser, IPunchRequestRepository requests, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<CancelPunchRequestCommand>
{
    public async Task<Result> Handle(CancelPunchRequestCommand command, CancellationToken cancellationToken)
    {
        var found = await requests.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var request = found.Value;
        if (currentUser.EmployeeId is not { } employeeId || request.EmployeeId != employeeId)
        {
            return Error.Forbidden(AttendanceErrors.PunchRequestNotOwner);
        }

        var cancelled = request.Cancel(clock.GetUtcNow().UtcDateTime);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        return request.ApprovalRequestId is { } approvalId
            ? await workflow.CancelAsync(approvalId, employeeId, cancellationToken)
            : Result.Success();
    }
}

[RequiresPermission(PunchRequestPermissions.My)]
public sealed record ListMyPunchRequestsQuery : IQuery<IReadOnlyList<PunchRequestDto>>;

public sealed class ListMyPunchRequestsHandler(ITenantContext tenant, ICurrentUser currentUser, IPunchRequestRepository requests, IEmployeeDirectory employees)
    : IQueryHandler<ListMyPunchRequestsQuery, IReadOnlyList<PunchRequestDto>>
{
    public async Task<Result<IReadOnlyList<PunchRequestDto>>> Handle(ListMyPunchRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var mine = await requests.ListForEmployeeAsync(employeeId, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken);
        return Result.Success(await PunchRequestRows.BuildAsync(tenant.TenantId, employees, mine, cancellationToken));
    }
}

/// <summary>Yönetici/İK listesi: veri kapsamındaki çalışanların talepleri, isteğe bağlı durum süzgeciyle.</summary>
[RequiresPermission(PunchRequestPermissions.Manage)]
public sealed record ListPunchRequestsQuery(PunchRequestStatus? Status) : IQuery<IReadOnlyList<PunchRequestDto>>;

public sealed class ListPunchRequestsHandler(ITenantContext tenant, IDataScopeService dataScope, IPunchRequestRepository requests, IEmployeeDirectory employees)
    : IQueryHandler<ListPunchRequestsQuery, IReadOnlyList<PunchRequestDto>>
{
    public async Task<Result<IReadOnlyList<PunchRequestDto>>> Handle(ListPunchRequestsQuery query, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(PunchRequestPermissions.Manage, cancellationToken);
        var all = await requests.ListAsync(query.Status, cancellationToken);
        var scoped = visible is null ? all : all.Where(r => visible.Contains(r.EmployeeId)).ToList();
        return Result.Success(await PunchRequestRows.BuildAsync(tenant.TenantId, employees, scoped, cancellationToken));
    }
}

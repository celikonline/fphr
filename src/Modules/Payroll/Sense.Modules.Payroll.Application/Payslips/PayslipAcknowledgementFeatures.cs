using FluentValidation;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Payslips;

/// <summary>One of the caller's own published payslips with its acknowledgement/objection state (E2 "Bordrolarım").</summary>
public sealed record MyPayslipDto(
    Guid PayslipId,
    Guid PeriodId,
    int Year,
    int Month,
    decimal GrossAmount,
    decimal NetAmount,
    DateTimeOffset PublishedAt,
    DateTimeOffset? AcknowledgedAt,
    PayslipObjectionStatus ObjectionStatus,
    string? ObjectionReason,
    DateTimeOffset? ObjectedAt,
    DateTimeOffset? ObjectionDecidedAt);

/// <summary>The Workflow approval request started for an objection.</summary>
public sealed record PayslipObjectionSubmittedDto(Guid Id);

/// <summary>
/// "Kendi bordrom" access rule shared by every E2 self-service handler: the payslip must belong to the caller's own
/// employee record (anything else is reported as not found, never as forbidden, so ids of others cannot be probed) and
/// it must be published - period paid or locked and <see cref="Payslip.PublishedAt"/> set. A reopened period hides its
/// payslips until it is paid again.
/// </summary>
internal static class MyPayslips
{
    public static bool IsPublished(Payslip payslip, PayrollPeriod period) =>
        payslip.PublishedAt is not null && period.Status is PayrollPeriodStatus.Paid or PayrollPeriodStatus.Locked;

    public static async Task<Result<(Payslip Payslip, PayrollPeriod Period)>> FindOwnPublishedAsync(
        ICurrentUser currentUser, IPayslipRepository payslips, IPayrollPeriodRepository periods, Guid payslipId, CancellationToken ct)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(PayrollDomainErrors.NoEmployeeForUser);
        }

        var payslip = await payslips.FindByIdAsync(payslipId, ct);
        if (payslip is null || payslip.EmployeeId != employeeId)
        {
            return Error.NotFound(PayrollDomainErrors.PayslipNotFound);
        }

        var period = await periods.FindByIdAsync(payslip.PayrollPeriodId, ct);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayslipNotFound);
        }

        return IsPublished(payslip, period) ? (payslip, period) : Error.Validation(PayrollDomainErrors.PayslipNotPublished);
    }

    public static MyPayslipDto ToDto(Payslip p, PayrollPeriod period) =>
        new(p.Id, period.Id, period.Year, period.Month, p.GrossAmount, p.NetAmount, p.PublishedAt!.Value, p.AcknowledgedAt,
            p.ObjectionStatus, p.ObjectionReason, p.ObjectedAt, p.ObjectionDecidedAt);
}

/// <summary>The caller's published payslips, newest period first.</summary>
[RequiresPermission("payroll.my.payslip")]
public sealed record ListMyPayslipsQuery : IQuery<IReadOnlyList<MyPayslipDto>>;

public sealed class ListMyPayslipsHandler(ICurrentUser currentUser, IPayslipRepository payslips, IPayrollPeriodRepository periods)
    : IQueryHandler<ListMyPayslipsQuery, IReadOnlyList<MyPayslipDto>>
{
    public async Task<Result<IReadOnlyList<MyPayslipDto>>> Handle(ListMyPayslipsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(PayrollDomainErrors.NoEmployeeForUser);
        }

        var mine = await payslips.ListByEmployeeAsync(employeeId, cancellationToken);
        if (mine.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyPayslipDto>>([]);
        }

        var periodsById = (await periods.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        IReadOnlyList<MyPayslipDto> list = mine
            .Where(p => p.EmployeeId == employeeId)
            .Select(p => (Payslip: p, Period: periodsById.GetValueOrDefault(p.PayrollPeriodId)))
            .Where(x => x.Period is not null && MyPayslips.IsPublished(x.Payslip, x.Period))
            .OrderByDescending(x => x.Period!.Year)
            .ThenByDescending(x => x.Period!.Month)
            .Select(x => MyPayslips.ToDto(x.Payslip, x.Period!))
            .ToList();
        return Result.Success(list);
    }
}

/// <summary>The ücret pusulası PDF of one of the caller's own published payslips.</summary>
[RequiresPermission("payroll.my.payslip")]
public sealed record ExportMyPayslipDocumentQuery(Guid PayslipId) : IQuery<ExportFileResult>;

public sealed class ExportMyPayslipDocumentHandler(
    ICurrentUser currentUser, IPayslipRepository payslips, IPayrollPeriodRepository periods, IPayslipDocumentProvider documents, ITenantContext tenant)
    : IQueryHandler<ExportMyPayslipDocumentQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(ExportMyPayslipDocumentQuery query, CancellationToken cancellationToken)
    {
        var own = await MyPayslips.FindOwnPublishedAsync(currentUser, payslips, periods, query.PayslipId, cancellationToken);
        if (own.IsFailure)
        {
            return own.Error;
        }

        var file = await documents.RenderPdfAsync(tenant.TenantId, own.Value.Payslip.Id, cancellationToken);
        return file is null ? Error.NotFound(PayrollDomainErrors.PayslipNotFound) : file;
    }
}

/// <summary>
/// K4 "imza": the employee acknowledges one of their own published payslips - server time, the request IP and the
/// client (User-Agent, passed by the API layer) are stored. Only once per payslip version.
/// </summary>
[RequiresPermission("payroll.my.payslip")]
public sealed record AcknowledgeMyPayslipCommand(Guid PayslipId, string? UserAgent) : ICommand;

public sealed class AcknowledgeMyPayslipHandler(
    ICurrentUser currentUser, IPayslipRepository payslips, IPayrollPeriodRepository periods, TimeProvider clock)
    : ICommandHandler<AcknowledgeMyPayslipCommand>
{
    public async Task<Result> Handle(AcknowledgeMyPayslipCommand command, CancellationToken cancellationToken)
    {
        var own = await MyPayslips.FindOwnPublishedAsync(currentUser, payslips, periods, command.PayslipId, cancellationToken);
        return own.IsFailure
            ? own.Error
            : own.Value.Payslip.Acknowledge(clock.GetUtcNow(), currentUser.IpAddress, command.UserAgent);
    }
}

/// <summary>
/// E2 itiraz: the employee objects to one of their own published, unacknowledged payslips; a Workflow
/// "PayslipObjection" approval starts with the employee as requester (approval = objection accepted, spec §5.5).
/// </summary>
[RequiresPermission("payroll.my.payslip")]
public sealed record ObjectToMyPayslipCommand(Guid PayslipId, string Reason, string? UserAgent) : ICommand<PayslipObjectionSubmittedDto>;

public sealed class ObjectToMyPayslipValidator : AbstractValidator<ObjectToMyPayslipCommand>
{
    public ObjectToMyPayslipValidator()
    {
        RuleFor(x => x.PayslipId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PayrollLimits.ObjectionReasonMaxLength);
    }
}

public sealed class ObjectToMyPayslipHandler(
    ICurrentUser currentUser, IPayslipRepository payslips, IPayrollPeriodRepository periods, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<ObjectToMyPayslipCommand, PayslipObjectionSubmittedDto>
{
    public async Task<Result<PayslipObjectionSubmittedDto>> Handle(ObjectToMyPayslipCommand command, CancellationToken cancellationToken)
    {
        var own = await MyPayslips.FindOwnPublishedAsync(currentUser, payslips, periods, command.PayslipId, cancellationToken);
        if (own.IsFailure)
        {
            return own.Error;
        }

        var payslip = own.Value.Payslip;
        var filed = payslip.FileObjection(command.Reason, clock.GetUtcNow());
        if (filed.IsFailure)
        {
            return filed.Error;
        }

        var approval = await workflow.StartAsync(
            ApprovalRequestTypes.PayslipObjection, payslip.Id, payslip.EmployeeId, null, currentUser.IpAddress, command.UserAgent, cancellationToken);
        if (approval.IsFailure)
        {
            // Nothing is saved on failure (the command transaction rolls back), but keep the tracked entity consistent too.
            payslip.WithdrawUnsubmittedObjection();
            return approval.Error;
        }

        payslip.AttachObjectionApproval(approval.Value);
        return new PayslipObjectionSubmittedDto(approval.Value);
    }
}

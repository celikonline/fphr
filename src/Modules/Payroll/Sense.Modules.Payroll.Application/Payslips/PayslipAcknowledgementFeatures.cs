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

/// <summary>
/// One of the caller's own published payslips with its acknowledgement/objection state (E2 "Bordrolarım"), in the F11
/// web/mobile contract's names. <see cref="ObjectionStatus"/> is null (omitted) when no objection was filed.
/// </summary>
public sealed record MyPayslipDto(
    Guid PayslipId,
    Guid PeriodId,
    int Year,
    int Month,
    decimal GrossPay,
    decimal NetPay,
    string Currency,
    DateTimeOffset PublishedAt,
    DateTimeOffset? AcknowledgedAt,
    PayslipObjectionStatus? ObjectionStatus,
    string? ObjectionReason,
    DateTimeOffset? ObjectedAt,
    DateTimeOffset? ObjectionDecidedAt);

/// <summary>
/// "Kendi bordrom" access rule shared by every E2 self-service handler: the payslip must belong to the caller's own
/// employee record (anything else is reported as not found, never as forbidden, so ids of others cannot be probed) and
/// it must be published - period paid or locked and <see cref="Payslip.PublishedAt"/> set. A reopened period hides its
/// payslips until it is paid again.
/// </summary>
internal static class MyPayslips
{
    /// <summary>Payroll runs in TRY only (see BankPaymentFileBuilder / Expense payout reader).</summary>
    public const string PayrollCurrency = "TRY";

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

        return IsPublished(payslip, period) ? (payslip, period) : Error.Rule(PayrollDomainErrors.PayslipNotPublished);
    }

    public static MyPayslipDto ToDto(Payslip p, PayrollPeriod period) =>
        new(p.Id, period.Id, period.Year, period.Month, p.GrossAmount, p.NetAmount, PayrollCurrency, p.PublishedAt!.Value, p.AcknowledgedAt,
            p.ObjectionStatus == PayslipObjectionStatus.None ? null : p.ObjectionStatus, p.ObjectionReason, p.ObjectedAt, p.ObjectionDecidedAt);
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
/// client (User-Agent, passed by the API layer) are stored. Only once per payslip version; the change is flushed at once
/// so a concurrent acknowledgement/objection loses with 409 instead of overwriting the evidence. Returns the updated row.
/// </summary>
[RequiresPermission("payroll.my.payslip")]
public sealed record AcknowledgeMyPayslipCommand(Guid PayslipId, string? UserAgent) : ICommand<MyPayslipDto>;

public sealed class AcknowledgeMyPayslipHandler(
    ICurrentUser currentUser, IPayslipRepository payslips, IPayrollPeriodRepository periods, IPayslipChangeSaver saver, TimeProvider clock)
    : ICommandHandler<AcknowledgeMyPayslipCommand, MyPayslipDto>
{
    public async Task<Result<MyPayslipDto>> Handle(AcknowledgeMyPayslipCommand command, CancellationToken cancellationToken)
    {
        var own = await MyPayslips.FindOwnPublishedAsync(currentUser, payslips, periods, command.PayslipId, cancellationToken);
        if (own.IsFailure)
        {
            return own.Error;
        }

        var (payslip, period) = own.Value;
        var acknowledged = payslip.Acknowledge(clock.GetUtcNow(), currentUser.IpAddress, command.UserAgent);
        if (acknowledged.IsFailure)
        {
            return acknowledged.Error;
        }

        var saved = await saver.SaveAsync(cancellationToken);
        return saved.IsFailure ? saved.Error : MyPayslips.ToDto(payslip, period);
    }
}

/// <summary>
/// E2 itiraz: the employee objects to one of their own published, unacknowledged payslips; a Workflow
/// "PayslipObjection" approval starts with the employee as requester (approval = objection accepted, spec §5.5).
/// The pending objection is flushed before Workflow is called, so a lost race (409) never leaves an approval request
/// behind. Returns the updated row.
/// </summary>
[RequiresPermission("payroll.my.payslip")]
public sealed record ObjectToMyPayslipCommand(Guid PayslipId, string Reason, string? UserAgent) : ICommand<MyPayslipDto>;

public sealed class ObjectToMyPayslipValidator : AbstractValidator<ObjectToMyPayslipCommand>
{
    public ObjectToMyPayslipValidator()
    {
        RuleFor(x => x.PayslipId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PayrollLimits.ObjectionReasonMaxLength);
    }
}

public sealed class ObjectToMyPayslipHandler(
    ICurrentUser currentUser, IPayslipRepository payslips, IPayrollPeriodRepository periods, IWorkflowService workflow,
    IPayslipChangeSaver saver, TimeProvider clock)
    : ICommandHandler<ObjectToMyPayslipCommand, MyPayslipDto>
{
    public async Task<Result<MyPayslipDto>> Handle(ObjectToMyPayslipCommand command, CancellationToken cancellationToken)
    {
        var own = await MyPayslips.FindOwnPublishedAsync(currentUser, payslips, periods, command.PayslipId, cancellationToken);
        if (own.IsFailure)
        {
            return own.Error;
        }

        var (payslip, period) = own.Value;
        var filed = payslip.FileObjection(command.Reason, clock.GetUtcNow());
        if (filed.IsFailure)
        {
            return filed.Error;
        }

        var claimed = await saver.SaveAsync(cancellationToken);
        if (claimed.IsFailure)
        {
            return claimed.Error;
        }

        var approval = await workflow.StartAsync(
            ApprovalRequestTypes.PayslipObjection, payslip.Id, payslip.EmployeeId, null, currentUser.IpAddress, command.UserAgent, cancellationToken);
        if (approval.IsFailure)
        {
            // The claim above is already written in this transaction (which commits even on a failed result): undo it.
            payslip.WithdrawUnsubmittedObjection();
            var undone = await saver.SaveAsync(cancellationToken);
            return undone.IsFailure ? undone.Error : approval.Error;
        }

        payslip.AttachObjectionApproval(approval.Value);
        return MyPayslips.ToDto(payslip, period);
    }
}

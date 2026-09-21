using FluentValidation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Closing;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Closing;

/// <summary>One item of a period's closing checklist as the "Kapanış" section lists it.</summary>
public sealed record ClosingCheckDto(
    string Code,
    ClosingStage Stage,
    ClosingCheckKind Kind,
    ClosingCheckSeverity Severity,
    ClosingCheckStatus Status,
    string? Detail,
    string? WaiverReason,
    DateTimeOffset? DecidedAt,
    bool IsSatisfied);

internal static class ClosingCheckMapper
{
    public static ClosingCheckDto ToDto(PeriodClosingCheck c) =>
        new(c.Code, c.Stage, c.Kind, c.Severity, c.Status, c.Detail, c.WaiverReason, c.DecidedAt, c.IsSatisfied);

    /// <summary>The template as a not-yet-opened checklist: every item pending (reads never write).</summary>
    public static IReadOnlyList<ClosingCheckDto> Template() =>
        ClosingCheckCatalog.All.Select(d => new ClosingCheckDto(d.Code, d.Stage, d.Kind, d.Severity, ClosingCheckStatus.Pending, null, null, null, false)).ToList();
}

/// <summary>The period's checklist; a period whose checklist was never opened shows the template with every item pending.</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record GetClosingChecklistQuery(Guid PeriodId) : IQuery<IReadOnlyList<ClosingCheckDto>>;

public sealed class GetClosingChecklistHandler(IPayrollPeriodRepository periods, IPeriodClosingCheckRepository checks)
    : IQueryHandler<GetClosingChecklistQuery, IReadOnlyList<ClosingCheckDto>>
{
    public async Task<Result<IReadOnlyList<ClosingCheckDto>>> Handle(GetClosingChecklistQuery query, CancellationToken cancellationToken)
    {
        if (await periods.FindByIdAsync(query.PeriodId, cancellationToken) is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var existing = await checks.ListByPeriodAsync(query.PeriodId, cancellationToken);
        IReadOnlyList<ClosingCheckDto> list = existing.Count == 0
            ? ClosingCheckMapper.Template()
            : ClosingChecklistService.Order(existing).Select(ClosingCheckMapper.ToDto).ToList();
        return Result.Success(list);
    }
}

/// <summary>Opens the checklist if needed and re-runs the automatic rules ("Kuralları çalıştır").</summary>
[RequiresPermission("payroll.period.approve")]
public sealed record RunClosingChecksCommand(Guid PeriodId) : ICommand<IReadOnlyList<ClosingCheckDto>>;

public sealed class RunClosingChecksHandler(IPayrollPeriodRepository periods, ClosingChecklistService checklist)
    : ICommandHandler<RunClosingChecksCommand, IReadOnlyList<ClosingCheckDto>>
{
    public async Task<Result<IReadOnlyList<ClosingCheckDto>>> Handle(RunClosingChecksCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var list = await checklist.EnsureAsync(period, cancellationToken);
        await checklist.RunAutomaticAsync(period, list, cancellationToken);
        IReadOnlyList<ClosingCheckDto> dtos = list.Select(ClosingCheckMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>A person ticks a manual item off, with an optional note.</summary>
[RequiresPermission("payroll.period.approve")]
public sealed record CompleteClosingCheckCommand(Guid PeriodId, string Code, string? Note) : ICommand;

public sealed class CompleteClosingCheckValidator : AbstractValidator<CompleteClosingCheckCommand>
{
    public CompleteClosingCheckValidator()
    {
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(PayrollLimits.ClosingCheckTextMaxLength);
    }
}

public sealed class CompleteClosingCheckHandler(IPayrollPeriodRepository periods, ClosingChecklistService checklist, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<CompleteClosingCheckCommand>
{
    public async Task<Result> Handle(CompleteClosingCheckCommand command, CancellationToken cancellationToken)
    {
        var located = await ClosingCheckLocator.FindAsync(periods, checklist, command.PeriodId, command.Code, cancellationToken);
        return located.IsFailure ? located.Error : located.Value.Complete(command.Note, currentUser.UserId, clock.GetUtcNow());
    }
}

/// <summary>Lets the period move on despite a failed or pending item; the written reason is kept with who waived it and when.</summary>
[RequiresPermission("payroll.period.approve")]
public sealed record WaiveClosingCheckCommand(Guid PeriodId, string Code, string Reason) : ICommand;

public sealed class WaiveClosingCheckValidator : AbstractValidator<WaiveClosingCheckCommand>
{
    public WaiveClosingCheckValidator()
    {
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PayrollLimits.ClosingCheckTextMaxLength);
    }
}

public sealed class WaiveClosingCheckHandler(IPayrollPeriodRepository periods, ClosingChecklistService checklist, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<WaiveClosingCheckCommand>
{
    public async Task<Result> Handle(WaiveClosingCheckCommand command, CancellationToken cancellationToken)
    {
        var located = await ClosingCheckLocator.FindAsync(periods, checklist, command.PeriodId, command.Code, cancellationToken);
        return located.IsFailure ? located.Error : located.Value.Waive(command.Reason, currentUser.UserId, clock.GetUtcNow());
    }
}

internal static class ClosingCheckLocator
{
    /// <summary>The check with <paramref name="code"/>, opening the period's checklist first when it was never opened.</summary>
    public static async Task<Result<PeriodClosingCheck>> FindAsync(
        IPayrollPeriodRepository periods, ClosingChecklistService checklist, Guid periodId, string code, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(periodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var list = await checklist.EnsureAsync(period, cancellationToken);
        var check = list.FirstOrDefault(c => c.Code == code);
        return check is null ? Error.NotFound(PayrollDomainErrors.ClosingCheckNotFound) : check;
    }
}

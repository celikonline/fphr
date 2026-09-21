using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Severance;

/// <summary>One payment line with the deductions it was netted with.</summary>
public sealed record ExitSettlementLineDto(
    Guid Id,
    ExitPayKind Kind,
    string Label,
    decimal GrossAmount,
    bool SgkSubject,
    bool IncomeTaxSubject,
    bool StampTaxSubject,
    bool IsAutomatic,
    decimal SgkAmount,
    decimal IncomeTaxAmount,
    decimal StampTaxAmount,
    decimal NetAmount);

/// <summary>The "Çıkış hesabı" of one exit, as the employee card shows it: the frozen automatic amounts plus any lines added since, with the net.</summary>
public sealed record ExitSettlementDto(
    Guid Id,
    Guid EmployeeId,
    DateOnly TerminationDate,
    SeveranceCauseCategory CauseCategory,
    string Currency,
    int SeniorityDays,
    decimal DailyGrossWage,
    decimal SeveranceAmount,
    decimal NoticeAmount,
    decimal UnusedLeaveAmount,
    decimal TotalAmount,
    bool NetCalculated,
    decimal SgkDeduction,
    decimal IncomeTax,
    decimal StampTax,
    decimal NetTotal,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<ExitSettlementLineDto> Lines,
    IReadOnlyList<SeveranceTraceStepDto> Steps);

/// <summary>The most recent exit settlement of the employee (a rehired employee may have several; the newest exit wins).</summary>
[RequiresPermission("payroll.severance.estimate")]
public sealed record GetExitSettlementQuery(Guid EmployeeId) : IQuery<ExitSettlementDto>;

public sealed class GetExitSettlementHandler(IExitSettlementRepository settlements) : IQueryHandler<GetExitSettlementQuery, ExitSettlementDto>
{
    public async Task<Result<ExitSettlementDto>> Handle(GetExitSettlementQuery query, CancellationToken cancellationToken)
    {
        var latest = (await settlements.ListByEmployeeAsync(query.EmployeeId, cancellationToken)).FirstOrDefault();
        return latest is null ? Error.NotFound(PayrollDomainErrors.ExitSettlementNotFound) : ExitSettlementMapper.ToDto(latest);
    }
}

internal static class ExitSettlementMapper
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ExitSettlementDto ToDto(ExitSettlement s) => new(
        s.Id, s.EmployeeId, s.TerminationDate, s.CauseCategory, s.Currency, s.SeniorityDays, s.DailyGrossWage,
        s.SeveranceAmount, s.NoticeAmount, s.UnusedLeaveAmount, s.TotalAmount,
        s.NetCalculated, s.SgkDeduction, s.IncomeTax, s.StampTax, s.NetTotal, s.CalculatedAt,
        s.Lines.Select(l => new ExitSettlementLineDto(
            l.Id, l.Kind, l.Label, l.GrossAmount, l.SgkSubject, l.IncomeTaxSubject, l.StampTaxSubject, l.IsAutomatic,
            l.SgkAmount, l.IncomeTaxAmount, l.StampTaxAmount, l.NetAmount)).ToList(),
        JsonSerializer.Deserialize<List<SeveranceTraceStepDto>>(s.StepsJson, Json) ?? []);

    /// <summary>
    /// The exit event carries Employee's TerminationType, not the wizard's md.25/II answer, so a just-cause employer exit is
    /// treated as <see cref="SeveranceCauseCategory.EmployerJustCauseOther"/> (kıdem ✓); the read-only wizard estimate is
    /// where ahlak/iyiniyet ihlali can be simulated. İkale, istifa and süre bitimi carry no kıdem/ihbar here (same
    /// mapping as the wizard's panel) - an ikale bedeli is agreed per contract, not computed by law.
    /// </summary>
    public static SeveranceCauseCategory MapCause(TerminationType type) => type switch
    {
        TerminationType.ValidCause_Art18 => SeveranceCauseCategory.EmployerNoticeBased,
        TerminationType.EmployeeJustCause_Art24 => SeveranceCauseCategory.EmployeeJustCause,
        TerminationType.EmployerJustCause_Art25 => SeveranceCauseCategory.EmployerJustCauseOther,
        TerminationType.MutualRescission or TerminationType.Resignation or TerminationType.FixedTermExpiry => SeveranceCauseCategory.EmployeeResignation,
        _ => SeveranceCauseCategory.EmployerNoticeBased,
    };
}

/// <summary>
/// EmployeeTerminated -> frozen <see cref="ExitSettlement"/> (senseik "Çıkış hesabı"). Runs the same
/// <see cref="SeveranceEstimator"/> as the wizard preview, so the recorded numbers match what the user saw.
/// Idempotent per (employee, termination date): an outbox redelivery does nothing. When the estimate cannot be produced
/// (no salary record, legislation parameter missing) the event is logged and skipped - a missing settlement is
/// recoverable, a failing handler would retry the outbox row forever.
/// </summary>
public sealed class EmployeeTerminatedSettlementHandler(
    IExitSettlementRepository settlements,
    SeveranceEstimator estimator,
    ExitSettlementNetService netService,
    IPayrollUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<EmployeeTerminatedSettlementHandler> logger) : IIntegrationEventHandler<EmployeeTerminated>
{
    public async Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.TerminationDate is not { } terminationDate)
        {
            logger.LogWarning("Exit settlement skipped for {EmployeeId}: the event has no termination date.", integrationEvent.EmployeeId);
            return;
        }

        if (await settlements.FindByEmployeeAndDateAsync(integrationEvent.EmployeeId, terminationDate, cancellationToken) is not null)
        {
            return;
        }

        var cause = ExitSettlementMapper.MapCause(integrationEvent.TerminationType);
        var estimate = await estimator.EstimateAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, terminationDate, cause, cancellationToken);
        if (estimate.IsFailure)
        {
            logger.LogWarning(
                "Exit settlement NOT recorded for {EmployeeId} (tenant {TenantId}): {ErrorCode}.",
                integrationEvent.EmployeeId, integrationEvent.TenantId, estimate.Error.Code);
            return;
        }

        var e = estimate.Value;
        var data = new ExitSettlementData(
            integrationEvent.EmployeeId, terminationDate, cause, e.Currency, e.SeniorityDays, e.DailyGrossWage,
            e.SeveranceAmount, e.NoticeAmount, e.UnusedLeaveAmount, e.TotalAmount, JsonSerializer.Serialize(e.Steps, ExitSettlementMapper.Json));

        var settlement = ExitSettlement.Create(integrationEvent.TenantId, data, clock.GetUtcNow());
        var net = await netService.RecalculateAsync(settlement, cancellationToken);
        if (net.IsFailure)
        {
            // The gross amounts are still worth keeping; the net can be recalculated once the legislation is there.
            logger.LogWarning(
                "Exit settlement net NOT calculated for {EmployeeId} (tenant {TenantId}): {ErrorCode}.",
                integrationEvent.EmployeeId, integrationEvent.TenantId, net.Error.Code);
        }

        settlements.Add(settlement);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

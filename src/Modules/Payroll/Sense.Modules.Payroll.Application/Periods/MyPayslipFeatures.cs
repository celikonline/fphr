using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>One of the caller's own payslips ("Bordro Pusulam", TK-030). No employer cost - that figure is not the
/// employee's business, unlike the HR-facing <see cref="EmployeePayslipDto"/>. <see cref="HasDocument"/> is always
/// true for a listed row: <c>GET payroll/payslips/my/{id}/document</c> can always render a persisted payslip's PDF.
/// <see cref="ViewedAt"/>/<see cref="AcknowledgedAt"/> come from <see cref="PayslipDelivery"/> (null until the
/// employee downloads / acknowledges this payslip - kabul kriteri 2).</summary>
public sealed record MyPayslipDto(
    Guid PayslipId, int Year, int Month, decimal GrossAmount, decimal NetAmount, bool HasDocument, DateTime CreatedAt,
    DateTimeOffset? ViewedAt, DateTimeOffset? AcknowledgedAt);

/// <summary>The caller's own payslips, newest period first (<see cref="PayslipVisibility"/>): an Open/Approved period may
/// still change before payment.</summary>
[RequiresPermission(PayrollPermissions.MyPayslipsKey)]
public sealed record GetMyPayslipsQuery : IQuery<IReadOnlyList<MyPayslipDto>>;

public sealed class GetMyPayslipsHandler(
    IPayslipRepository payslips, IPayrollPeriodRepository periods, IPayslipDeliveryRepository deliveries, ICurrentUser currentUser)
    : IQueryHandler<GetMyPayslipsQuery, IReadOnlyList<MyPayslipDto>>
{
    public async Task<Result<IReadOnlyList<MyPayslipDto>>> Handle(GetMyPayslipsQuery query, CancellationToken cancellationToken)
    {
        // Çalışan kaydı olmayan kullanıcının (ör. yalnız yönetici hesabı) bordro pusulası olamaz.
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Result.Success<IReadOnlyList<MyPayslipDto>>([]);
        }

        var mine = await payslips.ListByEmployeeAsync(employeeId, cancellationToken);
        if (mine.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyPayslipDto>>([]);
        }

        var periodsById = (await periods.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        // Tek toplu sorgu - liste boyu kadar tebliğ sorgusu atmıyoruz (N+1 yok).
        var deliveriesByPayslipId = (await deliveries.ListByEmployeeAsync(employeeId, cancellationToken)).ToDictionary(d => d.PayslipId);
        IReadOnlyList<MyPayslipDto> list = mine
            .Where(p => periodsById.TryGetValue(p.PayrollPeriodId, out var period) && PayslipVisibility.IsVisibleToEmployee(period.Status))
            .Select(p => (Payslip: p, Period: periodsById[p.PayrollPeriodId]))
            .OrderByDescending(x => x.Period.Year)
            .ThenByDescending(x => x.Period.Month)
            .Select(x =>
            {
                deliveriesByPayslipId.TryGetValue(x.Payslip.Id, out var delivery);
                return new MyPayslipDto(
                    x.Payslip.Id, x.Period.Year, x.Period.Month, x.Payslip.GrossAmount, x.Payslip.NetAmount, HasDocument: true, x.Payslip.CreatedAt,
                    delivery?.ViewedAt, delivery?.AcknowledgedAt);
            })
            .ToList();
        return Result.Success(list);
    }
}

/// <summary>"Okudum, onaylıyorum" (TK-030 kabul kriteri 2): idempotent - ikinci çağrı ilk onayı değiştirmez, 204 döner.
/// Başkasının pusulası veya görünmeyen (Open/Approved) bir dönem <see cref="Declarations.GetMyPayslipDocumentQuery"/>'yle
/// aynı "bulunamadı" hatasını döner - varlığı sızdırmaz.</summary>
[RequiresPermission(PayrollPermissions.MyPayslipsKey)]
public sealed record AcknowledgeMyPayslipCommand(Guid PayslipId, PayslipDeliveryChannel Channel) : ICommand;

public sealed class AcknowledgeMyPayslipHandler(
    IPayslipRepository payslips, IPayrollPeriodRepository periods, IPayslipDeliveryRepository deliveries,
    ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<AcknowledgeMyPayslipCommand>
{
    public async Task<Result> Handle(AcknowledgeMyPayslipCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(PayrollDomainErrors.PayslipNotFound);
        }

        var payslip = await payslips.FindByIdAsync(command.PayslipId, cancellationToken);
        var period = payslip is null ? null : await periods.FindByIdAsync(payslip.PayrollPeriodId, cancellationToken);
        if (payslip is null || payslip.EmployeeId != employeeId || period is null || !PayslipVisibility.IsVisibleToEmployee(period.Status))
        {
            return Error.NotFound(PayrollDomainErrors.PayslipNotFound);
        }

        var delivery = await deliveries.FindByPayslipIdAsync(payslip.Id, cancellationToken);
        if (delivery is null)
        {
            delivery = PayslipDelivery.Create(payslip.TenantId, payslip.Id, employeeId, command.Channel);
            deliveries.Add(delivery);
        }

        delivery.Acknowledge(clock.GetUtcNow(), command.Channel, payslip.Version, PayslipEvidenceHash.Compute(payslip.InputHash, payslip.Version));
        return Result.Success();
    }
}

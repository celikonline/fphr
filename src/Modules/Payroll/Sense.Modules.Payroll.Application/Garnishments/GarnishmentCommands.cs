using FluentValidation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Garnishments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Garnishments;

/// <summary>Bir icra/nafaka kesinti emri (senseik-019 2c, İİK m.83) - liste/detay ekranı için düz DTO.</summary>
public sealed record GarnishmentOrderDto(
    Guid Id, Guid EmployeeId, GarnishmentType Type, string CaseReference,
    decimal MonthlyInstallmentAmount, decimal RemainingBalance, DateOnly StartDate, DateOnly? ClosedDate)
{
    public bool IsClosed => ClosedDate is not null;
}

/// <summary>Yeni bir icra/nafaka kesinti emri açar ("Kesintiler" ekranı, P8; API only bu sürümde).</summary>
[RequiresPermission("payroll.courtorder.manage")]
public sealed record CreateGarnishmentOrderCommand(
    Guid EmployeeId, GarnishmentType Type, string CaseReference, decimal MonthlyInstallmentAmount, decimal RemainingBalance, DateOnly StartDate) : ICommand<Guid>;

public sealed class CreateGarnishmentOrderValidator : AbstractValidator<CreateGarnishmentOrderCommand>
{
    public CreateGarnishmentOrderValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.CaseReference).NotEmpty();
        RuleFor(x => x.MonthlyInstallmentAmount).GreaterThan(0);
        RuleFor(x => x.RemainingBalance).GreaterThan(0);
    }
}

public sealed class CreateGarnishmentOrderHandler(ITenantContext tenant, IGarnishmentOrderRepository orders) : ICommandHandler<CreateGarnishmentOrderCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateGarnishmentOrderCommand command, CancellationToken cancellationToken)
    {
        var created = GarnishmentOrder.Create(
            tenant.TenantId, command.EmployeeId, command.Type, command.CaseReference,
            command.MonthlyInstallmentAmount, command.RemainingBalance, command.StartDate);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        orders.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

/// <summary>Bir kesinti emrini manuel kapatır (örn. borç başka yolla kapandı).</summary>
[RequiresPermission("payroll.courtorder.manage")]
public sealed record CloseGarnishmentOrderCommand(Guid OrderId, DateOnly ClosedDate) : ICommand;

public sealed class CloseGarnishmentOrderHandler(IGarnishmentOrderRepository orders) : ICommandHandler<CloseGarnishmentOrderCommand>
{
    public async Task<Result> Handle(CloseGarnishmentOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Error.NotFound(PayrollDomainErrors.GarnishmentNotFound);
        }

        return order.Close(command.ClosedDate);
    }
}

/// <summary>Bir çalışanın kapalılar dahil tüm kesinti emirleri, en yeni önce ("Kesintiler" sekmesi).</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ListGarnishmentOrdersQuery(Guid EmployeeId) : IQuery<IReadOnlyList<GarnishmentOrderDto>>;

public sealed class ListGarnishmentOrdersHandler(IGarnishmentOrderRepository orders) : IQueryHandler<ListGarnishmentOrdersQuery, IReadOnlyList<GarnishmentOrderDto>>
{
    public async Task<Result<IReadOnlyList<GarnishmentOrderDto>>> Handle(ListGarnishmentOrdersQuery query, CancellationToken cancellationToken)
    {
        var list = await orders.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        return list
            .Select(o => new GarnishmentOrderDto(o.Id, o.EmployeeId, o.Type, o.CaseReference, o.MonthlyInstallmentAmount, o.RemainingBalance, o.StartDate, o.ClosedDate))
            .ToList();
    }
}

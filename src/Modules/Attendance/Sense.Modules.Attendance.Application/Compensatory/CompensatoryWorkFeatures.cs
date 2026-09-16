using FluentValidation;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Contracts.Events;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Compensatory;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Compensatory;

public sealed record CompensatoryWorkLedgerDto(
    Guid Id, Guid EmployeeId, DateOnly WeekStart, int DeficitMinutes, int CompensatedMinutes, DateOnly DueDate, CompensatoryWorkStatus Status);

internal static class CompensatoryWorkLedgerMapper
{
    public static CompensatoryWorkLedgerDto ToDto(CompensatoryWorkLedger l) =>
        new(l.Id, l.EmployeeId, l.WeekStart, l.DeficitMinutes, l.CompensatedMinutes, l.DueDate, l.Status);
}

/// <summary>
/// Bir haftanın puantajı tamamlanınca (package A3's TimesheetWeekCompleted) telafi borcunu hesaplar/günceller: o
/// haftanın AttendanceDay.PlannedMinutes toplamı 45s/hafta normunun altındaysa fark telafi borcu olarak açılır -
/// "kısa çalışma" nedeniyle bilinçli düşürülmüş bir takvim anlamına gelir (gerçekleşen/çalışılan saatin norma göre
/// az olması değil - bkz. CompensatoryWorkLedger doc comment). Bağımsız bir entegrasyon olay işleyicisi olarak
/// çalışır (EmployeeHiredHandler gibi), kendi SaveChanges'ini kendi yapar.
/// </summary>
public sealed class TimesheetWeekCompletedHandler(IAttendanceDayRepository days, ICompensatoryWorkLedgerRepository ledger, IAttendanceUnitOfWork unitOfWork)
    : IIntegrationEventHandler<TimesheetWeekCompleted>
{
    public async Task Handle(TimesheetWeekCompleted integrationEvent, CancellationToken cancellationToken)
    {
        var weekEnd = integrationEvent.WeekStart.AddDays(6);
        var records = await days.ListForEmployeeAsync(integrationEvent.EmployeeId, integrationEvent.WeekStart, weekEnd, cancellationToken);
        var plannedMinutes = records.Sum(d => d.PlannedMinutes);
        var deficit = Math.Max(CompensatoryWorkLimits.StandardWeeklyMinutes - plannedMinutes, 0);

        var existing = await ledger.FindByEmployeeAndWeekAsync(integrationEvent.EmployeeId, integrationEvent.WeekStart, cancellationToken);
        if (deficit <= 0)
        {
            return;
        }

        if (existing is not null)
        {
            if (existing.Status != CompensatoryWorkStatus.Open || existing.DeficitMinutes == deficit)
            {
                return;
            }

            existing.AdjustDeficit(deficit);
        }
        else
        {
            var dueDate = integrationEvent.WeekStart.AddMonths(CompensatoryWorkLimits.WindowMonths);
            ledger.Add(CompensatoryWorkLedger.Open(integrationEvent.TenantId, integrationEvent.EmployeeId, integrationEvent.WeekStart, deficit, dueDate));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequiresPermission(AttendancePermissions.CompensatoryViewKey)]
public sealed record ListCompensatoryLedgerQuery(Guid EmployeeId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<CompensatoryWorkLedgerDto>>;

public sealed class ListCompensatoryLedgerHandler(IDataScopeService dataScope, ICompensatoryWorkLedgerRepository ledger)
    : IQueryHandler<ListCompensatoryLedgerQuery, IReadOnlyList<CompensatoryWorkLedgerDto>>
{
    public async Task<Result<IReadOnlyList<CompensatoryWorkLedgerDto>>> Handle(ListCompensatoryLedgerQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.CompensatoryViewKey, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var list = await ledger.ListForEmployeeAsync(query.EmployeeId, query.From, query.To, cancellationToken);
        return list.Select(CompensatoryWorkLedgerMapper.ToDto).ToList();
    }
}

/// <summary>Yönetici/İK, çalışanın telafi çalıştığı dakikayı borcuna işler.</summary>
[RequiresPermission(AttendancePermissions.CompensatoryManageKey)]
public sealed record RecordCompensationCommand(Guid LedgerId, int Minutes) : ICommand;

public sealed class RecordCompensationValidator : AbstractValidator<RecordCompensationCommand>
{
    public RecordCompensationValidator() => RuleFor(x => x.Minutes).GreaterThan(0);
}

public sealed class RecordCompensationHandler(ICompensatoryWorkLedgerRepository ledger) : ICommandHandler<RecordCompensationCommand>
{
    public async Task<Result> Handle(RecordCompensationCommand command, CancellationToken cancellationToken)
    {
        var found = await ledger.GetByIdAsync(command.LedgerId, cancellationToken);
        return found.IsFailure ? found.Error : found.Value.RecordCompensation(command.Minutes);
    }
}

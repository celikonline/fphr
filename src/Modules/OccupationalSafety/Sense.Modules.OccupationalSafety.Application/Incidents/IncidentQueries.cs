using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.Incidents;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Application.Incidents;

/// <summary>İSG olay kaydı. <see cref="IsSgkOverdue"/>: kaza bildirilmemiş ve son gün geçmiş.</summary>
public sealed record OhsIncidentDto(
    Guid Id,
    OhsIncidentKind Kind,
    DateOnly EventDate,
    Guid? LocationId,
    Guid? EmployeeId,
    Guid? ReportedByEmployeeId,
    string Description,
    DateOnly? SgkNotificationDeadline,
    DateOnly? SgkNotifiedOn,
    bool IsSgkOverdue,
    string? CorrectiveAction,
    DateOnly? ClosedOn);

internal static class OhsIncidentMapper
{
    public static OhsIncidentDto ToDto(OhsIncident i, DateOnly today) => new(
        i.Id, i.Kind, i.EventDate, i.LocationId, i.EmployeeId, i.ReportedByEmployeeId, i.Description,
        i.SgkNotificationDeadline, i.SgkNotifiedOn,
        i.Kind == OhsIncidentKind.WorkAccident && i.SgkNotifiedOn is null && i.SgkNotificationDeadline < today,
        i.CorrectiveAction, i.ClosedOn);
}

/// <summary>Tüm İSG olayları, en yeni önce (İK / İSG uzmanı).</summary>
[RequiresPermission("ohs.incidents.view")]
public sealed record ListIncidentsQuery : IQuery<IReadOnlyList<OhsIncidentDto>>;

public sealed class ListIncidentsHandler(IOhsIncidentRepository incidents, TimeProvider clock)
    : IQueryHandler<ListIncidentsQuery, IReadOnlyList<OhsIncidentDto>>
{
    public async Task<Result<IReadOnlyList<OhsIncidentDto>>> Handle(ListIncidentsQuery query, CancellationToken cancellationToken)
    {
        var today = IncidentClock.Today(clock);
        IReadOnlyList<OhsIncidentDto> items = (await incidents.ListAsync(cancellationToken))
            .Select(i => OhsIncidentMapper.ToDto(i, today))
            .ToList();
        return Result.Success(items);
    }
}

/// <summary>Çalışanın kendi ramak kala bildirimleri (durumunu ve alınan önlemi görür).</summary>
[RequiresPermission("ohs.my.incident_reports")]
public sealed record ListMyIncidentReportsQuery : IQuery<IReadOnlyList<OhsIncidentDto>>;

public sealed class ListMyIncidentReportsHandler(ICurrentUser currentUser, IOhsIncidentRepository incidents, TimeProvider clock)
    : IQueryHandler<ListMyIncidentReportsQuery, IReadOnlyList<OhsIncidentDto>>
{
    public async Task<Result<IReadOnlyList<OhsIncidentDto>>> Handle(ListMyIncidentReportsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } reporterId)
        {
            return Result.Success<IReadOnlyList<OhsIncidentDto>>([]);
        }

        var today = IncidentClock.Today(clock);
        IReadOnlyList<OhsIncidentDto> items = (await incidents.ListReportedByAsync(reporterId, cancellationToken))
            .Select(i => OhsIncidentMapper.ToDto(i, today))
            .ToList();
        return Result.Success(items);
    }
}

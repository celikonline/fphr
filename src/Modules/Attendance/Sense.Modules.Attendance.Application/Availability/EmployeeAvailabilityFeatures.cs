using FluentValidation;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Availability;

public sealed record EmployeeAvailabilityDto(
    Guid Id, Guid EmployeeId, DateOnly Date, TimeOnly? Start, TimeOnly? End, AvailabilityKind Kind, string? Note, AvailabilityStatus Status);

internal static class EmployeeAvailabilityMapper
{
    public static EmployeeAvailabilityDto ToDto(EmployeeAvailability a) => new(a.Id, a.EmployeeId, a.Date, a.Start, a.End, a.Kind, a.Note, a.Status);
}

/// <summary>"Müsait değilim" gir (11.3.6): kendi adına, gün bazlı - Start/End boşsa tüm gün.</summary>
[RequiresPermission("attendance.availability.self")]
public sealed record SubmitMyAvailabilityCommand(DateOnly Date, TimeOnly? Start, TimeOnly? End, AvailabilityKind Kind, string? Note) : ICommand<Guid>;

public sealed class SubmitMyAvailabilityHandler(ITenantContext tenant, ICurrentUser currentUser, IEmployeeAvailabilityRepository availability)
    : ICommandHandler<SubmitMyAvailabilityCommand, Guid>
{
    public Task<Result<Guid>> Handle(SubmitMyAvailabilityCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Task.FromResult(Result.Failure<Guid>(Error.NotFound(AttendanceErrors.EmployeeNotFound)));
        }

        var created = EmployeeAvailability.Create(tenant.TenantId, employeeId, command.Date, command.Start, command.End, command.Kind, command.Note);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        availability.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

[RequiresPermission("attendance.availability.self")]
public sealed record CancelMyAvailabilityCommand(Guid AvailabilityId) : ICommand;

public sealed class CancelMyAvailabilityHandler(ICurrentUser currentUser, IEmployeeAvailabilityRepository availability) : ICommandHandler<CancelMyAvailabilityCommand>
{
    public async Task<Result> Handle(CancelMyAvailabilityCommand command, CancellationToken cancellationToken)
    {
        var found = await availability.GetByIdAsync(command.AvailabilityId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (found.Value.EmployeeId != currentUser.EmployeeId)
        {
            return Error.Forbidden(AttendanceDomainErrors.AvailabilityNotOwner);
        }

        return found.Value.Cancel();
    }
}

[RequiresPermission("attendance.availability.self")]
public sealed record ListMyAvailabilityQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<EmployeeAvailabilityDto>>;

public sealed class ListMyAvailabilityHandler(ICurrentUser currentUser, IEmployeeAvailabilityRepository availability)
    : IQueryHandler<ListMyAvailabilityQuery, IReadOnlyList<EmployeeAvailabilityDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeAvailabilityDto>>> Handle(ListMyAvailabilityQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var list = await availability.ListForEmployeeAsync(employeeId, query.From, query.To, cancellationToken);
        return list.Select(EmployeeAvailabilityMapper.ToDto).ToList();
    }
}

/// <summary>Planlayıcının görmesi için (11.3.6): bir çalışanın müsaitlik girişleri.</summary>
[RequiresPermission("attendance.availability.view")]
public sealed record ListEmployeeAvailabilityQuery(Guid EmployeeId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<EmployeeAvailabilityDto>>;

public sealed class ListEmployeeAvailabilityHandler(IDataScopeService dataScope, IEmployeeAvailabilityRepository availability)
    : IQueryHandler<ListEmployeeAvailabilityQuery, IReadOnlyList<EmployeeAvailabilityDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeAvailabilityDto>>> Handle(ListEmployeeAvailabilityQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.AvailabilityView.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var list = await availability.ListForEmployeeAsync(query.EmployeeId, query.From, query.To, cancellationToken);
        return list.Select(EmployeeAvailabilityMapper.ToDto).ToList();
    }
}

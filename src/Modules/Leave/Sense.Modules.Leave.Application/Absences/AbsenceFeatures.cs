using FluentValidation;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Absences;

/// <summary>Employee card → Devamsızlık row. <see cref="ReversesId"/>/<see cref="IsReversed"/> mirror the append-only pattern (bkz. AbsenceRecord).</summary>
public sealed record AbsenceDto(
    Guid Id, DateOnly Date, AbsenceKind Kind, decimal? Hours, string? Note, string RecordedBy, DateTime RecordedAt, Guid? ReversesId, bool IsReversed);

internal static class AbsenceMapper
{
    public static async Task<IReadOnlyList<AbsenceDto>> ToDtosAsync(
        Guid tenantId, IReadOnlyList<AbsenceRecord> records, IUserProfileLookup userProfiles, CancellationToken ct)
    {
        if (records.Count == 0)
        {
            return [];
        }

        var names = await userProfiles.GetAsync(tenantId, records.Select(r => r.RecordedByUserId).Distinct().ToList(), ct);
        var reversedIds = records.Where(r => r.ReversesId is not null).Select(r => r.ReversesId!.Value).ToHashSet();

        return records
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt)
            .Select(r => new AbsenceDto(
                r.Id, r.Date, r.Kind, r.Hours, r.Note, names.GetValueOrDefault(r.RecordedByUserId)?.DisplayName ?? string.Empty, r.CreatedAt,
                r.ReversesId, reversedIds.Contains(r.Id)))
            .ToList();
    }
}

/// <summary>Employee card → Devamsızlık sekmesi: absence records of one employee in [From, To] (originals and their reversals both included).</summary>
[RequiresPermission("leave.absences.view")]
public sealed record ListEmployeeAbsencesQuery(Guid EmployeeId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AbsenceDto>>;

public sealed class ListEmployeeAbsencesHandler(
    ITenantContext tenant, IDataScopeService dataScope, IAbsenceRecordRepository absences, IUserProfileLookup userProfiles)
    : IQueryHandler<ListEmployeeAbsencesQuery, IReadOnlyList<AbsenceDto>>
{
    public async Task<Result<IReadOnlyList<AbsenceDto>>> Handle(ListEmployeeAbsencesQuery query, CancellationToken cancellationToken)
    {
        if (query.To < query.From)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.AbsencesView.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var records = await absences.ListAsync(query.EmployeeId, query.From, query.To, cancellationToken);
        return Result.Success(await AbsenceMapper.ToDtosAsync(tenant.TenantId, records, userProfiles, cancellationToken));
    }
}

/// <summary>"Devamsızlık ekle": İK bir çalışan için mazeretsiz/mazeretli/geç kalma kaydı girer.</summary>
[RequiresPermission("leave.absences.manage")]
public sealed record RecordAbsenceCommand(Guid EmployeeId, DateOnly Date, AbsenceKind Kind, decimal? Hours, string? Note) : ICommand<Guid>;

public sealed class RecordAbsenceValidator : AbstractValidator<RecordAbsenceCommand>
{
    public RecordAbsenceValidator()
    {
        RuleFor(x => x.Note).MaximumLength(LeaveLimits.AbsenceNoteMaxLength);
        RuleFor(x => x.Hours).InclusiveBetween(0.01m, 24m).When(x => x.Hours is not null);
    }
}

public sealed class RecordAbsenceHandler(ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, IAbsenceRecordRepository absences)
    : ICommandHandler<RecordAbsenceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordAbsenceCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.AbsencesManage.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var record = AbsenceRecord.Record(
            tenant.TenantId, command.EmployeeId, command.Date, command.Kind, command.Hours, command.Note, currentUser.UserId ?? Guid.Empty);
        if (record.IsFailure)
        {
            return record.Error;
        }

        absences.Add(record.Value);
        return record.Value.Id;
    }
}

/// <summary>"Ters kayıt": bir devamsızlık kaydının düzeltmesi (append-only - orijinal satır değişmez, silinmez).</summary>
[RequiresPermission("leave.absences.manage")]
public sealed record ReverseAbsenceCommand(Guid EmployeeId, Guid Id, string Reason) : ICommand<Guid>;

public sealed class ReverseAbsenceValidator : AbstractValidator<ReverseAbsenceCommand>
{
    public ReverseAbsenceValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(LeaveLimits.AbsenceNoteMaxLength);
}

public sealed class ReverseAbsenceHandler(ICurrentUser currentUser, IDataScopeService dataScope, IAbsenceRecordRepository absences)
    : ICommandHandler<ReverseAbsenceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ReverseAbsenceCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.AbsencesManage.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var original = await absences.FindByIdAsync(command.Id, cancellationToken);
        if (original is null || original.EmployeeId != command.EmployeeId)
        {
            return Error.NotFound(LeaveErrors.AbsenceNotFound);
        }

        var alreadyReversed = await absences.ExistsReversalAsync(original.Id, cancellationToken);
        var reversal = AbsenceRecord.Reverse(original, command.Reason, currentUser.UserId ?? Guid.Empty, alreadyReversed);
        if (reversal.IsFailure)
        {
            return reversal.Error;
        }

        absences.Add(reversal.Value);
        return reversal.Value.Id;
    }
}

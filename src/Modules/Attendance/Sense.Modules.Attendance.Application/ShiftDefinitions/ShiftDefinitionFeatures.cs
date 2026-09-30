using FluentValidation;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.ShiftDefinitions;

/// <summary>Mola satırı: sabit molada <see cref="StartMinute"/> zorunlu (gün başından dakika), esnek molada yok sayılır.</summary>
public sealed record ShiftBreakDto(int? StartMinute, int DurationMinutes, bool IsFlexible);

public sealed record ShiftDefinitionDto(
    Guid Id, string Code, string Name, string ColorHex, ShiftType Type, int StartMinute, int EndMinute, bool StartsPreviousDay,
    int? DurationMinutes, int? FlexStartEarliestMinute, int? FlexStartLatestMinute, IReadOnlyList<ShiftBreakDto> Breaks,
    int LateToleranceMinutes, int EarlyLeaveToleranceMinutes, int? EarlyArrivalOvertimeMinMinutes, int? EarlyArrivalOvertimeMaxMinutes,
    int? LateDepartureOvertimeMinMinutes, int? LateDepartureOvertimeMaxMinutes, int BonusMinutes, bool IsActive);

/// <summary>Oluşturma/güncelleme gövdesi (spec §4 alanları); doğrulama <see cref="ShiftDefinition"/>'dadır.</summary>
public sealed record ShiftDefinitionInput(
    string Code, string Name, string ColorHex, ShiftType Type, int StartMinute, int EndMinute, bool StartsPreviousDay,
    int? DurationMinutes, int? FlexStartEarliestMinute, int? FlexStartLatestMinute, IReadOnlyList<ShiftBreakDto>? Breaks,
    int LateToleranceMinutes, int EarlyLeaveToleranceMinutes, int? EarlyArrivalOvertimeMinMinutes, int? EarlyArrivalOvertimeMaxMinutes,
    int? LateDepartureOvertimeMinMinutes, int? LateDepartureOvertimeMaxMinutes, int BonusMinutes)
{
    /// <summary>A fixed break without a start gets an out-of-range minute so the domain rejects it (ShiftDefinitionBreakInvalid).</summary>
    private const int MissingBreakStart = -1;

    public ShiftDefinitionValues ToValues() =>
        new(Code, Name, ColorHex, Type, StartMinute, EndMinute, StartsPreviousDay, DurationMinutes, FlexStartEarliestMinute, FlexStartLatestMinute,
            (Breaks ?? []).Select(b => b.IsFlexible ? ShiftBreak.Flexible(b.DurationMinutes) : ShiftBreak.Fixed(b.StartMinute ?? MissingBreakStart, b.DurationMinutes))
                .ToList(),
            LateToleranceMinutes, EarlyLeaveToleranceMinutes, EarlyArrivalOvertimeMinMinutes, EarlyArrivalOvertimeMaxMinutes,
            LateDepartureOvertimeMinMinutes, LateDepartureOvertimeMaxMinutes, BonusMinutes);
}

/// <summary>
/// Kaydetme sonucu: "Geçmişe uygula" seçildiyse etkilenen atama sayısı ve kuyruğa alınan yeniden hesaplama işi
/// (<see cref="IAttendanceDayRecalculator.GetStatusAsync"/> ile izlenir); etkilenen atama yoksa iş yoktur.
/// </summary>
public sealed record ShiftDefinitionSaveResultDto(Guid Id, Guid? RecalculationJobId, int AffectedAssignmentCount);

internal static class ShiftDefinitionMapper
{
    public static ShiftDefinitionDto ToDto(ShiftDefinition d) =>
        new(d.Id, d.Code, d.Name, d.ColorHex, d.Type, d.StartMinute, d.EndMinute, d.StartsPreviousDay, d.DurationMinutes,
            d.FlexStartEarliestMinute, d.FlexStartLatestMinute, d.Breaks.Select(b => new ShiftBreakDto(b.StartMinute, b.DurationMinutes, b.IsFlexible)).ToList(),
            d.LateToleranceMinutes, d.EarlyLeaveToleranceMinutes, d.EarlyArrivalOvertimeMinMinutes, d.EarlyArrivalOvertimeMaxMinutes,
            d.LateDepartureOvertimeMinMinutes, d.LateDepartureOvertimeMaxMinutes, d.BonusMinutes, d.IsActive);

    /// <summary>Kod tenant içinde tekil (spec §4). Boş kodu domain doğrulaması reddeder.</summary>
    public static async Task<bool> IsCodeTakenAsync(IShiftDefinitionRepository definitions, string? code, Guid? excludeId, CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(code) && await definitions.CodeExistsAsync(code.Trim(), excludeId, cancellationToken).ConfigureAwait(false);
}

/// <summary>Vardiya kataloğu (Z0.2).</summary>
[RequiresPermission("attendance.shiftdefinition.view")]
public sealed record ListShiftDefinitionsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<ShiftDefinitionDto>>;

public sealed class ListShiftDefinitionsHandler(IShiftDefinitionRepository definitions) : IQueryHandler<ListShiftDefinitionsQuery, IReadOnlyList<ShiftDefinitionDto>>
{
    public async Task<Result<IReadOnlyList<ShiftDefinitionDto>>> Handle(ListShiftDefinitionsQuery query, CancellationToken cancellationToken) =>
        (await definitions.ListAsync(query.IncludeInactive, cancellationToken)).Select(ShiftDefinitionMapper.ToDto).ToList();
}

[RequiresPermission("attendance.shiftdefinition.view")]
public sealed record GetShiftDefinitionQuery(Guid ShiftDefinitionId) : IQuery<ShiftDefinitionDto>;

public sealed class GetShiftDefinitionHandler(IShiftDefinitionRepository definitions) : IQueryHandler<GetShiftDefinitionQuery, ShiftDefinitionDto>
{
    public async Task<Result<ShiftDefinitionDto>> Handle(GetShiftDefinitionQuery query, CancellationToken cancellationToken)
    {
        var definition = await definitions.GetByIdAsync(query.ShiftDefinitionId, cancellationToken);
        return definition.IsFailure ? definition.Error : ShiftDefinitionMapper.ToDto(definition.Value);
    }
}

[RequiresPermission("attendance.shiftdefinition.manage")]
public sealed record CreateShiftDefinitionCommand(ShiftDefinitionInput Definition) : ICommand<Guid>;

public sealed class CreateShiftDefinitionValidator : AbstractValidator<CreateShiftDefinitionCommand>
{
    public CreateShiftDefinitionValidator() => RuleFor(x => x.Definition).NotNull();
}

public sealed class CreateShiftDefinitionHandler(IShiftDefinitionRepository definitions, ITenantContext tenant) : ICommandHandler<CreateShiftDefinitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateShiftDefinitionCommand command, CancellationToken cancellationToken)
    {
        var created = ShiftDefinition.Create(tenant.TenantId, command.Definition.ToValues());
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (await ShiftDefinitionMapper.IsCodeTakenAsync(definitions, created.Value.Code, null, cancellationToken))
        {
            return Error.Conflict(AttendanceErrors.ShiftDefinitionCodeDuplicate);
        }

        definitions.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>
/// "Kaydet" / "Kaydet &amp; Geçmişe Uygula" (spec §5.3): <see cref="ApplyToPast"/> ile tanıma bağlı atamaların
/// [<see cref="ApplyFrom"/> (yoksa en eski), bugün] aralığı <see cref="IAttendanceDayRecalculator"/>'a kuyruklanır; dinamik tanımda
/// tüm dinamik tanımların atamaları etkilenir (eşleşme değişebilir). Kilitli ayları (K6) yeniden hesaplama servisi atlar.
/// </summary>
[RequiresPermission("attendance.shiftdefinition.manage")]
public sealed record UpdateShiftDefinitionCommand(Guid ShiftDefinitionId, ShiftDefinitionInput Definition, bool ApplyToPast, DateOnly? ApplyFrom)
    : ICommand<ShiftDefinitionSaveResultDto>;

public sealed class UpdateShiftDefinitionValidator : AbstractValidator<UpdateShiftDefinitionCommand>
{
    public UpdateShiftDefinitionValidator() => RuleFor(x => x.Definition).NotNull();
}

public sealed class UpdateShiftDefinitionHandler(
    IShiftDefinitionRepository definitions, IAttendanceDayRecalculator recalculator, ITenantContext tenant, ITenantLocalClock localClock)
    : ICommandHandler<UpdateShiftDefinitionCommand, ShiftDefinitionSaveResultDto>
{
    public async Task<Result<ShiftDefinitionSaveResultDto>> Handle(UpdateShiftDefinitionCommand command, CancellationToken cancellationToken)
    {
        var found = await definitions.GetByIdAsync(command.ShiftDefinitionId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var definition = found.Value;
        var localToday = DateOnly.FromDateTime((await localClock.NowAsync(tenant.TenantId, cancellationToken)).Local);
        if (command.ApplyToPast && command.ApplyFrom > localToday)
        {
            return Error.Validation(AttendanceErrors.DateRangeInvalid);
        }

        if (await ShiftDefinitionMapper.IsCodeTakenAsync(definitions, command.Definition.Code, definition.Id, cancellationToken))
        {
            return Error.Conflict(AttendanceErrors.ShiftDefinitionCodeDuplicate);
        }

        var wasDynamic = definition.Type == ShiftType.Dynamic;
        var updated = definition.Update(command.Definition.ToValues());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        return command.ApplyToPast
            ? await ApplyToPastAsync(definition, wasDynamic, command.ApplyFrom ?? DateOnly.MinValue, localToday, cancellationToken)
            : new ShiftDefinitionSaveResultDto(definition.Id, null, 0);
    }

    private async Task<ShiftDefinitionSaveResultDto> ApplyToPastAsync(
        ShiftDefinition definition, bool wasDynamic, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> definitionIds = wasDynamic || definition.Type == ShiftType.Dynamic
            ? (await definitions.ListAsync(includeInactive: true, cancellationToken))
                .Where(d => d.Type == ShiftType.Dynamic || d.Id == definition.Id).Select(d => d.Id).ToList()
            : [definition.Id];

        var affected = await definitions.ListAssignmentsUsingAsync(definitionIds, from, to, cancellationToken);
        if (affected.Count == 0)
        {
            return new ShiftDefinitionSaveResultDto(definition.Id, null, 0);
        }

        var first = affected.Min(a => a.Date);
        var last = affected.Max(a => a.Date);
        var employeeIds = affected.Select(a => a.EmployeeId).Distinct().ToList();
        definition.MarkAppliedToPast(first);
        var jobId = await recalculator.EnqueueAsync(tenant.TenantId, first, last, employeeIds, cancellationToken);
        return new ShiftDefinitionSaveResultDto(definition.Id, jobId, affected.Count);
    }
}

/// <summary>Pasife alma (silme yok: geçmiş atamalar tanıma bağlı kalır). Pasif tanım dinamik eşleşmede aday olmaz.</summary>
[RequiresPermission("attendance.shiftdefinition.manage")]
public sealed record SetShiftDefinitionActiveCommand(Guid ShiftDefinitionId, bool IsActive) : ICommand;

public sealed class SetShiftDefinitionActiveHandler(IShiftDefinitionRepository definitions) : ICommandHandler<SetShiftDefinitionActiveCommand>
{
    public async Task<Result> Handle(SetShiftDefinitionActiveCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.GetByIdAsync(command.ShiftDefinitionId, cancellationToken);
        if (definition.IsFailure)
        {
            return definition.Error;
        }

        if (command.IsActive)
        {
            definition.Value.Activate();
        }
        else
        {
            definition.Value.Deactivate();
        }

        return Result.Success();
    }
}

/// <summary>
/// Silme: shift_definition_id için FK yok (uygulama bütünlüğü) - atamada, rotasyon adımında ya da plan şablonunda kullanılan tanım
/// silinemez (<see cref="AttendanceErrors.ShiftDefinitionInUse"/>), yalnız pasife alınır (<see cref="SetShiftDefinitionActiveCommand"/>).
/// </summary>
[RequiresPermission("attendance.shiftdefinition.manage")]
public sealed record DeleteShiftDefinitionCommand(Guid ShiftDefinitionId) : ICommand;

public sealed class DeleteShiftDefinitionHandler(IShiftDefinitionRepository definitions) : ICommandHandler<DeleteShiftDefinitionCommand>
{
    public async Task<Result> Handle(DeleteShiftDefinitionCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.GetByIdAsync(command.ShiftDefinitionId, cancellationToken);
        if (definition.IsFailure)
        {
            return definition.Error;
        }

        if (await definitions.IsInUseAsync(definition.Value.Id, cancellationToken))
        {
            return Error.Conflict(AttendanceErrors.ShiftDefinitionInUse);
        }

        definitions.Remove(definition.Value);
        return Result.Success();
    }
}

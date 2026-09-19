using FluentValidation;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Contracts.Events;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Overtime;

public sealed record OvertimeConsentDto(
    Guid Id, Guid EmployeeId, DateOnly GivenAt, OvertimeConsentSource Source, bool CoversNightWork, DateOnly? RevocationNoticeAt,
    DateOnly? RevocationEffectiveAt, Guid? DocumentId);

public sealed record OvertimeConversionDto(
    Guid Id, Guid OvertimeRequestId, Guid EmployeeId, OvertimeConversionKind Kind, OvertimeMinutesBasis MinutesBasis, decimal Rate, int BasisMinutes,
    int ResultMinutes, decimal ResultDays, Guid? LeaveTypeId, Guid? LedgerEntryId, OvertimeConversionStatus Status, DateTime ConvertedAt, DateOnly? UsedByDate);

internal static class OvertimeConsentMapper
{
    public static OvertimeConsentDto ToDto(OvertimeConsent c) =>
        new(c.Id, c.EmployeeId, c.GivenAt, c.Source, c.CoversNightWork, c.RevocationNoticeAt, c.RevocationEffectiveAt, c.DocumentId);

    public static OvertimeConversionDto ToDto(OvertimeConversion c) =>
        new(c.Id, c.OvertimeRequestId, c.EmployeeId, c.Kind, c.MinutesBasis, c.RateSnapshot, c.BasisMinutes, c.ResultMinutes, c.ResultDays, c.LeaveTypeId,
            c.LedgerEntryId, c.Status, c.ConvertedAt, c.UsedByDate);
}

[RequiresPermission("leave.overtime_consents.manage")]
public sealed record ListOvertimeConsentsQuery(Guid EmployeeId, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<OvertimeConsentDto>>;

public sealed class ListOvertimeConsentsHandler(IOvertimeConsentRepository consents, IDataScopeService dataScope)
    : IQueryHandler<ListOvertimeConsentsQuery, IReadOnlyList<OvertimeConsentDto>>
{
    public async Task<Result<IReadOnlyList<OvertimeConsentDto>>> Handle(ListOvertimeConsentsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimeConsentsManage.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        IReadOnlyList<OvertimeConsentDto> list = (await consents.ListByEmployeeAsync(query.EmployeeId, query.Take, cancellationToken)).Select(OvertimeConsentMapper.ToDto).ToList();
        return Result.Success(list);
    }
}

/// <summary>Records a written overtime consent (no expiry; 2017 regulation).</summary>
[RequiresPermission("leave.overtime_consents.manage")]
public sealed record RecordOvertimeConsentCommand(Guid EmployeeId, DateOnly GivenAt, OvertimeConsentSource Source, bool CoversNightWork, Guid? DocumentId)
    : ICommand<Guid>;

public sealed class RecordOvertimeConsentHandler(
    ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, IOvertimeConsentRepository consents, IIntegrationEventOutbox outbox)
    : ICommandHandler<RecordOvertimeConsentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordOvertimeConsentCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimeConsentsManage.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var consent = OvertimeConsent.Record(tenant.TenantId, command.EmployeeId, command.GivenAt, command.Source, command.CoversNightWork, command.DocumentId, currentUser.UserId);
        consents.Add(consent);
        outbox.Enqueue(new OvertimeConsentRecorded(consent.TenantId, consent.Id, consent.EmployeeId, consent.GivenAt, consent.CoversNightWork));
        return consent.Id;
    }
}

/// <summary>Records the employee's written revocation notice; effective after Overtime.ConsentRevocationNoticeDays (30).</summary>
[RequiresPermission("leave.overtime_consents.manage")]
public sealed record RevokeOvertimeConsentCommand(Guid EmployeeId, Guid ConsentId, DateOnly NoticeAt) : ICommand<OvertimeConsentDto>;

public sealed class RevokeOvertimeConsentHandler(IDataScopeService dataScope, IOvertimeConsentRepository consents, ILegislationParameterReader legislation)
    : ICommandHandler<RevokeOvertimeConsentCommand, OvertimeConsentDto>
{
    public const int FallbackNoticeDays = 30;

    public async Task<Result<OvertimeConsentDto>> Handle(RevokeOvertimeConsentCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimeConsentsManage.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var consent = await consents.FindByIdAsync(command.ConsentId, cancellationToken);
        if (consent is null || consent.EmployeeId != command.EmployeeId)
        {
            return Error.NotFound(LeaveDomainErrors.OvertimeConsentNotFound);
        }

        var noticeDays = (int)await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.OvertimeConsentRevocationNoticeDays, command.NoticeAt, FallbackNoticeDays, cancellationToken);
        var revoked = consent.Revoke(command.NoticeAt, noticeDays);
        return revoked.IsFailure ? revoked.Error : OvertimeConsentMapper.ToDto(consent);
    }
}

/// <summary>
/// "Ekstra izne çevir": approved overtime → ExtraGrant ledger row. Minutes basis defaults to PayableMinutes; Full needs
/// leave.overtimes.override_payable. Days = round(basis × rate) / daily work minutes (placeholder 450 = 7.5 h, see OvertimeMath).
/// LeaveTypeId defaults to the Annual type of the employee's rule set.
/// </summary>
[RequiresPermission("leave.overtimes.convert")]
public sealed record ConvertOvertimeToTimeOffCommand(
    Guid EmployeeId, Guid OvertimeRequestId, decimal? Rate, OvertimeMinutesBasis MinutesBasis, Guid? LeaveTypeId, string? Note) : ICommand<OvertimeConversionDto>;

public sealed class ConvertOvertimeToTimeOffValidator : AbstractValidator<ConvertOvertimeToTimeOffCommand>
{
    public ConvertOvertimeToTimeOffValidator()
    {
        RuleFor(x => x.Rate).InclusiveBetween(OvertimeLimits.MinRate, OvertimeLimits.MaxRate).When(x => x.Rate is not null);
        RuleFor(x => x.Note).MaximumLength(LeaveLimits.ReasonMaxLength);
    }
}

public sealed class ConvertOvertimeToTimeOffHandler(
    ICurrentUser currentUser,
    IDataScopeService dataScope,
    IOvertimeRequestRepository requests,
    IOvertimeConversionRepository conversions,
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    ILeaveLedgerRepository ledger,
    ILegislationParameterReader legislation,
    IIntegrationEventOutbox outbox,
    TimeProvider clock)
    : ICommandHandler<ConvertOvertimeToTimeOffCommand, OvertimeConversionDto>
{
    /// <summary>m.41 varsayılan serbest zaman kullanım süresi (Overtime.CompTimeExpiryMonths seed'i olmadığında).</summary>
    public const int FallbackExpiryMonths = 6;

    public async Task<Result<OvertimeConversionDto>> Handle(ConvertOvertimeToTimeOffCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesConvert.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        if (command.MinutesBasis == OvertimeMinutesBasis.Full
            && !await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesOverridePayable.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.OverridePayableNotAllowed);
        }

        var request = await requests.FindByIdAsync(command.OvertimeRequestId, cancellationToken);
        if (request is null || request.EmployeeId != command.EmployeeId)
        {
            return Error.NotFound(LeaveDomainErrors.OvertimeNotFound);
        }

        var profile = await profiles.FindByEmployeeIdAsync(command.EmployeeId, cancellationToken);
        if (profile is null)
        {
            return Error.NotFound(LeaveErrors.ProfileNotFound);
        }

        var set = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var type = command.LeaveTypeId is { } typeId
            ? set.Value.Types.FirstOrDefault(t => t.Id == typeId)
            : set.Value.Types.FirstOrDefault(t => t.Code == LeaveTypeCode.Annual && !t.IsDisabled);
        if (type is null)
        {
            return Error.NotFound(LeaveErrors.TypeNotFound);
        }

        if (!type.HasBalance)
        {
            return Error.Validation(LeaveErrors.TypeHasNoBalance);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var expiryMonths = (int)await legislation.GetDecimalOrDefaultAsync(
            LegislationParameterKeys.OvertimeCompTimeExpiryMonths, today, FallbackExpiryMonths, cancellationToken);
        var usedByDate = today.AddMonths(expiryMonths);

        var conversion = OvertimeConversion.ToTimeOff(
            request, command.MinutesBasis, command.Rate ?? OvertimeMath.DefaultTimeOffRate, OvertimeMath.DefaultDailyWorkMinutes, type.Id, currentUser.UserId,
            now, command.Note, usedByDate);
        if (conversion.IsFailure)
        {
            return conversion.Error;
        }

        var grant = LeaveLedgerEntry.ExtraGrant(
            request.TenantId, request.EmployeeId, type.Id, conversion.Value.ResultDays, DateOnly.FromDateTime(now), LeaveLedgerSource.OvertimeConversion,
            conversion.Value.Id, command.Note);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var marked = request.MarkConverted(conversion.Value.Id);
        if (marked.IsFailure)
        {
            return marked.Error;
        }

        conversion.Value.AttachLedgerEntry(grant.Value.Id);
        conversions.Add(conversion.Value);
        ledger.AddRange([grant.Value]);
        var c = conversion.Value;
        outbox.Enqueue(new OvertimeConvertedToTimeOff(c.TenantId, request.Id, c.Id, c.EmployeeId, type.Id, c.MinutesBasis.ToString(), c.ResultMinutes, c.ResultDays));
        return OvertimeConsentMapper.ToDto(conversion.Value);
    }
}

/// <summary>
/// "Ödemeye çevir" (kart senseik-030): approved overtime → explicit Kind=Pay conversion, no ledger row. Money is not
/// computed here - Leave has no salary access (Modules_TalkOnlyThroughContracts); Payroll reads the payable minutes
/// through <c>IOvertimePayrollReader</c> during its own gross calculation (a never-converted request and a Kind=Pay one
/// land in the same cash bucket there - see that contract's doc comment). This command mainly (a) records HR's explicit
/// decision for audit and (b) blocks a later TimeOff conversion of the same request.
/// </summary>
[RequiresPermission("leave.overtimes.convert")]
public sealed record ConvertOvertimeToPayCommand(Guid EmployeeId, Guid OvertimeRequestId, OvertimeMinutesBasis MinutesBasis, string? Note) : ICommand<OvertimeConversionDto>;

public sealed class ConvertOvertimeToPayValidator : AbstractValidator<ConvertOvertimeToPayCommand>
{
    public ConvertOvertimeToPayValidator()
    {
        RuleFor(x => x.Note).MaximumLength(LeaveLimits.ReasonMaxLength);
    }
}

public sealed class ConvertOvertimeToPayHandler(
    ICurrentUser currentUser,
    IDataScopeService dataScope,
    IOvertimeRequestRepository requests,
    IOvertimeConversionRepository conversions,
    IIntegrationEventOutbox outbox,
    TimeProvider clock)
    : ICommandHandler<ConvertOvertimeToPayCommand, OvertimeConversionDto>
{
    public async Task<Result<OvertimeConversionDto>> Handle(ConvertOvertimeToPayCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesConvert.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        if (command.MinutesBasis == OvertimeMinutesBasis.Full
            && !await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesOverridePayable.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.OverridePayableNotAllowed);
        }

        var request = await requests.FindByIdAsync(command.OvertimeRequestId, cancellationToken);
        if (request is null || request.EmployeeId != command.EmployeeId)
        {
            return Error.NotFound(LeaveDomainErrors.OvertimeNotFound);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var conversion = OvertimeConversion.ToPay(request, command.MinutesBasis, currentUser.UserId, now, command.Note);
        if (conversion.IsFailure)
        {
            return conversion.Error;
        }

        var marked = request.MarkConverted(conversion.Value.Id);
        if (marked.IsFailure)
        {
            return marked.Error;
        }

        conversions.Add(conversion.Value);
        var c = conversion.Value;
        outbox.Enqueue(new OvertimeConvertedToPay(c.TenantId, request.Id, c.Id, c.EmployeeId, request.WorkDate, c.MinutesBasis.ToString(), c.BasisMinutes, c.RateSnapshot));
        return OvertimeConsentMapper.ToDto(conversion.Value);
    }
}

[RequiresPermission("leave.overtimes.read")]
public sealed record ListOvertimeConversionsQuery(Guid EmployeeId, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<OvertimeConversionDto>>;

public sealed class ListOvertimeConversionsHandler(IOvertimeConversionRepository conversions, IDataScopeService dataScope)
    : IQueryHandler<ListOvertimeConversionsQuery, IReadOnlyList<OvertimeConversionDto>>
{
    public async Task<Result<IReadOnlyList<OvertimeConversionDto>>> Handle(ListOvertimeConversionsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        IReadOnlyList<OvertimeConversionDto> list = (await conversions.ListByEmployeeAsync(query.EmployeeId, query.Take, cancellationToken)).Select(OvertimeConsentMapper.ToDto).ToList();
        return Result.Success(list);
    }
}

using FluentValidation;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Profiles;

public sealed record EmployeePayrollProfileDto(
    Guid Id,
    Guid EmployeeId,
    Guid SgkWorkplaceCompanyId,
    Guid? SgkWorkplaceLocationId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    WageBasis WageBasis,
    SgkStatus SgkStatus,
    string? IncentiveLawCode,
    MealBenefitVariant MealBenefitVariant,
    decimal MealDailyAmount,
    TransportVariant TransportVariant,
    PayrollPaymentMethod PaymentMethod,
    Guid? BankAccountId,
    bool SickDaysReducePay,
    string? ExternalPayrollCode,
    string? Notes,
    decimal TransportDailyAmount = 0m,
    decimal RegularBenefitsMonthlyGross = 0m);

internal static class EmployeePayrollProfileMapper
{
    public static EmployeePayrollProfileDto ToDto(EmployeePayrollProfile profile) => new(
        profile.Id, profile.EmployeeId, profile.SgkWorkplaceCompanyId, profile.SgkWorkplaceLocationId,
        profile.ValidFrom, profile.ValidTo, profile.WageBasis, profile.SgkStatus, profile.IncentiveLawCode,
        profile.MealBenefitVariant, profile.MealDailyAmount, profile.TransportVariant, profile.PaymentMethod,
        profile.BankAccountId, profile.SickDaysReducePay, profile.ExternalPayrollCode, profile.Notes, profile.TransportDailyAmount,
        profile.RegularBenefitsMonthlyGross);
}

/// <summary>Every dated version of an employee's payroll profile, newest ValidFrom first.</summary>
[RequiresPermission("payroll.profile.manage")]
public sealed record ListEmployeePayrollProfilesQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeePayrollProfileDto>>;

public sealed class ListEmployeePayrollProfilesHandler(IEmployeePayrollProfileRepository profiles)
    : IQueryHandler<ListEmployeePayrollProfilesQuery, IReadOnlyList<EmployeePayrollProfileDto>>
{
    public async Task<Result<IReadOnlyList<EmployeePayrollProfileDto>>> Handle(ListEmployeePayrollProfilesQuery query, CancellationToken cancellationToken)
    {
        var list = await profiles.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        return list.OrderByDescending(p => p.ValidFrom).Select(EmployeePayrollProfileMapper.ToDto).ToList();
    }
}

/// <summary>The employee's payroll profile version in effect on <paramref name="AsOf"/> (defaults to today, UTC).</summary>
[RequiresPermission("payroll.profile.manage")]
public sealed record GetCurrentEmployeePayrollProfileQuery(Guid EmployeeId, DateOnly? AsOf = null) : IQuery<EmployeePayrollProfileDto>;

public sealed class GetCurrentEmployeePayrollProfileHandler(IEmployeePayrollProfileRepository profiles)
    : IQueryHandler<GetCurrentEmployeePayrollProfileQuery, EmployeePayrollProfileDto>
{
    public async Task<Result<EmployeePayrollProfileDto>> Handle(GetCurrentEmployeePayrollProfileQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await profiles.FindCurrentAsync(query.EmployeeId, asOf, cancellationToken);
        return current is null ? Error.NotFound(PayrollDomainErrors.ProfileNotFound) : EmployeePayrollProfileMapper.ToDto(current);
    }
}

[RequiresPermission("payroll.profile.manage")]
public sealed record CreateEmployeePayrollProfileCommand(Guid EmployeeId, EmployeePayrollProfileData Data) : ICommand<Guid>;

public sealed class CreateEmployeePayrollProfileValidator : AbstractValidator<CreateEmployeePayrollProfileCommand>
{
    public CreateEmployeePayrollProfileValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

/// <summary>A profile create/update flags the employee dirty in every Open period covering (or after) the profile's
/// ValidFrom (11.3.07 akış §1 "değişiklik yeni sürüm → açık dönem bayraklanır"), via <see cref="PayslipDirtyFlagService"/>.</summary>
public sealed class CreateEmployeePayrollProfileHandler(ITenantContext tenant, IEmployeePayrollProfileRepository profiles, PayslipDirtyFlagService flagging)
    : ICommandHandler<CreateEmployeePayrollProfileCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateEmployeePayrollProfileCommand command, CancellationToken cancellationToken)
    {
        var existing = await profiles.ListByEmployeeAsync(command.EmployeeId, cancellationToken);
        if (existing.Any(p => p.Overlaps(command.Data.ValidFrom, command.Data.ValidTo)))
        {
            return Error.Conflict(PayrollDomainErrors.ProfileOverlap);
        }

        var created = EmployeePayrollProfile.Create(tenant.TenantId, command.EmployeeId, command.Data);
        if (created.IsFailure)
        {
            return created.Error;
        }

        profiles.Add(created.Value);
        await flagging.FlagAsync(tenant.TenantId, command.EmployeeId, command.Data.ValidFrom, PayslipDirtyFlagReasons.ProfileChanged, sourceEventId: null, cancellationToken);
        return created.Value.Id;
    }
}

[RequiresPermission("payroll.profile.manage")]
public sealed record UpdateEmployeePayrollProfileCommand(Guid ProfileId, EmployeePayrollProfileData Data, bool PreserveRegularBenefits = false) : ICommand;

[RequiresPermission("payroll.profile.manage")]
public sealed record UpdateEmployeeRegularBenefitsCommand(Guid EmployeeId, Guid ProfileId, decimal RegularBenefitsMonthlyGross) : ICommand;

public sealed class UpdateEmployeeRegularBenefitsHandler(
    ITenantContext tenant, IEmployeePayrollProfileRepository profiles, PayslipDirtyFlagService flagging)
    : ICommandHandler<UpdateEmployeeRegularBenefitsCommand>
{
    public async Task<Result> Handle(UpdateEmployeeRegularBenefitsCommand command, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindByIdAsync(command.ProfileId, cancellationToken);
        if (profile is null || profile.EmployeeId != command.EmployeeId || profile.TenantId != tenant.TenantId)
            return Error.NotFound(PayrollDomainErrors.ProfileNotFound);
        var updated = profile.SetRegularBenefitsMonthlyGross(command.RegularBenefitsMonthlyGross);
        if (updated.IsFailure) return updated.Error;
        if (updated.Value)
            await flagging.FlagAsync(tenant.TenantId, profile.EmployeeId, profile.ValidFrom, PayslipDirtyFlagReasons.ProfileChanged, null, cancellationToken);
        return Result.Success();
    }
}

public sealed class UpdateEmployeePayrollProfileHandler(ITenantContext tenant, IEmployeePayrollProfileRepository profiles, PayslipDirtyFlagService flagging)
    : ICommandHandler<UpdateEmployeePayrollProfileCommand>
{
    public async Task<Result> Handle(UpdateEmployeePayrollProfileCommand command, CancellationToken cancellationToken)
    {
        if (await profiles.FindByIdAsync(command.ProfileId, cancellationToken) is not { } profile)
        {
            return Error.NotFound(PayrollDomainErrors.ProfileNotFound);
        }

        var siblings = await profiles.ListByEmployeeAsync(profile.EmployeeId, cancellationToken);
        if (siblings.Any(p => p.Id != profile.Id && p.Overlaps(command.Data.ValidFrom, command.Data.ValidTo)))
        {
            return Error.Conflict(PayrollDomainErrors.ProfileOverlap);
        }

        var data = command.PreserveRegularBenefits
            ? command.Data with { RegularBenefitsMonthlyGross = profile.RegularBenefitsMonthlyGross }
            : command.Data;
        var updated = profile.Update(data);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (updated.Value)
        {
            await flagging.FlagAsync(tenant.TenantId, profile.EmployeeId, command.Data.ValidFrom, PayslipDirtyFlagReasons.ProfileChanged, sourceEventId: null, cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>Closes an open profile at <paramref name="ValidTo"/> (a replacing version normally starts the next day).</summary>
[RequiresPermission("payroll.profile.manage")]
public sealed record CloseEmployeePayrollProfileCommand(Guid ProfileId, DateOnly ValidTo) : ICommand;

public sealed class CloseEmployeePayrollProfileHandler(IEmployeePayrollProfileRepository profiles) : ICommandHandler<CloseEmployeePayrollProfileCommand>
{
    public async Task<Result> Handle(CloseEmployeePayrollProfileCommand command, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindByIdAsync(command.ProfileId, cancellationToken);
        return profile is null ? Error.NotFound(PayrollDomainErrors.ProfileNotFound) : profile.Close(command.ValidTo);
    }
}

[RequiresPermission("payroll.profile.manage")]
public sealed record DeleteEmployeePayrollProfileCommand(Guid ProfileId) : ICommand;

public sealed class DeleteEmployeePayrollProfileHandler(IEmployeePayrollProfileRepository profiles) : ICommandHandler<DeleteEmployeePayrollProfileCommand>
{
    public async Task<Result> Handle(DeleteEmployeePayrollProfileCommand command, CancellationToken cancellationToken)
    {
        if (await profiles.FindByIdAsync(command.ProfileId, cancellationToken) is not { } profile)
        {
            return Error.NotFound(PayrollDomainErrors.ProfileNotFound);
        }

        profiles.Remove(profile);
        return Result.Success();
    }
}

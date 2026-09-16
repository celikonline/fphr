using FluentValidation;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
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
    string? Notes);

internal static class EmployeePayrollProfileMapper
{
    public static EmployeePayrollProfileDto ToDto(EmployeePayrollProfile profile) => new(
        profile.Id, profile.EmployeeId, profile.SgkWorkplaceCompanyId, profile.SgkWorkplaceLocationId,
        profile.ValidFrom, profile.ValidTo, profile.WageBasis, profile.SgkStatus, profile.IncentiveLawCode,
        profile.MealBenefitVariant, profile.MealDailyAmount, profile.TransportVariant, profile.PaymentMethod,
        profile.BankAccountId, profile.SickDaysReducePay, profile.ExternalPayrollCode, profile.Notes);
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

public sealed class CreateEmployeePayrollProfileHandler(ITenantContext tenant, IEmployeePayrollProfileRepository profiles)
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
        return created.Value.Id;
    }
}

[RequiresPermission("payroll.profile.manage")]
public sealed record UpdateEmployeePayrollProfileCommand(Guid ProfileId, EmployeePayrollProfileData Data) : ICommand;

public sealed class UpdateEmployeePayrollProfileHandler(IEmployeePayrollProfileRepository profiles) : ICommandHandler<UpdateEmployeePayrollProfileCommand>
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

        return profile.Update(command.Data);
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

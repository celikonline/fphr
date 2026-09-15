using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Terminations;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Terminations;

/// <summary>
/// Adds a tenant-authored termination reason ("kiracı kendi nedenini oluşturur", 11.3.03). Code is the stable
/// identifier imports and reports match on, so it is tenant-unique and immutable afterwards.
/// </summary>
[RequiresPermission(EmployeePermissions.TerminationReasonManageKey)]
public sealed record CreateTerminationReasonCommand(
    string Code,
    string Name,
    string? DefaultSgkExitCode = null,
    TerminationType? DefaultTerminationType = null,
    int SortOrder = 0) : ICommand<TerminationReasonDto>;

/// <summary>Renames a reason and changes what it pre-fills; system rows may be edited too (each tenant owns its copy of the seed).</summary>
[RequiresPermission(EmployeePermissions.TerminationReasonManageKey)]
public sealed record UpdateTerminationReasonCommand(
    Guid Id,
    string Name,
    string? DefaultSgkExitCode = null,
    TerminationType? DefaultTerminationType = null,
    int SortOrder = 0) : ICommand<TerminationReasonDto>;

/// <summary>Hides a reason from the wizard; employees terminated with it keep referencing it (reasons are never deleted).</summary>
[RequiresPermission(EmployeePermissions.TerminationReasonManageKey)]
public sealed record DeactivateTerminationReasonCommand(Guid Id) : ICommand<TerminationReasonDto>;

[RequiresPermission(EmployeePermissions.TerminationReasonManageKey)]
public sealed record ReactivateTerminationReasonCommand(Guid Id) : ICommand<TerminationReasonDto>;

public sealed class CreateTerminationReasonValidator : AbstractValidator<CreateTerminationReasonCommand>
{
    public CreateTerminationReasonValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage(EmployeeDomainErrors.TerminationReasonCodeRequired)
            .MaximumLength(TerminationReasonLimits.CodeMaxLength).WithMessage(EmployeeDomainErrors.TerminationReasonCodeTooLong);
        RuleFor(x => x.Name).NotEmpty().WithMessage(EmployeeDomainErrors.TerminationReasonNameRequired)
            .MaximumLength(TerminationReasonLimits.NameMaxLength).WithMessage(EmployeeDomainErrors.TerminationReasonNameTooLong);
        RuleFor(x => x.DefaultSgkExitCode).MaximumLength(TerminationReasonLimits.SgkExitCodeMaxLength);
    }
}

public sealed class UpdateTerminationReasonValidator : AbstractValidator<UpdateTerminationReasonCommand>
{
    public UpdateTerminationReasonValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().WithMessage(EmployeeDomainErrors.TerminationReasonNameRequired)
            .MaximumLength(TerminationReasonLimits.NameMaxLength).WithMessage(EmployeeDomainErrors.TerminationReasonNameTooLong);
        RuleFor(x => x.DefaultSgkExitCode).MaximumLength(TerminationReasonLimits.SgkExitCodeMaxLength);
    }
}

public sealed class CreateTerminationReasonHandler(
    ITenantContext tenant,
    ITerminationReasonRepository reasons,
    TerminationReasonProvisioningService provisioning) : ICommandHandler<CreateTerminationReasonCommand, TerminationReasonDto>
{
    public async Task<Result<TerminationReasonDto>> Handle(CreateTerminationReasonCommand command, CancellationToken cancellationToken)
    {
        // Seed first: otherwise a tenant that never opened the list could take a code the seed is about to insert.
        await provisioning.EnsureSeededAsync(tenant.TenantId, cancellationToken);

        var code = command.Code.Trim();
        if (await reasons.CodeTakenAsync(code, exceptId: null, cancellationToken))
        {
            return Error.Conflict(EmployeeDomainErrors.TerminationReasonCodeTaken);
        }

        var created = TerminationReason.Create(
            tenant.TenantId, code, command.Name, command.DefaultSgkExitCode, command.DefaultTerminationType, isSystem: false, command.SortOrder);
        if (created.IsFailure)
        {
            return created.Error;
        }

        reasons.Add(created.Value);
        return TerminationReasonDto.From(created.Value);
    }
}

public sealed class UpdateTerminationReasonHandler(ITerminationReasonRepository reasons)
    : ICommandHandler<UpdateTerminationReasonCommand, TerminationReasonDto>
{
    public async Task<Result<TerminationReasonDto>> Handle(UpdateTerminationReasonCommand command, CancellationToken cancellationToken)
    {
        var found = await reasons.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var updated = found.Value.Update(command.Name, command.DefaultSgkExitCode, command.DefaultTerminationType, command.SortOrder);
        return updated.IsFailure ? updated.Error : TerminationReasonDto.From(found.Value);
    }
}

public sealed class DeactivateTerminationReasonHandler(ITerminationReasonRepository reasons)
    : ICommandHandler<DeactivateTerminationReasonCommand, TerminationReasonDto>
{
    public async Task<Result<TerminationReasonDto>> Handle(DeactivateTerminationReasonCommand command, CancellationToken cancellationToken)
    {
        var found = await reasons.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.Deactivate();
        return TerminationReasonDto.From(found.Value);
    }
}

public sealed class ReactivateTerminationReasonHandler(ITerminationReasonRepository reasons)
    : ICommandHandler<ReactivateTerminationReasonCommand, TerminationReasonDto>
{
    public async Task<Result<TerminationReasonDto>> Handle(ReactivateTerminationReasonCommand command, CancellationToken cancellationToken)
    {
        var found = await reasons.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.Reactivate();
        return TerminationReasonDto.From(found.Value);
    }
}

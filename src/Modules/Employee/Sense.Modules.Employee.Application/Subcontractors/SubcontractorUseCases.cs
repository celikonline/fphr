using FluentValidation;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Subcontractors;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Subcontractors;

/// <summary>A subcontractor company ("alt işveren / taşeron", E5b).</summary>
public sealed record SubcontractorDto(
    Guid Id,
    string Name,
    string? TaxNumber,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    DateOnly? ContractStartDate,
    DateOnly? ContractEndDate,
    bool IsActive)
{
    public static SubcontractorDto From(Subcontractor s) =>
        new(s.Id, s.Name, s.TaxNumber, s.ContactName, s.ContactEmail, s.ContactPhone, s.ContractStartDate, s.ContractEndDate, s.IsActive);
}

/// <summary>Subcontractor catalog; inactive companies are included only when asked.</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record ListSubcontractorsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<SubcontractorDto>>;

[RequiresPermission(EmployeePermissions.SubcontractorManageKey)]
public sealed record CreateSubcontractorCommand(SubcontractorData Data) : ICommand<SubcontractorDto>;

[RequiresPermission(EmployeePermissions.SubcontractorManageKey)]
public sealed record UpdateSubcontractorCommand(Guid Id, SubcontractorData Data) : ICommand<SubcontractorDto>;

[RequiresPermission(EmployeePermissions.SubcontractorManageKey)]
public sealed record SetSubcontractorActiveCommand(Guid Id, bool Active) : ICommand<SubcontractorDto>;

/// <summary>Links an employee to a subcontractor (null unlinks them, making them the tenant's own staff again).</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record LinkEmployeeSubcontractorCommand(Guid EmployeeId, Guid? SubcontractorId) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class CreateSubcontractorValidator : AbstractValidator<CreateSubcontractorCommand>
{
    public CreateSubcontractorValidator() =>
        RuleFor(x => x.Data.Name).NotEmpty().WithMessage(EmployeeDomainErrors.SubcontractorNameRequired)
            .MaximumLength(SubcontractorLimits.NameMaxLength).WithMessage(EmployeeDomainErrors.SubcontractorNameTooLong);
}

public sealed class UpdateSubcontractorValidator : AbstractValidator<UpdateSubcontractorCommand>
{
    public UpdateSubcontractorValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Data.Name).NotEmpty().WithMessage(EmployeeDomainErrors.SubcontractorNameRequired)
            .MaximumLength(SubcontractorLimits.NameMaxLength).WithMessage(EmployeeDomainErrors.SubcontractorNameTooLong);
    }
}

public sealed class ListSubcontractorsHandler(ISubcontractorRepository subcontractors)
    : IQueryHandler<ListSubcontractorsQuery, IReadOnlyList<SubcontractorDto>>
{
    public async Task<Result<IReadOnlyList<SubcontractorDto>>> Handle(ListSubcontractorsQuery query, CancellationToken cancellationToken)
    {
        var list = await subcontractors.ListAsync(query.IncludeInactive, cancellationToken);
        return Result.Success<IReadOnlyList<SubcontractorDto>>(list.Select(SubcontractorDto.From).ToList());
    }
}

public sealed class CreateSubcontractorHandler(ITenantContext tenant, ISubcontractorRepository subcontractors)
    : ICommandHandler<CreateSubcontractorCommand, SubcontractorDto>
{
    public Task<Result<SubcontractorDto>> Handle(CreateSubcontractorCommand command, CancellationToken cancellationToken)
    {
        var created = Subcontractor.Create(tenant.TenantId, command.Data);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<SubcontractorDto>(created.Error));
        }

        subcontractors.Add(created.Value);
        return Task.FromResult(Result.Success(SubcontractorDto.From(created.Value)));
    }
}

public sealed class UpdateSubcontractorHandler(ISubcontractorRepository subcontractors)
    : ICommandHandler<UpdateSubcontractorCommand, SubcontractorDto>
{
    public async Task<Result<SubcontractorDto>> Handle(UpdateSubcontractorCommand command, CancellationToken cancellationToken)
    {
        var found = await subcontractors.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var updated = found.Value.Update(command.Data);
        return updated.IsFailure ? updated.Error : SubcontractorDto.From(found.Value);
    }
}

public sealed class SetSubcontractorActiveHandler(ISubcontractorRepository subcontractors)
    : ICommandHandler<SetSubcontractorActiveCommand, SubcontractorDto>
{
    public async Task<Result<SubcontractorDto>> Handle(SetSubcontractorActiveCommand command, CancellationToken cancellationToken)
    {
        var found = await subcontractors.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (command.Active)
        {
            found.Value.Reactivate();
        }
        else
        {
            found.Value.Deactivate();
        }

        return SubcontractorDto.From(found.Value);
    }
}

public sealed class LinkEmployeeSubcontractorHandler(
    IEmployeeRepository employees,
    ISubcontractorRepository subcontractors,
    EmployeeDetailBuilder details) : ICommandHandler<LinkEmployeeSubcontractorCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(LinkEmployeeSubcontractorCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (command.SubcontractorId is { } subcontractorId)
        {
            var subcontractor = await subcontractors.GetByIdAsync(subcontractorId, cancellationToken);
            if (subcontractor.IsFailure)
            {
                return subcontractor.Error;
            }

            if (!subcontractor.Value.IsActive)
            {
                return Error.Rule(EmployeeDomainErrors.SubcontractorInactive);
            }
        }

        found.Value.LinkSubcontractor(command.SubcontractorId);
        return await details.BuildAsync(found.Value, cancellationToken);
    }
}

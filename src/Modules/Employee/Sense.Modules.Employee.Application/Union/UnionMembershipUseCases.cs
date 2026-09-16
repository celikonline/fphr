using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Union;

/// <summary>
/// A union membership as the card shows it ("Sendika bilgileri", E5b). The union name is decrypted here and the whole
/// DTO only reaches callers with employee.union.view (special-category data, KVKK md.6).
/// </summary>
public sealed record UnionMembershipDto(
    Guid Id,
    string UnionName,
    string? BusinessLineCode,
    DateOnly MembershipDate,
    DateOnly? EmployerNotifiedDate,
    UnionCoverageType CoverageType,
    bool IsUnionOfficer,
    DateOnly? OfficerSince,
    DateOnly? EndDate,
    bool IsCurrent);

/// <summary>Create/edit body for a union membership.</summary>
public sealed record UnionMembershipInput(
    string UnionName,
    DateOnly MembershipDate,
    UnionCoverageType CoverageType,
    string? BusinessLineCode = null,
    DateOnly? EmployerNotifiedDate = null,
    bool IsUnionOfficer = false,
    DateOnly? OfficerSince = null,
    DateOnly? EndDate = null);

/// <summary>Encrypts/decrypts union names and shapes the DTOs; the counterpart of PaymentsService for E5b.</summary>
public sealed class UnionMembershipService(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEncryptionProvider encryption,
    IIntegrationEventOutbox outbox)
{
    public UnionMembershipData ToData(UnionMembershipInput input) =>
        new(
            string.IsNullOrWhiteSpace(input.UnionName) ? string.Empty : encryption.Encrypt(input.UnionName.Trim()),
            input.MembershipDate,
            input.CoverageType,
            input.BusinessLineCode,
            input.EmployerNotifiedDate,
            input.IsUnionOfficer,
            input.OfficerSince,
            input.EndDate);

    public UnionMembershipDto ToDto(EmployeeUnionMembership membership) =>
        new(
            membership.Id,
            encryption.Decrypt(membership.ProtectedUnionName),
            membership.BusinessLineCode,
            membership.MembershipDate,
            membership.EmployerNotifiedDate,
            membership.CoverageType,
            membership.IsUnionOfficer,
            membership.OfficerSince,
            membership.EndDate,
            membership.IsCurrent);

    public IReadOnlyList<UnionMembershipDto> ToDtos(EmployeeEntity employee) =>
        employee.UnionTimeline.Select(ToDto).ToList();

    /// <summary>Union data changed; the event carries no values (special-category data), consumers re-read behind the permission.</summary>
    public void PublishChanged(Guid employeeId) =>
        outbox.Enqueue(new Contracts.Events.EmployeeProfileChanged(tenant.TenantId, employeeId, Contracts.Events.EmployeeProfileSections.Union)
        {
            ActorUserId = currentUser.UserId,
        });
}

/// <summary>Union memberships of an employee (newest first); out-of-scope targets return 404 (IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.UnionViewKey)]
public sealed record ListUnionMembershipsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<UnionMembershipDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class ListUnionMembershipsHandler(IEmployeeReadStore store, UnionMembershipService service)
    : IQueryHandler<ListUnionMembershipsQuery, IReadOnlyList<UnionMembershipDto>>
{
    public async Task<Result<IReadOnlyList<UnionMembershipDto>>> Handle(ListUnionMembershipsQuery query, CancellationToken cancellationToken)
    {
        var employee = await store.GetWithUnionAsync(query.EmployeeId, cancellationToken);
        return employee is null
            ? Error.NotFound(EmployeeErrors.EmployeeNotFound)
            : Result.Success(service.ToDtos(employee));
    }
}

[RequiresPermission(EmployeePermissions.UnionManageKey)]
public sealed record AddUnionMembershipCommand(Guid EmployeeId, UnionMembershipInput Input) : ICommand<IReadOnlyList<UnionMembershipDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

[RequiresPermission(EmployeePermissions.UnionManageKey)]
public sealed record UpdateUnionMembershipCommand(Guid EmployeeId, Guid MembershipId, UnionMembershipInput Input)
    : ICommand<IReadOnlyList<UnionMembershipDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

[RequiresPermission(EmployeePermissions.UnionManageKey)]
public sealed record DeleteUnionMembershipCommand(Guid EmployeeId, Guid MembershipId) : ICommand<IReadOnlyList<UnionMembershipDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class AddUnionMembershipValidator : AbstractValidator<AddUnionMembershipCommand>
{
    public AddUnionMembershipValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Input.UnionName).NotEmpty().WithMessage(EmployeeDomainErrors.UnionNameRequired)
            .MaximumLength(UnionLimits.UnionNameMaxLength);
    }
}

public sealed class UpdateUnionMembershipValidator : AbstractValidator<UpdateUnionMembershipCommand>
{
    public UpdateUnionMembershipValidator()
    {
        RuleFor(x => x.MembershipId).NotEmpty();
        RuleFor(x => x.Input.UnionName).NotEmpty().WithMessage(EmployeeDomainErrors.UnionNameRequired)
            .MaximumLength(UnionLimits.UnionNameMaxLength);
    }
}

public sealed class AddUnionMembershipHandler(IEmployeeRepository employees, UnionMembershipService service)
    : ICommandHandler<AddUnionMembershipCommand, IReadOnlyList<UnionMembershipDto>>
{
    public async Task<Result<IReadOnlyList<UnionMembershipDto>>> Handle(AddUnionMembershipCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithUnionAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var added = found.Value.AddUnionMembership(service.ToData(command.Input));
        if (added.IsFailure)
        {
            return added.Error;
        }

        service.PublishChanged(found.Value.Id);
        return Result.Success(service.ToDtos(found.Value));
    }
}

public sealed class UpdateUnionMembershipHandler(IEmployeeRepository employees, UnionMembershipService service)
    : ICommandHandler<UpdateUnionMembershipCommand, IReadOnlyList<UnionMembershipDto>>
{
    public async Task<Result<IReadOnlyList<UnionMembershipDto>>> Handle(UpdateUnionMembershipCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithUnionAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var updated = found.Value.UpdateUnionMembership(command.MembershipId, service.ToData(command.Input));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        service.PublishChanged(found.Value.Id);
        return Result.Success(service.ToDtos(found.Value));
    }
}

public sealed class DeleteUnionMembershipHandler(IEmployeeRepository employees, UnionMembershipService service)
    : ICommandHandler<DeleteUnionMembershipCommand, IReadOnlyList<UnionMembershipDto>>
{
    public async Task<Result<IReadOnlyList<UnionMembershipDto>>> Handle(DeleteUnionMembershipCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithUnionAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var deleted = found.Value.DeleteUnionMembership(command.MembershipId);
        if (deleted.IsFailure)
        {
            return deleted.Error;
        }

        service.PublishChanged(found.Value.Id);
        return Result.Success(service.ToDtos(found.Value));
    }
}

using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.Delegations;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Application.Delegations;

/// <summary>Delegation as returned by the API.</summary>
public sealed record DelegationDto(
    Guid Id, Guid DelegatorEmployeeId, Guid DelegateEmployeeId, DateOnly StartDate, DateOnly EndDate,
    IReadOnlyList<string> RequestTypes, bool IsRevoked, DateTime? RevokedAt,
    string? DelegatorName = null, string? DelegateName = null);

internal static class DelegationMapping
{
    public static DelegationDto ToDto(ApprovalDelegation d, IReadOnlyDictionary<Guid, string> names) =>
        new(d.Id, d.DelegatorEmployeeId, d.DelegateEmployeeId, d.StartDate, d.EndDate, d.RequestTypeList, d.IsRevoked, d.RevokedAt,
            names.GetValueOrDefault(d.DelegatorEmployeeId), names.GetValueOrDefault(d.DelegateEmployeeId));

    /// <summary>Employee display names for every delegator/delegate in the list; unresolved employees are simply absent.</summary>
    public static async Task<IReadOnlyList<DelegationDto>> ToDtosAsync(
        IReadOnlyList<ApprovalDelegation> list, Guid tenantId, IEmployeeDirectory directory, CancellationToken cancellationToken)
    {
        var ids = list.SelectMany(d => new[] { d.DelegatorEmployeeId, d.DelegateEmployeeId }).Distinct().ToList();
        var contacts = ids.Count == 0 ? [] : await directory.GetContactsAsync(tenantId, ids, cancellationToken);
        var names = contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());
        return list.Select(d => ToDto(d, names)).ToList();
    }
}

/// <summary>Shared creation logic for the own-delegation and admin (any delegator) commands.</summary>
internal static class DelegationCreation
{
    public static async Task<Result<Guid>> CreateAsync(
        ITenantContext tenant, ICurrentUser currentUser, IApprovalDelegationRepository delegations, IEmployeeLookup employees,
        IIntegrationEventOutbox outbox, Guid delegatorEmployeeId, Guid delegateEmployeeId, DateOnly startDate, DateOnly endDate,
        IReadOnlyCollection<string>? requestTypes, CancellationToken cancellationToken)
    {
        var created = ApprovalDelegation.Create(
            tenant.TenantId, delegatorEmployeeId, delegateEmployeeId, startDate, endDate, requestTypes, currentUser.EmployeeId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (!await employees.ExistsActiveAsync(tenant.TenantId, delegateEmployeeId, cancellationToken)
            || !await employees.ExistsActiveAsync(tenant.TenantId, delegatorEmployeeId, cancellationToken))
        {
            return Error.Validation(WorkflowErrors.DelegationEmployeeInvalid);
        }

        delegations.Add(created.Value);
        outbox.Enqueue(new ApprovalDelegationAssigned(
            tenant.TenantId, created.Value.Id, delegatorEmployeeId, delegateEmployeeId, startDate, endDate));
        return created.Value.Id;
    }
}

/// <summary>The caller delegates their own approval authority to another employee.</summary>
[RequiresPermission("workflow.my.delegation")]
public sealed record CreateMyDelegationCommand(Guid DelegateEmployeeId, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<string>? RequestTypes) : ICommand<Guid>;

public sealed class CreateMyDelegationValidator : AbstractValidator<CreateMyDelegationCommand>
{
    public CreateMyDelegationValidator()
    {
        RuleFor(x => x.DelegateEmployeeId).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
    }
}

public sealed class CreateMyDelegationHandler(
    ITenantContext tenant, ICurrentUser currentUser, IApprovalDelegationRepository delegations, IEmployeeLookup employees, IIntegrationEventOutbox outbox)
    : ICommandHandler<CreateMyDelegationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMyDelegationCommand command, CancellationToken cancellationToken) =>
        currentUser.EmployeeId is not { } me
            ? Error.Rule(WorkflowErrors.AssigneeMismatch)
            : await DelegationCreation.CreateAsync(
                tenant, currentUser, delegations, employees, outbox, me, command.DelegateEmployeeId,
                command.StartDate, command.EndDate, command.RequestTypes, cancellationToken);
}

/// <summary>Admin: creates a delegation on behalf of any employee.</summary>
[RequiresPermission("workflow.delegation.manage")]
public sealed record CreateDelegationCommand(Guid DelegatorEmployeeId, Guid DelegateEmployeeId, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<string>? RequestTypes) : ICommand<Guid>;

public sealed class CreateDelegationValidator : AbstractValidator<CreateDelegationCommand>
{
    public CreateDelegationValidator()
    {
        RuleFor(x => x.DelegatorEmployeeId).NotEmpty();
        RuleFor(x => x.DelegateEmployeeId).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
    }
}

public sealed class CreateDelegationHandler(
    ITenantContext tenant, ICurrentUser currentUser, IApprovalDelegationRepository delegations, IEmployeeLookup employees, IIntegrationEventOutbox outbox)
    : ICommandHandler<CreateDelegationCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateDelegationCommand command, CancellationToken cancellationToken) =>
        DelegationCreation.CreateAsync(
            tenant, currentUser, delegations, employees, outbox, command.DelegatorEmployeeId, command.DelegateEmployeeId,
            command.StartDate, command.EndDate, command.RequestTypes, cancellationToken);
}

/// <summary>The delegator revokes their own delegation; takes effect immediately, past decisions are untouched.</summary>
[RequiresPermission("workflow.my.delegation")]
public sealed record RevokeMyDelegationCommand(Guid DelegationId) : ICommand;

public sealed class RevokeMyDelegationHandler(
    ICurrentUser currentUser, IApprovalDelegationRepository delegations, TimeProvider clock) : ICommandHandler<RevokeMyDelegationCommand>
{
    public async Task<Result> Handle(RevokeMyDelegationCommand command, CancellationToken cancellationToken)
    {
        var loaded = await delegations.GetByIdAsync(command.DelegationId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        // Someone else's delegation is reported as not found rather than forbidden (no existence leak).
        if (loaded.Value.DelegatorEmployeeId != currentUser.EmployeeId)
        {
            return Error.NotFound(WorkflowDomainErrors.DelegationNotFound);
        }

        return loaded.Value.Revoke(clock.GetUtcNow().UtcDateTime);
    }
}

/// <summary>Admin: revokes any delegation.</summary>
[RequiresPermission("workflow.delegation.manage")]
public sealed record RevokeDelegationCommand(Guid DelegationId) : ICommand;

public sealed class RevokeDelegationHandler(IApprovalDelegationRepository delegations, TimeProvider clock) : ICommandHandler<RevokeDelegationCommand>
{
    public async Task<Result> Handle(RevokeDelegationCommand command, CancellationToken cancellationToken)
    {
        var loaded = await delegations.GetByIdAsync(command.DelegationId, cancellationToken);
        return loaded.IsFailure ? loaded.Error : loaded.Value.Revoke(clock.GetUtcNow().UtcDateTime);
    }
}

/// <summary>Delegations the caller gave or received.</summary>
[RequiresPermission("workflow.my.delegation")]
public sealed record ListMyDelegationsQuery : IQuery<IReadOnlyList<DelegationDto>>;

public sealed class ListMyDelegationsHandler(
    ITenantContext tenant, ICurrentUser currentUser, IApprovalDelegationRepository delegations, IEmployeeDirectory directory)
    : IQueryHandler<ListMyDelegationsQuery, IReadOnlyList<DelegationDto>>
{
    public async Task<Result<IReadOnlyList<DelegationDto>>> Handle(ListMyDelegationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } me)
        {
            return Result.Success<IReadOnlyList<DelegationDto>>([]);
        }

        var list = await delegations.ListInvolvingAsync(me, cancellationToken);
        return Result.Success(await DelegationMapping.ToDtosAsync(list, tenant.TenantId, directory, cancellationToken));
    }
}

/// <summary>Admin: every delegation in the tenant.</summary>
[RequiresPermission("workflow.delegation.manage")]
public sealed record ListDelegationsQuery : IQuery<IReadOnlyList<DelegationDto>>;

public sealed class ListDelegationsHandler(ITenantContext tenant, IApprovalDelegationRepository delegations, IEmployeeDirectory directory)
    : IQueryHandler<ListDelegationsQuery, IReadOnlyList<DelegationDto>>
{
    public async Task<Result<IReadOnlyList<DelegationDto>>> Handle(ListDelegationsQuery query, CancellationToken cancellationToken)
    {
        var list = await delegations.ListAllAsync(cancellationToken);
        return Result.Success(await DelegationMapping.ToDtosAsync(list, tenant.TenantId, directory, cancellationToken));
    }
}

using FluentValidation;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Contracts.Events;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.ApprovalPolicies;

/// <summary>One request type's approval policy on an org unit: its own assignment and the effective (possibly inherited) one.</summary>
/// <param name="RequestType">One of <see cref="ApprovalRequestTypes.All"/>.</param>
/// <param name="ApprovalPolicyId">The unit's own assignment; null = inherits.</param>
/// <param name="EffectiveApprovalPolicyId">Own assignment, else the nearest active ancestor's; null = Workflow default (first manager).</param>
/// <param name="InheritedFromOrgUnitId">The ancestor supplying <paramref name="EffectiveApprovalPolicyId"/>; null when own or none.</param>
public sealed record OrgUnitApprovalPolicyDto(string RequestType, Guid? ApprovalPolicyId, Guid? EffectiveApprovalPolicyId, Guid? InheritedFromOrgUnitId);

/// <summary>Desired own assignment for one request type; null policy clears it.</summary>
public sealed record OrgUnitApprovalPolicyAssignmentInput(string RequestType, Guid? ApprovalPolicyId);

/// <summary>Own and effective approval policy of the unit for every unit-assignable request type.</summary>
[RequiresPermission("organization.units.read")]
public sealed record GetOrgUnitApprovalPoliciesQuery(Guid OrgUnitId) : IQuery<IReadOnlyList<OrgUnitApprovalPolicyDto>>;

public sealed class GetOrgUnitApprovalPoliciesValidator : AbstractValidator<GetOrgUnitApprovalPoliciesQuery>
{
    public GetOrgUnitApprovalPoliciesValidator() => RuleFor(x => x.OrgUnitId).NotEmpty();
}

public sealed class GetOrgUnitApprovalPoliciesHandler(IOrgUnitRepository units, IOrgUnitApprovalPolicyAssignmentRepository assignments)
    : IQueryHandler<GetOrgUnitApprovalPoliciesQuery, IReadOnlyList<OrgUnitApprovalPolicyDto>>
{
    public async Task<Result<IReadOnlyList<OrgUnitApprovalPolicyDto>>> Handle(GetOrgUnitApprovalPoliciesQuery query, CancellationToken cancellationToken)
    {
        var context = await OrgUnitApprovalPolicyContext.LoadAsync(query.OrgUnitId, units, assignments, cancellationToken);
        return context is null
            ? Error.NotFound(OrganizationErrors.OrgUnitNotFound)
            : Result.Success(context.ToDtos());
    }
}

/// <summary>Sets or clears the unit's own approval policy per request type; request types not listed stay unchanged.</summary>
[RequiresPermission(OrganizationPermissions.ApprovalAssignmentsManageKey)]
public sealed record SetOrgUnitApprovalPoliciesCommand(Guid OrgUnitId, IReadOnlyList<OrgUnitApprovalPolicyAssignmentInput> Assignments)
    : ICommand<IReadOnlyList<OrgUnitApprovalPolicyDto>>;

public sealed class SetOrgUnitApprovalPoliciesValidator : AbstractValidator<SetOrgUnitApprovalPoliciesCommand>
{
    public SetOrgUnitApprovalPoliciesValidator()
    {
        RuleFor(x => x.OrgUnitId).NotEmpty();
        RuleFor(x => x.Assignments).NotNull();
        RuleFor(x => x.Assignments)
            .Must(list => list.Select(a => a.RequestType).Distinct(StringComparer.Ordinal).Count() == list.Count)
            .When(x => x.Assignments is not null)
            .WithMessage(OrganizationErrors.ApprovalAssignmentDuplicateRequestType);
        RuleForEach(x => x.Assignments).ChildRules(item =>
        {
            item.RuleFor(a => a.RequestType)
                .Must(type => type is not null && ApprovalRequestTypes.All.Contains(type, StringComparer.Ordinal))
                .WithMessage(OrganizationErrors.ApprovalAssignmentRequestTypeUnknown);
            item.RuleFor(a => a.ApprovalPolicyId)
                .Must(id => id != Guid.Empty)
                .WithMessage(OrganizationDomainErrors.ApprovalAssignmentPolicyRequired);
        });
    }
}

public sealed class SetOrgUnitApprovalPoliciesHandler(
    ITenantContext tenant,
    ICurrentUser user,
    IOrgUnitRepository units,
    IOrgUnitApprovalPolicyAssignmentRepository assignments,
    IIntegrationEventOutbox outbox) : ICommandHandler<SetOrgUnitApprovalPoliciesCommand, IReadOnlyList<OrgUnitApprovalPolicyDto>>
{
    public async Task<Result<IReadOnlyList<OrgUnitApprovalPolicyDto>>> Handle(SetOrgUnitApprovalPoliciesCommand command, CancellationToken cancellationToken)
    {
        var context = await OrgUnitApprovalPolicyContext.LoadAsync(command.OrgUnitId, units, assignments, cancellationToken);
        if (context is null)
        {
            return Error.NotFound(OrganizationErrors.OrgUnitNotFound);
        }

        if (!context.IsActive)
        {
            return Error.Rule(OrganizationErrors.ApprovalAssignmentOrgUnitInactive);
        }

        foreach (var input in command.Assignments)
        {
            var own = context.Own(input.RequestType);
            var changed = false;

            if (input.ApprovalPolicyId is not { } policyId)
            {
                if (own is not null)
                {
                    own.Clear();
                    assignments.Remove(own);
                    context.Assignments.Remove(own);
                    changed = true;
                }
            }
            else if (own is not null)
            {
                var reassigned = own.Reassign(policyId);
                if (reassigned.IsFailure)
                {
                    return reassigned.Error;
                }

                changed = reassigned.Value;
            }
            else
            {
                var created = OrgUnitApprovalPolicyAssignment.Create(tenant.TenantId, command.OrgUnitId, input.RequestType, policyId);
                if (created.IsFailure)
                {
                    return created.Error;
                }

                assignments.Add(created.Value);
                context.Assignments.Add(created.Value);
                changed = true;
            }

            if (changed)
            {
                outbox.Enqueue(new OrgUnitApprovalPolicyChanged(tenant.TenantId, command.OrgUnitId, input.RequestType, input.ApprovalPolicyId)
                {
                    ActorUserId = user.UserId,
                });
            }
        }

        return Result.Success(context.ToDtos());
    }
}

/// <summary>A unit, its active path to the root and the assignments on that path (tracked).</summary>
internal sealed class OrgUnitApprovalPolicyContext
{
    private OrgUnitApprovalPolicyContext(Guid orgUnitId, bool isActive, IReadOnlyList<Guid> activePath, List<OrgUnitApprovalPolicyAssignment> assignments)
    {
        OrgUnitId = orgUnitId;
        IsActive = isActive;
        ActivePath = activePath;
        Assignments = assignments;
    }

    public Guid OrgUnitId { get; }

    public bool IsActive { get; }

    public IReadOnlyList<Guid> ActivePath { get; }

    public List<OrgUnitApprovalPolicyAssignment> Assignments { get; }

    public static async Task<OrgUnitApprovalPolicyContext?> LoadAsync(
        Guid orgUnitId,
        IOrgUnitRepository units,
        IOrgUnitApprovalPolicyAssignmentRepository assignments,
        CancellationToken cancellationToken)
    {
        var all = await units.ListAllAsync(activeOnly: false, cancellationToken);
        var nodes = all.ToDictionary(u => u.Id, u => new OrgUnitNode(u.Id, u.ParentId, u.IsActive));
        if (!nodes.TryGetValue(orgUnitId, out var unit))
        {
            return null;
        }

        var path = ApprovalPolicyInheritance.ActivePath(orgUnitId, nodes);
        var loaded = await assignments.ListByOrgUnitsAsync([orgUnitId, .. path], cancellationToken);
        return new OrgUnitApprovalPolicyContext(orgUnitId, unit.IsActive, path, [.. loaded]);
    }

    public OrgUnitApprovalPolicyAssignment? Own(string requestType) =>
        Assignments.FirstOrDefault(a => a.OrgUnitId == OrgUnitId && string.Equals(a.RequestType, requestType, StringComparison.Ordinal));

    public IReadOnlyList<OrgUnitApprovalPolicyDto> ToDtos() =>
        ApprovalRequestTypes.All.Select(type =>
        {
            var effective = ApprovalPolicyInheritance.Resolve(ActivePath, Assignments, type);
            var inheritedFrom = effective is not null && effective.SourceOrgUnitId != OrgUnitId ? effective.SourceOrgUnitId : (Guid?)null;
            return new OrgUnitApprovalPolicyDto(type, Own(type)?.ApprovalPolicyId, effective?.ApprovalPolicyId, inheritedFrom);
        }).ToList();
}

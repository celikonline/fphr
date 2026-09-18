using FluentValidation;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Contracts;
// Not "using Sense.Modules.Employee.Contracts.Events" - it declares its own SuspensionReason/TerminationType
// (Contracts stays independent of Domain, per architecture rule), which would collide with Domain.Employees'
// versions below; event types (EmployeeHired/EmployeeSuspended/EmployeeReactivated/EmployeeTerminated) are
// referenced as Contracts.Events.* instead.
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Assigns one manager to every employee of a grid selection; per-item outcome (self → Skipped, cycle → Failed).</summary>
[RequiresPermission(EmployeePermissions.EmployeesBulkUpdateKey)]
public sealed record BulkAssignManagerCommand(EmployeeSelection Selection, Guid ManagerEmployeeId) : ICommand<BulkOperationResult>;

public sealed class BulkAssignManagerValidator : AbstractValidator<BulkAssignManagerCommand>
{
    public BulkAssignManagerValidator()
    {
        RuleFor(x => x.Selection).NotNull();
        RuleFor(x => x.ManagerEmployeeId).NotEmpty();
    }
}

public sealed class BulkAssignManagerHandler(
    ITenantContext tenant,
    IEmployeeRepository employees,
    EmployeeSelectionResolver resolver,
    CareerService career) : ICommandHandler<BulkAssignManagerCommand, BulkOperationResult>
{
    public async Task<Result<BulkOperationResult>> Handle(BulkAssignManagerCommand command, CancellationToken cancellationToken)
    {
        var managerId = command.ManagerEmployeeId;
        if (!await employees.ExistsActiveAsync(managerId, cancellationToken))
        {
            return Error.NotFound(EmployeeErrors.ManagerNotFound);
        }

        var resolved = await resolver.ResolveAsync(tenant.TenantId, command.Selection, EmployeePermissions.EmployeesReadKey, cancellationToken);

        // Explicit ids that were dropped (unknown or out of scope) are reported as failed rather than silently ignored.
        IReadOnlyList<Guid> requested = resolved;
        if (command.Selection.EmployeeIds is { } explicitIds)
        {
            var excluded = (command.Selection.ExcludedEmployeeIds ?? []).ToHashSet();
            requested = explicitIds.Distinct().Where(id => !excluded.Contains(id)).ToList();
        }

        var loaded = (await employees.ListByIdsAsync(resolved, cancellationToken)).ToDictionary(e => e.Id);
        var managerMap = new Dictionary<Guid, Guid?>(await employees.GetManagerMapAsync(cancellationToken));
        var placement = await career.LoadPlacementAsync(cancellationToken);
        var context = career.BuildContext(placement, managerMap);

        var items = new List<BulkItemResult>(requested.Count);
        foreach (var id in requested)
        {
            if (!loaded.TryGetValue(id, out var employee))
            {
                items.Add(new BulkItemResult(id, BulkItemOutcome.Failed, EmployeeErrors.EmployeeNotFound));
                continue;
            }

            if (id == managerId)
            {
                items.Add(new BulkItemResult(id, BulkItemOutcome.Skipped, EmployeeDomainErrors.ManagerSelf));
                continue;
            }

            // Same career rule as a single edit: amend a record starting today, otherwise a ManagerChange record effective today.
            career.Materialize(employee, placement);
            var latest = employee.CareerTimeline.LastOrDefault();
            var assigned = employee.ApplyPlacement(
                new PlacementRequest(
                    latest?.AssignmentInputs() ?? placement.ChainFor(employee.OrgUnitId),
                    managerId,
                    latest is null ? employee.EmploymentType : latest.EmploymentType,
                    CareerChangeType.ManagerChange),
                context);
            if (assigned.IsFailure)
            {
                items.Add(new BulkItemResult(id, BulkItemOutcome.Failed, assigned.Error.Code));
                continue;
            }

            career.Publish(employee, assigned.Value);
            managerMap[id] = employee.ManagerEmployeeId;
            items.Add(new BulkItemResult(id, BulkItemOutcome.Succeeded, null));
        }

        return BulkOperationResult.From(requested.Count, items);
    }
}

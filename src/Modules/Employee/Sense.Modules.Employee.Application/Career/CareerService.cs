using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using ContractEvents = Sense.Modules.Employee.Contracts.Events;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Career;

/// <summary>
/// Shared career write-side plumbing for every path that moves an employee (career endpoints, PUT /employees/{id},
/// bulk assign-manager, import): "today", the domain <see cref="CareerContext"/>, lazy materialization of the initial
/// record for employees that predate career history, assignment validation/scope and the integration events.
/// </summary>
/// <remarks>
/// "Today" is evaluated on every write (UTC, like the rest of the module). A record dated in the future therefore does
/// not change the denormalized OrgUnitId/ManagerEmployeeId/EmploymentType until something writes on or after its
/// ValidFrom; a daily recompute job that rolls future records over on their own date is future work (E-series/Leave).
/// </remarks>
public sealed class CareerService(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    OrgPlacementLoader placements,
    IDataScopeService dataScope,
    IIntegrationEventOutbox outbox,
    TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public Task<OrgPlacement> LoadPlacementAsync(CancellationToken ct) => placements.LoadAsync(ct);

    public async Task<CareerContext> BuildContextAsync(OrgPlacement placement, CancellationToken ct) =>
        new(Today, placement.LevelOrder, await employees.GetManagerMapAsync(ct).ConfigureAwait(false));

    public CareerContext BuildContext(OrgPlacement placement, IReadOnlyDictionary<Guid, Guid?> managerMap) =>
        new(Today, placement.LevelOrder, managerMap);

    /// <summary>
    /// Writes the initial Hire record of an employee that has none yet (created before career history existed, or by
    /// a seed/quick-add path) from the current denormalized values, so every later write edits real history.
    /// </summary>
    public void Materialize(EmployeeEntity employee, OrgPlacement placement) =>
        employee.EnsureInitialCareerRecord(placement.ChainFor(employee.OrgUnitId), Today);

    /// <summary>Every assignment must be an active unit of the level it is sent for; changed units must be inside the caller's accessible org units.</summary>
    public async Task<Result> ValidateAssignmentsAsync(
        IReadOnlyList<OrgAssignmentInput> assignments,
        CareerRecord? previous,
        OrgPlacement placement,
        string permission,
        CancellationToken ct)
    {
        if (assignments.Any(a => !placement.IsValidAssignment(a.OrgUnitTypeId, a.OrgUnitId)))
        {
            return Error.NotFound(EmployeeErrors.OrgUnitNotFound);
        }

        var accessible = await dataScope.GetAccessibleOrgUnitIdsAsync(permission, ct).ConfigureAwait(false);
        if (accessible is null)
        {
            return Result.Success();
        }

        // Levels the caller leaves untouched (e.g. a unit manager keeping the pre-filled Şirket/Şube of the employee's
        // previous record) stay valid even when those units are above their authorized subtree.
        var unchanged = previous?.AssignmentInputs() ?? [];
        var moved = assignments.Where(a => !unchanged.Any(u => u.OrgUnitTypeId == a.OrgUnitTypeId && u.OrgUnitId == a.OrgUnitId));
        return moved.Any(a => !accessible.Contains(a.OrgUnitId))
            ? Error.Forbidden(EmployeeErrors.OrgUnitOutOfScope)
            : Result.Success();
    }

    /// <summary>Enqueues CareerChanged (and ManagerChanged when the effective manager moved) for a career mutation; a no-op outcome publishes nothing.</summary>
    public void Publish(EmployeeEntity employee, CareerOutcome? outcome)
    {
        if (outcome is null || !outcome.Changed)
        {
            return;
        }

        PublishCareerChanged(employee, outcome.Record);
        if (outcome.ManagerTransition is { } transition)
        {
            PublishManagerChanged(employee, transition);
        }
    }

    /// <summary>Events for the initial Hire record of a newly created employee.</summary>
    public void PublishInitial(EmployeeEntity employee, CareerRecord? record)
    {
        if (record is null)
        {
            return;
        }

        PublishCareerChanged(employee, record);
        if (record.ManagerEmployeeId is not null)
        {
            PublishManagerChanged(employee, new ManagerTransition(null, record.ManagerEmployeeId, record.ValidFrom));
        }
    }

    private void PublishCareerChanged(EmployeeEntity employee, CareerRecord record) =>
        outbox.Enqueue(new ContractEvents.CareerChanged(tenant.TenantId, employee.Id, record.Id, ToContractChangeType(record.ChangeType), record.ValidFrom)
        {
            ActorUserId = currentUser.UserId,
        });

    private void PublishManagerChanged(EmployeeEntity employee, ManagerTransition transition) =>
        outbox.Enqueue(new ContractEvents.ManagerChanged(
            tenant.TenantId, employee.Id, transition.OldManagerEmployeeId, transition.NewManagerEmployeeId, transition.EffectiveFrom)
        {
            ActorUserId = currentUser.UserId,
        });

    public static ContractEvents.CareerChangeType ToContractChangeType(CareerChangeType changeType) => changeType switch
    {
        CareerChangeType.Hire => ContractEvents.CareerChangeType.Hire,
        CareerChangeType.Promotion => ContractEvents.CareerChangeType.Promotion,
        CareerChangeType.LateralMove => ContractEvents.CareerChangeType.LateralMove,
        CareerChangeType.Demotion => ContractEvents.CareerChangeType.Demotion,
        CareerChangeType.TitleChange => ContractEvents.CareerChangeType.TitleChange,
        CareerChangeType.ManagerChange => ContractEvents.CareerChangeType.ManagerChange,
        CareerChangeType.GroupTransfer => ContractEvents.CareerChangeType.GroupTransfer,
        CareerChangeType.BusinessTransfer => ContractEvents.CareerChangeType.BusinessTransfer,
        CareerChangeType.Rehire => ContractEvents.CareerChangeType.Rehire,
        _ => ContractEvents.CareerChangeType.Other,
    };
}

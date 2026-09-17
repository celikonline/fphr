using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// Implements IEmployeeProvisioning: creates employee records from candidates (ATS to HR core bridge).
/// Called when Recruitment raises JobApplicationStageChanged with Hired status (Faz 9).
/// <see cref="EmployeeEntity.Create"/> is called with NationalId if present (employee becomes Active),
/// or without HireDate if NationalId is missing (employee stays Draft).
/// Idempotency is intended via SourceApplicationId tracking, but Employee.Domain does not yet have that
/// field; once it exists (ADR-0022 migration exemption), the query will check for existing employee
/// and return its Guid without creating a duplicate. For now, this creates a new employee each call.
/// </summary>
public sealed class EmployeeProvisioning : IEmployeeProvisioning
{
    private readonly ITenantContext _tenant;
    private readonly IEmployeeRepository _employees;
    private readonly IIntegrationEventOutbox _outbox;

    public EmployeeProvisioning(
        ITenantContext tenant,
        IEmployeeRepository employees,
        IIntegrationEventOutbox outbox)
    {
        _tenant = tenant;
        _employees = employees;
        _outbox = outbox;
    }

    public async Task<Guid> CreateFromCandidateAsync(
        Guid tenantId,
        CandidateHireRequest request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Once Employee.Domain.Employee has a SourceApplicationId field (migration required, ADR-0022 exemption),
        // uncomment this idempotency check:
        // var existing = await _employees.FindBySourceApplicationIdAsync(tenantId, request.SourceApplicationId, cancellationToken);
        // if (existing is not null)
        // {
        //     return existing.Id;
        // }

        // If NationalId is missing, create the employee without a hire date (Draft status).
        // If NationalId is present, set the hire date so the employee becomes Active and EmployeeHired event is enqueued.
        var hireDate = request.NationalId is not null ? request.HireDate : (DateOnly?)null;

        var info = new EmployeeInfo(
            FirstName: request.FirstName,
            LastName: request.LastName,
            NationalId: request.NationalId,
            WorkEmail: request.Email,
            WorkPhone: request.Phone,
            HireDate: hireDate);

        var result = EmployeeEntity.Create(
            tenantId: tenantId,
            info: info,
            userId: null,
            orgUnitId: request.OrgUnitId,
            managerEmployeeId: null);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to create employee from candidate: {result.Error.Code}");
        }

        var employee = result.Value;
        _employees.Add(employee);
        await _employees.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // If NationalId was present and hire date was set, the employee transitioned to Active.
        // The Application layer raises EmployeeHired exactly once for that transition.
        if (hireDate.HasValue)
        {
            var leaveAccrualStartDate = employee.LeaveAccrualStartDate ?? hireDate.Value;
            _outbox.Enqueue(new Contracts.Events.EmployeeHired(
                tenantId,
                employee.Id,
                hireDate.Value,
                leaveAccrualStartDate));
        }

        return employee.Id;
    }
}

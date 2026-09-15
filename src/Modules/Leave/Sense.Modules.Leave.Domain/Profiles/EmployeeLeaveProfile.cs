using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Leave.Domain.Profiles;

/// <summary>
/// Leave-side view of an employee: assigned rule set and the dates the balance engine needs. Created from
/// Employee.Contracts.EmployeeHired; one per employee.
/// </summary>
public sealed class EmployeeLeaveProfile : TenantAggregateRoot<Guid>, IAuditLogged
{
    private EmployeeLeaveProfile()
    {
    }

    private EmployeeLeaveProfile(Guid tenantId, Guid employeeId, Guid ruleSetId, DateOnly hireDate, DateOnly leaveAccrualStartDate)
        : base(Guid.CreateVersion7(), tenantId)
    {
        EmployeeId = employeeId;
        RuleSetId = ruleSetId;
        HireDate = hireDate;
        LeaveAccrualStartDate = leaveAccrualStartDate;
    }

    public Guid EmployeeId { get; private set; }

    public Guid RuleSetId { get; private set; }

    public DateOnly HireDate { get; private set; }

    /// <summary>"İzne esas hak ediş tarihi" (defaults to HireDate; may be earlier for group transfers/rehires).</summary>
    public DateOnly LeaveAccrualStartDate { get; private set; }

    /// <summary>Needed for the ≤18/≥50 age exception; not exposed by Employee.Contracts yet, so null until it is.</summary>
    public DateOnly? BirthDate { get; private set; }

    public static EmployeeLeaveProfile Create(Guid tenantId, Guid employeeId, Guid ruleSetId, DateOnly hireDate, DateOnly? leaveAccrualStartDate) =>
        new(tenantId, employeeId, ruleSetId, hireDate, leaveAccrualStartDate ?? hireDate);

    public void AssignRuleSet(Guid ruleSetId) => RuleSetId = ruleSetId;

    public void SetDates(DateOnly hireDate, DateOnly leaveAccrualStartDate, DateOnly? birthDate)
    {
        HireDate = hireDate;
        LeaveAccrualStartDate = leaveAccrualStartDate;
        BirthDate = birthDate;
    }
}

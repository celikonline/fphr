using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Mentor assignment ("mentör atama", Faz 4 #72): a colleague guiding a new hire during onboarding, deliberately
/// independent of the manager/reporting hierarchy (<see cref="Employee.ManagerEmployeeId"/>) - no cycle check is
/// needed, only a self-assignment guard, since a mentor relationship is not transitive.
/// </summary>
public sealed partial class Employee
{
    /// <summary>Assigned mentor (another employee of the same tenant); never self. Same-module, FK-less cross-reference like <see cref="ManagerEmployeeId"/>.</summary>
    public Guid? MentorEmployeeId { get; private set; }

    /// <summary>Assigns (or clears, with null) the mentor. The Application layer checks the mentor employee exists and is active.</summary>
    public Result AssignMentor(Guid? mentorEmployeeId)
    {
        if (mentorEmployeeId == Id)
        {
            return Error.Rule(EmployeeDomainErrors.MentorSelf);
        }

        MentorEmployeeId = mentorEmployeeId;
        return Result.Success();
    }
}

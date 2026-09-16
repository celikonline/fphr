using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Union memberships ("Sendika bilgileri", 11.3.03 E5b). Like salary/bank records this collection is not auto-included:
/// only the union use cases load it, so special-category data never rides along with an ordinary employee read.
/// </summary>
public sealed partial class Employee
{
    private readonly List<EmployeeUnionMembership> _unionMemberships = [];

    /// <summary>Union memberships (EF navigation; use <see cref="UnionTimeline"/> for the ordered view).</summary>
    public IReadOnlyCollection<EmployeeUnionMembership> UnionMemberships => _unionMemberships.AsReadOnly();

    /// <summary>Memberships newest first; at most one of them is open.</summary>
    public IReadOnlyList<EmployeeUnionMembership> UnionTimeline =>
        _unionMemberships.OrderByDescending(m => m.MembershipDate).ThenByDescending(m => m.Id).ToList();

    public EmployeeUnionMembership? CurrentUnionMembership => _unionMemberships.FirstOrDefault(m => m.IsCurrent);

    public Result<EmployeeUnionMembership> AddUnionMembership(UnionMembershipData data)
    {
        var validated = EmployeeUnionMembership.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        // An employee belongs to one union at a time: end the previous membership before recording a new open one.
        if (data.EndDate is null && CurrentUnionMembership is not null)
        {
            return Error.Conflict(EmployeeDomainErrors.UnionMembershipAlreadyOpen);
        }

        var membership = new EmployeeUnionMembership(Guid.CreateVersion7(), TenantId, Id, data);
        _unionMemberships.Add(membership);
        return membership;
    }

    public Result<EmployeeUnionMembership> UpdateUnionMembership(Guid membershipId, UnionMembershipData data)
    {
        if (_unionMemberships.FirstOrDefault(m => m.Id == membershipId) is not { } membership)
        {
            return Error.NotFound(EmployeeDomainErrors.UnionMembershipNotFound);
        }

        var validated = EmployeeUnionMembership.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        if (data.EndDate is null && _unionMemberships.Any(m => m.Id != membershipId && m.IsCurrent))
        {
            return Error.Conflict(EmployeeDomainErrors.UnionMembershipAlreadyOpen);
        }

        membership.Apply(data);
        return membership;
    }

    public Result DeleteUnionMembership(Guid membershipId)
    {
        if (_unionMemberships.FirstOrDefault(m => m.Id == membershipId) is not { } membership)
        {
            return Error.NotFound(EmployeeDomainErrors.UnionMembershipNotFound);
        }

        _unionMemberships.Remove(membership);
        return Result.Success();
    }
}

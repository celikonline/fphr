using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// How the employee is covered by the collective agreement ("TİS kapsamı", 11.3.03 E5b): a member, a non-member paying
/// solidarity dues, someone the union extended coverage to, or explicitly out of scope (e.g. an employer representative).
/// </summary>
public enum UnionCoverageType
{
    Member = 0,
    SolidarityDues = 1,
    UnionApproved = 2,
    OutOfScope = 3,
}

/// <summary>
/// Editable content of a union membership. <see cref="ProtectedUnionName"/> is already ciphertext: union membership is
/// special-category personal data (KVKK md.6), so the Application layer encrypts the name before it reaches the domain.
/// </summary>
public sealed record UnionMembershipData(
    string ProtectedUnionName,
    DateOnly MembershipDate,
    UnionCoverageType CoverageType,
    string? BusinessLineCode = null,
    DateOnly? EmployerNotifiedDate = null,
    bool IsUnionOfficer = false,
    DateOnly? OfficerSince = null,
    DateOnly? EndDate = null);

/// <summary>
/// An employee's union membership (11.3.03 E5b). Child entity of the <see cref="Employee"/> aggregate, loaded only by
/// the union use cases; the name is stored as AES-GCM ciphertext and the whole record is gated behind
/// employee.union.view. Collective-agreement entitlement starts at <see cref="EmployerNotifiedDate"/>, not at
/// <see cref="MembershipDate"/>; union office (<see cref="IsUnionOfficer"/>) is what suspends an employee.
/// </summary>
public sealed class EmployeeUnionMembership : TenantEntity<Guid>
{
    internal EmployeeUnionMembership(Guid id, Guid tenantId, Guid employeeId, UnionMembershipData data)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Apply(data);
    }

    private EmployeeUnionMembership()
    {
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>AES-GCM ciphertext of the union's name.</summary>
    public string ProtectedUnionName { get; private set; } = string.Empty;

    /// <summary>"İş kolu" code of the union (6356 sayılı Kanun ek listesi); free text, no catalog in the codebase yet.</summary>
    public string? BusinessLineCode { get; private set; }

    public DateOnly MembershipDate { get; private set; }

    /// <summary>Date the employer was notified; collective-agreement entitlement runs from here.</summary>
    public DateOnly? EmployerNotifiedDate { get; private set; }

    public UnionCoverageType CoverageType { get; private set; }

    public bool IsUnionOfficer { get; private set; }

    public DateOnly? OfficerSince { get; private set; }

    /// <summary>Null while the membership is current.</summary>
    public DateOnly? EndDate { get; private set; }

    public bool IsCurrent => EndDate is null;

    internal void Apply(UnionMembershipData data)
    {
        ProtectedUnionName = data.ProtectedUnionName;
        BusinessLineCode = string.IsNullOrWhiteSpace(data.BusinessLineCode) ? null : data.BusinessLineCode.Trim();
        MembershipDate = data.MembershipDate;
        EmployerNotifiedDate = data.EmployerNotifiedDate;
        CoverageType = data.CoverageType;
        IsUnionOfficer = data.IsUnionOfficer;
        OfficerSince = data.IsUnionOfficer ? data.OfficerSince : null;
        EndDate = data.EndDate;
    }

    public static Result Validate(UnionMembershipData data)
    {
        if (string.IsNullOrWhiteSpace(data.ProtectedUnionName))
        {
            return Error.Validation(EmployeeDomainErrors.UnionNameRequired);
        }

        if (data.BusinessLineCode is { Length: > UnionLimits.BusinessLineCodeMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.UnionBusinessLineCodeTooLong);
        }

        if (data.EndDate is { } end && end < data.MembershipDate)
        {
            return Error.Validation(EmployeeDomainErrors.UnionEndDateBeforeMembership);
        }

        if (data.OfficerSince is { } officer && officer < data.MembershipDate)
        {
            return Error.Validation(EmployeeDomainErrors.UnionOfficerSinceBeforeMembership);
        }

        return Result.Success();
    }
}

public static class UnionLimits
{
    public const int UnionNameMaxLength = 200;
    public const int BusinessLineCodeMaxLength = 30;
}

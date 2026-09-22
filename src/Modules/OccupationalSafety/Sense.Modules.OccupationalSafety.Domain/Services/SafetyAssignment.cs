using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Domain.Services;

/// <summary>
/// İş güvenliği uzmanı / işyeri hekimi / DSP görevlendirmesi for a location (Organization, cross-module Guid). The person is
/// free text (often an OSGB employee, not on the payroll) with an optional employee link. A period is open until ended;
/// its contracted monthly minutes are compared with OhsServiceNorms by the location compliance query.
/// </summary>
public sealed class SafetyAssignment : TenantAggregateRoot<Guid>, IAuditLogged
{
    private SafetyAssignment()
    {
    }

    public Guid LocationId { get; private set; }
    public SafetyServiceRole Role { get; private set; }
    public string PersonName { get; private set; } = string.Empty;
    public Guid? EmployeeId { get; private set; }

    /// <summary>Safety experts only.</summary>
    public SafetyExpertCertificateClass? CertificateClass { get; private set; }

    public string? CertificateNumber { get; private set; }

    /// <summary>OSGB (ortak sağlık ve güvenlik birimi) name when the service is outsourced.</summary>
    public string? ProviderName { get; private set; }

    public int MonthlyMinutes { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    public bool IsActiveOn(DateOnly day) => StartDate <= day && (EndDate is null || EndDate >= day);

    public static Result<SafetyAssignment> Create(
        Guid tenantId, Guid locationId, SafetyServiceRole role, string personName, Guid? employeeId,
        SafetyExpertCertificateClass? certificateClass, string? certificateNumber, string? providerName,
        int monthlyMinutes, DateOnly startDate)
    {
        if (locationId == Guid.Empty)
        {
            return Error.Validation(OhsDomainErrors.AssignmentLocationRequired);
        }

        if (string.IsNullOrWhiteSpace(personName))
        {
            return Error.Validation(OhsDomainErrors.AssignmentPersonRequired);
        }

        if (monthlyMinutes <= 0)
        {
            return Error.Validation(OhsDomainErrors.AssignmentMinutesInvalid);
        }

        if (role == SafetyServiceRole.SafetyExpert && certificateClass is null)
        {
            return Error.Validation(OhsDomainErrors.AssignmentCertificateRequired);
        }

        return new SafetyAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            LocationId = locationId,
            Role = role,
            PersonName = Guard.MaxLength(personName.Trim(), OhsLimits.PersonNameMaxLength),
            EmployeeId = employeeId,
            CertificateClass = role == SafetyServiceRole.SafetyExpert ? certificateClass : null,
            CertificateNumber = Trim(certificateNumber),
            ProviderName = Trim(providerName),
            MonthlyMinutes = monthlyMinutes,
            StartDate = startDate,
        };
    }

    public Result End(DateOnly endDate)
    {
        if (EndDate is not null)
        {
            return Error.Conflict(OhsDomainErrors.AssignmentAlreadyEnded);
        }

        if (endDate < StartDate)
        {
            return Error.Validation(OhsDomainErrors.AssignmentEndBeforeStart);
        }

        EndDate = endDate;
        return Result.Success();
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), OhsLimits.PersonNameMaxLength);
}

/// <summary>İSG kurulu üyeliği for a location (6331 md.22); ends when the member leaves the committee.</summary>
public sealed class OhsCommitteeMember : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OhsCommitteeMember()
    {
    }

    public Guid LocationId { get; private set; }
    public CommitteeRole Role { get; private set; }
    public string PersonName { get; private set; } = string.Empty;
    public Guid? EmployeeId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    public bool IsActiveOn(DateOnly day) => StartDate <= day && (EndDate is null || EndDate >= day);

    public static Result<OhsCommitteeMember> Create(
        Guid tenantId, Guid locationId, CommitteeRole role, string personName, Guid? employeeId, DateOnly startDate)
    {
        if (locationId == Guid.Empty)
        {
            return Error.Validation(OhsDomainErrors.AssignmentLocationRequired);
        }

        if (string.IsNullOrWhiteSpace(personName))
        {
            return Error.Validation(OhsDomainErrors.AssignmentPersonRequired);
        }

        return new OhsCommitteeMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            LocationId = locationId,
            Role = role,
            PersonName = Guard.MaxLength(personName.Trim(), OhsLimits.PersonNameMaxLength),
            EmployeeId = employeeId,
            StartDate = startDate,
        };
    }

    public Result End(DateOnly endDate)
    {
        if (EndDate is not null)
        {
            return Error.Conflict(OhsDomainErrors.AssignmentAlreadyEnded);
        }

        if (endDate < StartDate)
        {
            return Error.Validation(OhsDomainErrors.AssignmentEndBeforeStart);
        }

        EndDate = endDate;
        return Result.Success();
    }
}

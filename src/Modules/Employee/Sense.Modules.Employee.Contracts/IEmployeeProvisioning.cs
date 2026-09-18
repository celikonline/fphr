namespace Sense.Modules.Employee.Contracts;

/// <summary>
/// Cross-module bridge for provisioning employees from candidates (ATS to HR core).
/// When Recruitment raises JobApplicationStageChanged with Hired status, this service creates
/// the employee record. Implemented by Sense.Modules.Employee.Application and registered by EmployeeModule.AddModule.
/// </summary>
public interface IEmployeeProvisioning
{
    /// <summary>
    /// Creates an employee record from a candidate hire request (from ATS JobApplication).
    ///
    /// The operation is idempotent on <paramref name="request"/>.SourceApplicationId:
    /// calling with the same SourceApplicationId twice returns the existing employee Guid
    /// without creating a duplicate.
    ///
    /// If <paramref name="request"/>.NationalId is null, the employee record is created
    /// in Draft status (no TCKN, hire date may be set but the profile is incomplete).
    ///
    /// Note: Implementation and DI registration occur in a later release; this version
    /// defines the contract only.
    /// </summary>
    /// <param name="tenantId">Tenant that owns both the application and the resulting employee.</param>
    /// <param name="request">Candidate-to-employee mapping: name, contact, identification, org context, and the source application id for idempotency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Guid of the created or found employee.</returns>
    Task<Guid> CreateFromCandidateAsync(
        Guid tenantId,
        CandidateHireRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to provision an employee from a candidate (Faz 9 JobApplication).
/// Positional record for forward compatibility: new fields append only.
/// </summary>
/// <param name="FirstName">Employee's first name (required).</param>
/// <param name="LastName">Employee's last name (required).</param>
/// <param name="Email">Employee's business email address (required).</param>
/// <param name="Phone">Employee's phone number (optional).</param>
/// <param name="NationalId">Employee's Turkish national ID (TCKN, optional; if null, employee is Draft).</param>
/// <param name="OrgUnitId">Org unit to assign the employee (optional; may be determined during onboarding).</param>
/// <param name="PositionTitle">Job position title or label (optional; formal Position record comes later).</param>
/// <param name="HireDate">Effective hire date (required).</param>
/// <param name="SourceApplicationId">Guid of the Recruitment JobApplication for idempotency and audit trail (required).</param>
public sealed record CandidateHireRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? NationalId,
    Guid? OrgUnitId,
    string? PositionTitle,
    DateOnly HireDate,
    Guid SourceApplicationId);

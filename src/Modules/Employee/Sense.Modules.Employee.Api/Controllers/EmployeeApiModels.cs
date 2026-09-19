using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.EmploymentPeriods;
using Sense.Modules.Employee.Application.Subcontractors;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Application.Union;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Subcontractors;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>Create/update body for an employee record.</summary>
/// <param name="ClearLeaveAccrualStartDate">
/// "İzne esas hak ediş tarihi" temizle (X) sinyali (senseik-044): true iken alan, HireDate'i takip eden varsayılan
/// mantığı atlayarak açıkça boşa (null) ayarlanır - <paramref name="LeaveAccrualStartDate"/>'in null gönderilmesi
/// zaten "dokunulmadı, otomatik takip devam etsin" anlamına geldiği için ayrı bir sinyale ihtiyaç var.
/// </param>
public sealed record EmployeeRequest(
    string FirstName,
    string LastName,
    string? NationalId,
    string? WorkEmail,
    string? PersonalEmail,
    string? WorkPhone,
    DateOnly? HireDate,
    DateOnly? BirthDate,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId,
    Gender Gender = Gender.Unspecified,
    MaritalStatus MaritalStatus = MaritalStatus.Unspecified,
    int? ChildCount = null,
    EducationStatus? EducationStatus = null,
    HighestEducationLevel? HighestEducationLevel = null,
    MilitaryStatus? MilitaryStatus = null,
    DisabilityDegree? DisabilityDegree = null,
    BloodType? BloodType = null,
    string? MobilePhone = null,
    string? HomePhone = null,
    string? AddressLine = null,
    string? City = null,
    string? PostalCode = null,
    ContractType? ContractType = null,
    EmploymentType? EmploymentType = null,
    DateOnly? LeaveAccrualStartDate = null,
    bool ClearLeaveAccrualStartDate = false);

/// <summary>"Pozisyon ekle" / edit body for a career record (changeType/employmentType as camelCase enum strings).</summary>
public sealed record CareerRecordRequest(
    DateOnly ValidFrom,
    CareerChangeType ChangeType,
    EmploymentType? EmploymentType,
    Guid? ManagerEmployeeId,
    string? Note,
    IReadOnlyList<OrgAssignmentDto>? Assignments);

/// <summary>Suspend body.</summary>
public sealed record SuspendEmployeeRequest(SuspensionReason Reason);

/// <summary>
/// Termination wizard's submit body ("İşten ayrılış sihirbazı"); TerminationType/SgkExitCode override the chosen
/// reason's defaults when sent. SeverancePaid/LeaveSettledAtExit are the exit settlement flags: they close the
/// employment period and decide whether it still counts towards a later rehire's seniority.
/// </summary>
public sealed record TerminateEmployeeRequest(
    DateOnly TerminationDate,
    Guid TerminationReasonId,
    TerminationType? TerminationType,
    string? SgkExitCode,
    bool SeverancePaid = false,
    bool LeaveSettledAtExit = false);

/// <summary>Termination reason create/edit body; Code is only read on create (it is immutable afterwards).</summary>
public sealed record TerminationReasonRequest(
    string? Code,
    string Name,
    string? DefaultSgkExitCode = null,
    TerminationType? DefaultTerminationType = null,
    int SortOrder = 0);

/// <summary>Body for linking an employee to a subcontractor; null unlinks them.</summary>
public sealed record LinkSubcontractorRequest(Guid? SubcontractorId);

/// <summary>"Yeniden işe al" body; MergePreviousPeriods carries the counted previous periods into the new leave accrual start.</summary>
public sealed record RehireEmployeeRequest(DateOnly HireDate, bool MergePreviousPeriods = true, DateOnly? LeaveAccrualStartDate = null);

/// <summary>Bulk-set body for custom field values: only the entries present change (DefinitionId -&gt; raw value, null clears it).</summary>
public sealed record SetCustomFieldsRequest(IReadOnlyDictionary<Guid, string?> Values);

/// <summary>Self-service profile edit body ("Self-servis düzenleme"): own contact fields + own employee-editable custom field values.</summary>
public sealed record UpdateMyProfileRequest(
    string? PersonalEmail,
    string? MobilePhone,
    string? HomePhone,
    string? AddressLine,
    string? City,
    string? PostalCode,
    IReadOnlyDictionary<Guid, string?>? CustomFieldValues);

/// <summary>Employee grid filters (query string), next to the grid-standard paging parameters.</summary>
public sealed record EmployeeListFilterRequest
{
    public Guid? OrgUnitId { get; init; }

    public bool IncludeSubUnits { get; init; }

    public string? Status { get; init; }

    public bool? HasUserAccount { get; init; }

    public Guid? ManagerEmployeeId { get; init; }

    public string? EmploymentType { get; init; }

    public string? ContractType { get; init; }

    /// <summary>"Eksik bilgi": true lists only records with an unfilled personnel field, false only complete ones.</summary>
    public bool? MissingInfo { get; init; }
}

public sealed record BulkAssignManagerRequest(EmployeeSelection Selection, Guid ManagerEmployeeId);

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>Create/update body for an employee record.</summary>
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
    DateOnly? LeaveAccrualStartDate = null);

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

/// <summary>Employee grid filters (query string), next to the grid-standard paging parameters.</summary>
public sealed record EmployeeListFilterRequest
{
    public Guid? OrgUnitId { get; init; }

    public bool IncludeSubUnits { get; init; }

    public string? Status { get; init; }

    public bool? HasUserAccount { get; init; }

    public Guid? ManagerEmployeeId { get; init; }
}

public sealed record BulkAssignManagerRequest(EmployeeSelection Selection, Guid ManagerEmployeeId);

/// <summary>Employee records (Faz 0 item 9): list/detail/create/update, bulk manager assignment, directory, org chart, birthdays.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employees")]
[Authorize]
public sealed class EmployeesController : ApiControllerBase
{
    /// <summary>Paged, searchable, sortable employee list filtered by the caller's data scope.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<EmployeeListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, [FromQuery] EmployeeListFilterRequest filter, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(
            new ListEmployeesQuery(paging, new EmployeeFilter(paging.Q, filter.OrgUnitId, filter.IncludeSubUnits, filter.Status, filter.HasUserAccount, filter.ManagerEmployeeId)),
            ct));

    /// <summary>The caller's own employee card (404 when the caller has no employee record).</summary>
    [HttpGet("me")]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyEmployeeQuery(), ct));

    /// <summary>The caller's own career timeline (read-only).</summary>
    [HttpGet("me/career")]
    [ProducesResponseType<IReadOnlyList<CareerRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyCareer(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyCareerQuery(), ct));

    /// <summary>Employee card (404 when missing or outside the caller's data scope).</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeQuery(id), ct));

    /// <summary>Creates an employee record.</summary>
    [HttpPost]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] EmployeeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateEmployeeCommand(request.FirstName, request.LastName, request.NationalId, request.WorkEmail, request.PersonalEmail,
                request.WorkPhone, request.HireDate, request.BirthDate, request.OrgUnitId, request.ManagerEmployeeId,
                request.Gender, request.MaritalStatus, request.ChildCount, request.EducationStatus, request.HighestEducationLevel,
                request.MilitaryStatus, request.DisabilityDegree, request.BloodType, request.MobilePhone, request.HomePhone,
                request.AddressLine, request.City, request.PostalCode, request.ContractType, request.EmploymentType, request.LeaveAccrualStartDate),
            ct));

    /// <summary>Updates an employee record.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] EmployeeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateEmployeeCommand(id, request.FirstName, request.LastName, request.NationalId, request.WorkEmail, request.PersonalEmail,
                request.WorkPhone, request.HireDate, request.BirthDate, request.OrgUnitId, request.ManagerEmployeeId,
                request.Gender, request.MaritalStatus, request.ChildCount, request.EducationStatus, request.HighestEducationLevel,
                request.MilitaryStatus, request.DisabilityDegree, request.BloodType, request.MobilePhone, request.HomePhone,
                request.AddressLine, request.City, request.PostalCode, request.ContractType, request.EmploymentType, request.LeaveAccrualStartDate),
            ct));

    /// <summary>Career timeline, newest first (an employee without stored history shows its current values as one implicit Hire record).</summary>
    [HttpGet(ApiRoutes.IdParam + "/career")]
    [ProducesResponseType<IReadOnlyList<CareerRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Career(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeCareerQuery(id), ct));

    /// <summary>"Pozisyon ekle": adds a career record (closes the previous one); returns the updated timeline.</summary>
    [HttpPost(ApiRoutes.IdParam + "/career")]
    [ProducesResponseType<IReadOnlyList<CareerRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddCareerRecord(Guid id, [FromBody] CareerRecordRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddCareerRecordCommand(id, request.ValidFrom, request.ChangeType, request.EmploymentType, request.ManagerEmployeeId, request.Note, request.Assignments ?? []),
            ct));

    /// <summary>Edits a career record (only the latest may change its start date); returns the updated timeline.</summary>
    [HttpPut(ApiRoutes.IdParam + "/career/{recordId:guid}")]
    [ProducesResponseType<IReadOnlyList<CareerRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCareerRecord(Guid id, Guid recordId, [FromBody] CareerRecordRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateCareerRecordCommand(id, recordId, request.ValidFrom, request.ChangeType, request.EmploymentType, request.ManagerEmployeeId, request.Note, request.Assignments ?? []),
            ct));

    /// <summary>Deletes the latest career record and reopens the previous one; returns the updated timeline.</summary>
    [HttpDelete(ApiRoutes.IdParam + "/career/{recordId:guid}")]
    [ProducesResponseType<IReadOnlyList<CareerRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteCareerRecord(Guid id, Guid recordId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteCareerRecordCommand(id, recordId), ct));

    /// <summary>Suspends an Active employee (askıya alma).</summary>
    [HttpPost(ApiRoutes.IdParam + "/suspend")]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suspend(Guid id, [FromBody] SuspendEmployeeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SuspendEmployeeCommand(id, request.Reason), ct));

    /// <summary>Reactivates a Suspended employee back to Active.</summary>
    [HttpPost(ApiRoutes.IdParam + "/reactivate")]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReactivateEmployeeCommand(id), ct));

    /// <summary>Assigns a manager to every employee in the selection; per-item outcome.</summary>
    [HttpPost("bulk/assign-manager")]
    [ProducesResponseType<BulkOperationResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkAssignManager([FromBody] BulkAssignManagerRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new BulkAssignManagerCommand(request.Selection, request.ManagerEmployeeId), ct));

    /// <summary>Company directory of active employees.</summary>
    [HttpGet("directory")]
    [ProducesResponseType<PagedResult<DirectoryEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Directory([FromQuery] PagedQuery paging, [FromQuery] Guid? orgUnitId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeDirectoryQuery(paging, orgUnitId), ct));

    /// <summary>Organization chart by manager hierarchy.</summary>
    [HttpGet("org-chart")]
    [ProducesResponseType<IReadOnlyList<OrgChartNodeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OrgChart(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetOrgChartQuery(), ct));

    /// <summary>Active employees with a birthday in the next <paramref name="days"/> days.</summary>
    [HttpGet("upcoming-birthdays")]
    [ProducesResponseType<IReadOnlyList<UpcomingBirthdayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpcomingBirthdays([FromQuery] int days = GetUpcomingBirthdaysQuery.DefaultDays, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new GetUpcomingBirthdaysQuery(days), ct));
}

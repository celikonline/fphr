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
            new ListEmployeesQuery(paging, new EmployeeFilter(
                paging.Q, filter.OrgUnitId, filter.IncludeSubUnits, filter.Status, filter.HasUserAccount, filter.ManagerEmployeeId,
                filter.EmploymentType, filter.ContractType, filter.MissingInfo)),
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

    /// <summary>Self-service edit ("Self-servis düzenleme"): own contact fields + own employee-editable custom field values.</summary>
    [HttpPut("me/profile")]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateMyProfileRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateMyProfileCommand(request.PersonalEmail, request.MobilePhone, request.HomePhone, request.AddressLine, request.City, request.PostalCode, request.CustomFieldValues),
            ct));

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

    /// <summary>Termination reason catalog for the wizard's dropdown (system-seeded per tenant on first use).</summary>
    [HttpGet("termination-reasons")]
    [ProducesResponseType<IReadOnlyList<TerminationReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TerminationReasons([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTerminationReasonsQuery(includeInactive), ct));

    /// <summary>
    /// Terminates an Active or Suspended employee ("İşten ayrılış sihirbazı", E5a). Returns the updated card plus an
    /// advisory job-security warning when applicable - the warning never blocks the termination.
    /// </summary>
    [HttpPost(ApiRoutes.IdParam + "/terminate")]
    [ProducesResponseType<TerminateEmployeeResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Terminate(Guid id, [FromBody] TerminateEmployeeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new TerminateEmployeeCommand(
                id,
                request.TerminationDate,
                request.TerminationReasonId,
                request.TerminationType,
                request.SgkExitCode,
                request.SeverancePaid,
                request.LeaveSettledAtExit),
            ct));

    /// <summary>Adds a tenant-authored termination reason.</summary>
    [HttpPost("termination-reasons")]
    [ProducesResponseType<TerminationReasonDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTerminationReason([FromBody] TerminationReasonRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateTerminationReasonCommand(request.Code ?? string.Empty, request.Name, request.DefaultSgkExitCode, request.DefaultTerminationType, request.SortOrder),
            ct));

    /// <summary>Renames a termination reason and changes what it pre-fills (its code never changes).</summary>
    [HttpPut("termination-reasons/{reasonId:guid}")]
    [ProducesResponseType<TerminationReasonDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTerminationReason(Guid reasonId, [FromBody] TerminationReasonRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateTerminationReasonCommand(reasonId, request.Name, request.DefaultSgkExitCode, request.DefaultTerminationType, request.SortOrder),
            ct));

    /// <summary>Hides a termination reason from the wizard (reasons are never deleted).</summary>
    [HttpPost("termination-reasons/{reasonId:guid}/deactivate")]
    [ProducesResponseType<TerminationReasonDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateTerminationReason(Guid reasonId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateTerminationReasonCommand(reasonId), ct));

    /// <summary>Brings a deactivated termination reason back.</summary>
    [HttpPost("termination-reasons/{reasonId:guid}/reactivate")]
    [ProducesResponseType<TerminationReasonDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReactivateTerminationReason(Guid reasonId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReactivateTerminationReasonCommand(reasonId), ct));

    /// <summary>
    /// Collective-dismissal status of the rolling 30-day window ("Toplu işten çıkarma eşik uyarısı"): what the wizard
    /// shows before a termination is submitted. Advisory - it never blocks a termination.
    /// </summary>
    [HttpGet("collective-dismissal-status")]
    [ProducesResponseType<CollectiveDismissalEvaluation>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CollectiveDismissalStatus([FromQuery] DateOnly? asOf, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetCollectiveDismissalStatusQuery(asOf), ct));

    /// <summary>Subcontractor ("alt işveren") catalog of the tenant.</summary>
    [HttpGet("subcontractors")]
    [ProducesResponseType<IReadOnlyList<SubcontractorDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Subcontractors([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListSubcontractorsQuery(includeInactive), ct));

    /// <summary>Adds a subcontractor company.</summary>
    [HttpPost("subcontractors")]
    [ProducesResponseType<SubcontractorDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSubcontractor([FromBody] SubcontractorData request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateSubcontractorCommand(request), ct));

    /// <summary>Edits a subcontractor company.</summary>
    [HttpPut("subcontractors/{subcontractorId:guid}")]
    [ProducesResponseType<SubcontractorDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubcontractor(Guid subcontractorId, [FromBody] SubcontractorData request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateSubcontractorCommand(subcontractorId, request), ct));

    /// <summary>Hides a subcontractor from the pickers (rows are never deleted - employees reference them).</summary>
    [HttpPost("subcontractors/{subcontractorId:guid}/deactivate")]
    [ProducesResponseType<SubcontractorDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateSubcontractor(Guid subcontractorId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetSubcontractorActiveCommand(subcontractorId, Active: false), ct));

    /// <summary>Brings a deactivated subcontractor back.</summary>
    [HttpPost("subcontractors/{subcontractorId:guid}/reactivate")]
    [ProducesResponseType<SubcontractorDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReactivateSubcontractor(Guid subcontractorId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetSubcontractorActiveCommand(subcontractorId, Active: true), ct));

    /// <summary>Links the employee to a subcontractor, or unlinks them with a null id.</summary>
    [HttpPut(ApiRoutes.IdParam + "/subcontractor")]
    [ProducesResponseType<EmployeeDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LinkSubcontractor(Guid id, [FromBody] LinkSubcontractorRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new LinkEmployeeSubcontractorCommand(id, request.SubcontractorId), ct));

    /// <summary>Union memberships of an employee ("Sendika bilgileri"); special-category data behind employee.union.view.</summary>
    [HttpGet(ApiRoutes.IdParam + "/union-memberships")]
    [ProducesResponseType<IReadOnlyList<UnionMembershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnionMemberships(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListUnionMembershipsQuery(id), ct));

    /// <summary>Records a union membership; an employee may have only one open membership at a time.</summary>
    [HttpPost(ApiRoutes.IdParam + "/union-memberships")]
    [ProducesResponseType<IReadOnlyList<UnionMembershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddUnionMembership(Guid id, [FromBody] UnionMembershipInput request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddUnionMembershipCommand(id, request), ct));

    /// <summary>Edits a union membership (e.g. ending it or recording the employer notification date).</summary>
    [HttpPut(ApiRoutes.IdParam + "/union-memberships/{membershipId:guid}")]
    [ProducesResponseType<IReadOnlyList<UnionMembershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUnionMembership(Guid id, Guid membershipId, [FromBody] UnionMembershipInput request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateUnionMembershipCommand(id, membershipId, request), ct));

    /// <summary>Deletes a union membership recorded by mistake.</summary>
    [HttpDelete(ApiRoutes.IdParam + "/union-memberships/{membershipId:guid}")]
    [ProducesResponseType<IReadOnlyList<UnionMembershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteUnionMembership(Guid id, Guid membershipId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteUnionMembershipCommand(id, membershipId), ct));

    /// <summary>Employment periods and merged seniority of an employee ("Çalışma dönemleri ve kıdem birleştirme").</summary>
    [HttpGet(ApiRoutes.IdParam + "/employment-periods")]
    [ProducesResponseType<EmploymentHistoryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EmploymentPeriods(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmploymentHistoryQuery(id), ct));

    /// <summary>Re-hires a terminated employee ("Yeniden işe al"); returns the refreshed card with the new period and merged seniority.</summary>
    [HttpPost(ApiRoutes.IdParam + "/rehire")]
    [ProducesResponseType<RehireEmployeeResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rehire(Guid id, [FromBody] RehireEmployeeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new RehireEmployeeCommand(id, request.HireDate, request.MergePreviousPeriods, request.LeaveAccrualStartDate),
            ct));

    /// <summary>
    /// HR/admin write of custom field values: only the entries present in the body change, any active definition may
    /// be set (a sensitive one is preserved without employee.sensitive.view). Returns the employee's full custom field list.
    /// </summary>
    [HttpPut(ApiRoutes.IdParam + "/custom-fields")]
    [ProducesResponseType<IReadOnlyList<CustomFieldValueDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCustomFields(Guid id, [FromBody] SetCustomFieldsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetEmployeeCustomFieldsCommand(id, request.Values), ct));

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

    /// <summary>Active employee headcount grouped by Department-level org unit.</summary>
    [HttpGet("department-distribution")]
    [ProducesResponseType<IReadOnlyList<DepartmentDistributionItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DepartmentDistribution(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetDepartmentDistributionQuery(), ct));
}

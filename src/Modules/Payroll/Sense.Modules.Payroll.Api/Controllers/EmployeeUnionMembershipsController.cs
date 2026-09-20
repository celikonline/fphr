using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Unions;
using Sense.Modules.Payroll.Domain.Unions;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Create-or-update body for an employee's union membership (11.3.07 #58 "Sendika / TİS"). Null
/// <see cref="MembershipId"/> creates a new record; a set one edits that record in place.</summary>
public sealed record EmployeeUnionMembershipRequest(
    Guid? MembershipId,
    Guid UnionId,
    DateOnly MembershipDate,
    DateOnly NotifiedToEmployerAt,
    UnionCoverageType CoverageType,
    UnionDuesRule DuesRule,
    decimal DuesValue,
    DateOnly? EndDate);

/// <summary>Çalışan kartı Ücret sekmesi > "Sendika" (11.3.07 #58): the employee's union/TİS membership history.
/// API only - the screen itself is P8. All actions require payroll.union.manage.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/employees/{employeeId:guid}/union-memberships")]
[Authorize]
public sealed class EmployeeUnionMembershipsController : ApiControllerBase
{
    /// <summary>The membership in effect on <paramref name="asOf"/> (defaults to today).</summary>
    [HttpGet("current")]
    [ProducesResponseType<EmployeeUnionMembershipDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent(Guid employeeId, [FromQuery] DateOnly? asOf, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetCurrentEmployeeUnionMembershipQuery(employeeId, asOf), ct));

    /// <summary>Every membership record of the employee, newest MembershipDate first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EmployeeUnionMembershipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeUnionMembershipsQuery(employeeId), ct));

    /// <summary>Creates a new membership record (no MembershipId) or edits an existing one (MembershipId set); a
    /// create with no EndDate fails if the employee already has an open membership (payroll.errors.union_membership_already_open).</summary>
    [HttpPut]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Upsert(Guid employeeId, [FromBody] EmployeeUnionMembershipRequest request, CancellationToken ct)
    {
        var data = new EmployeeUnionMembershipData(
            request.UnionId, request.MembershipDate, request.NotifiedToEmployerAt, request.CoverageType, request.DuesRule, request.DuesValue, request.EndDate);

        return request.MembershipId is { } membershipId
            ? FromResult(await Dispatcher.Send(new UpdateEmployeeUnionMembershipCommand(membershipId, data), ct))
            : FromResult(await Dispatcher.Send(new CreateEmployeeUnionMembershipCommand(employeeId, data), ct));
    }

    [HttpPost("{membershipId:guid}/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> End(Guid membershipId, [FromBody] DateOnly endDate, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new EndEmployeeUnionMembershipCommand(membershipId, endDate), ct));
}

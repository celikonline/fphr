using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Create-or-update body for an employee's payroll profile version (11.3.07 "Bordro parametresi", P2).
/// Null <see cref="ProfileId"/> opens a new version (payroll.profile.manage); a set one edits that version in place.</summary>
public sealed record PayrollProfileRequest(
    Guid? ProfileId,
    Guid SgkWorkplaceCompanyId,
    Guid? SgkWorkplaceLocationId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    WageBasis WageBasis,
    SgkStatus SgkStatus,
    string? IncentiveLawCode,
    MealBenefitVariant MealBenefitVariant,
    decimal MealDailyAmount,
    TransportVariant TransportVariant,
    PayrollPaymentMethod PaymentMethod,
    Guid? BankAccountId,
    bool SickDaysReducePay,
    string? ExternalPayrollCode,
    string? Notes,
    decimal TransportDailyAmount = 0m);

/// <summary>
/// Employee card > Ücret sekmesi > "Bordro parametreleri" (11.3.07 P2): the employee's dated
/// EmployeePayrollProfile versions. API only - the screen itself is P8. All actions require payroll.profile.manage
/// (enforced on the commands/queries themselves via [RequiresPermission], mirroring CompaniesController).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/employees/{employeeId:guid}/profile")]
[Authorize]
public sealed class EmployeePayrollProfilesController : ApiControllerBase
{
    /// <summary>The profile version in effect on <paramref name="asOf"/> (defaults to today).</summary>
    [HttpGet]
    [ProducesResponseType<EmployeePayrollProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent(Guid employeeId, [FromQuery] DateOnly? asOf, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetCurrentEmployeePayrollProfileQuery(employeeId, asOf), ct));

    /// <summary>Every dated version of the employee's profile, newest ValidFrom first.</summary>
    [HttpGet("history")]
    [ProducesResponseType<IReadOnlyList<EmployeePayrollProfileDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeePayrollProfilesQuery(employeeId), ct));

    /// <summary>APHB/e-Bildirge pre-fill data (senseik-010/F-03): SGK işyeri sicil no, meslek kodu, teşvik kodu -
    /// the export itself is out of scope, this only surfaces the stored values.</summary>
    [HttpGet("aphb-prefill")]
    [ProducesResponseType<AphbPrefillDataDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAphbPrefill(Guid employeeId, [FromQuery] DateOnly? asOf, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAphbPrefillDataQuery(employeeId, asOf), ct));

    /// <summary>Opens a new profile version (no ProfileId) or edits an existing one (ProfileId set); either way the
    /// caller must not create an overlapping date range for this employee (payroll.errors.profile_overlap).</summary>
    [HttpPut]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Upsert(Guid employeeId, [FromBody] PayrollProfileRequest request, CancellationToken ct)
    {
        var data = new EmployeePayrollProfileData(
            request.SgkWorkplaceCompanyId,
            request.SgkWorkplaceLocationId,
            request.ValidFrom,
            request.ValidTo,
            request.WageBasis,
            request.SgkStatus,
            request.IncentiveLawCode,
            request.MealBenefitVariant,
            request.MealDailyAmount,
            request.TransportVariant,
            request.PaymentMethod,
            request.BankAccountId,
            request.SickDaysReducePay,
            request.ExternalPayrollCode,
            request.Notes,
            request.TransportDailyAmount);

        return request.ProfileId is { } profileId
            ? FromResult(await Dispatcher.Send(new UpdateEmployeePayrollProfileCommand(profileId, data), ct))
            : FromResult(await Dispatcher.Send(new CreateEmployeePayrollProfileCommand(employeeId, data), ct));
    }
}

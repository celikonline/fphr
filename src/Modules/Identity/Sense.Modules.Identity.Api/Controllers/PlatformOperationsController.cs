using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record PlatformHealthComponentDto(string Name, string Status, double DurationMs);

public sealed record PlatformOperationsOverviewDto(
    string Status,
    double DurationMs,
    IReadOnlyList<PlatformHealthComponentDto> Components,
    BackgroundJobRuntimeStatus Jobs);

/// <summary>Platform operators' safe, read-only view of service health and scheduled jobs.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.PlatformOperations)]
[Authorize]
public sealed class PlatformOperationsController(
    ICurrentUser currentUser,
    IUserRepository users,
    IPermissionService permissions,
    ITenantContext tenantContext,
    HealthCheckService healthChecks,
    IBackgroundJobMonitor jobMonitor) : ApiControllerBase
{
    [HttpGet("overview")]
    [ProducesResponseType<PlatformOperationsOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        if (!await HasPlatformPermissionAsync(PlatformPermissions.OperationsView, ct).ConfigureAwait(false))
        {
            return Forbid();
        }

        var report = await healthChecks.CheckHealthAsync(ct).ConfigureAwait(false);
        var components = report.Entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new PlatformHealthComponentDto(
                entry.Key,
                entry.Value.Status.ToString().ToLowerInvariant(),
                entry.Value.Duration.TotalMilliseconds))
            .ToArray();

        return Ok(new PlatformOperationsOverviewDto(
            report.Status.ToString().ToLowerInvariant(),
            report.TotalDuration.TotalMilliseconds,
            components,
            jobMonitor.GetRuntimeStatus()));
    }

    [HttpGet("jobs")]
    [ProducesResponseType<IReadOnlyList<RecurringJobStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Jobs([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (skip < 0 || take is < 1 or > 100)
        {
            return BadRequest();
        }

        if (!await HasPlatformPermissionAsync(PlatformPermissions.JobsView, ct).ConfigureAwait(false))
        {
            return Forbid();
        }

        return Ok(jobMonitor.GetRecurringJobs(skip, take));
    }

    private async Task<bool> HasPlatformPermissionAsync(string permission, CancellationToken ct)
    {
        // Platform operations must never be reached through an impersonated tenant session.
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId
            || !PlatformTenant.IsPlatform(tenantContext.TenantSlug)
            || currentUser.ImpersonatorUserId is not null || currentUser.ApiKeyId is not null)
        {
            return false;
        }

        var actor = await users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        return actor?.IsPlatformStaff == true
            && await permissions.HasAsync(userId, permission, ct).ConfigureAwait(false);
    }
}

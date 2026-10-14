using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Api.Terminals;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>
/// Vendor event bridge endpoint. A local connector/gateway reads or receives native device traffic and forwards the vendor payload here
/// over HTTPS using the registered terminal key. Raw biometric templates and images are never accepted.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/terminals/integrations")]
[Authorize(AuthenticationSchemes = TerminalKeyDefaults.AuthenticationScheme)]
[TerminalTenantScope]
public sealed class TerminalIntegrationController(
    IEnumerable<ITerminalEventAdapter> adapters,
    TerminalPunchIngest ingest,
    ITenantLocalClock tenantClock,
    TerminalRequestThrottle throttle) : ApiControllerBase
{
    private readonly IReadOnlyDictionary<string, ITerminalEventAdapter> _adapters =
        adapters.ToDictionary(adapter => adapter.Provider, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Accepts forwarded ZKTeco ATTLOG text, Hikvision ISAPI XML, or Suprema BioStar JSON. User IDs are namespaced by provider and
    /// terminal code before matching against an employee's ExternalId terminal credential.
    /// </summary>
    [HttpPost("{provider}")]
    [RequestSizeLimit(TerminalKeyDefaults.MaxRequestBodyBytes)]
    [ProducesResponseType<TerminalPunchBatchResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        if (TerminalPrincipal.Read(User) is not { } terminal)
        {
            return Unauthorized();
        }

        if (!_adapters.TryGetValue(provider, out var adapter))
        {
            return NotFound();
        }

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return BadRequest();
        }

        IReadOnlyList<DeviceIntegrationEvent> events;
        try
        {
            var zone = await tenantClock.GetTimeZoneAsync(terminal.TenantId, cancellationToken).ConfigureAwait(false);
            events = adapter.Parse(payload, zone);
        }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException or System.Text.Json.JsonException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid device event payload.");
        }

        if (events.Count == 0 || events.Count > TerminalPunchIngest.MaxItemsPerRequest
            || events.Any(e => string.IsNullOrWhiteSpace(e.ExternalUserId)
                || $"{adapter.Provider}:{terminal.Code}:{e.ExternalUserId}".Length > EmployeeTerminalCredential.ValueMaxLength
                || (e.EventId is not null && $"{adapter.Provider}:{terminal.Code}:{e.EventId}".Length > 200)))
        {
            return BadRequest();
        }

        if (!throttle.TryAcquire(terminal.TerminalId, TerminalRequestThrottle.CostOf(events.Count), out var retryAfter))
        {
            if (retryAfter is { } wait)
            {
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Terminal request limit exceeded.");
        }

        var items = events.Select(e => new TerminalPunchItem(
            new TerminalCredentialInput(TerminalCredentialKind.ExternalId, $"{adapter.Provider}:{terminal.Code}:{e.ExternalUserId}"),
            e.OccurredAtUtc,
            e.Direction,
            e.EventId is null ? null : $"{adapter.Provider}:{terminal.Code}:{e.EventId}")).ToArray();
        return FromResult(await ingest.IngestAsync(terminal, items, PunchSourceDto.DeviceIntegration, cancellationToken).ConfigureAwait(false));
    }
}

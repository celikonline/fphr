using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.AuditLog;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

/// <summary>S-1 sözleşmesi: KVKK denetim izi sorgulama (TB-2). Yetki kontrolü <see cref="GetAuditLogQuery"/> üzerindeki <c>[RequiresPermission]</c> ile yapılır.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.AuditLog)]
[Authorize]
public sealed class AuditLogController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? actor,
        [FromQuery] string? action,
        [FromQuery] int page = PagingDefaults.FirstPage,
        [FromQuery] int pageSize = GetAuditLogHandler.DefaultPageSize,
        CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new GetAuditLogQuery(from, to, actor, action, page, pageSize), ct));
}

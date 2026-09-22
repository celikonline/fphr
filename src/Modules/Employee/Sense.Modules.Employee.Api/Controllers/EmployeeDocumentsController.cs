using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.Employees;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>Documents the employer issues about an employee (today: the çalışma belgesi of a terminated employee).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employees")]
[Authorize]
public sealed class EmployeeDocumentsController : ApiControllerBase
{
    /// <summary>Çalışma belgesi (4857 md.28) as PDF; 404 while the employee is not terminated.</summary>
    [HttpGet(ApiRoutes.IdParam + "/work-certificate")]
    public async Task<IActionResult> WorkCertificate(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetWorkCertificateQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }
}

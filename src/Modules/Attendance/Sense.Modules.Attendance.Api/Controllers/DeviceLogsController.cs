using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.DeviceLogs;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>PDKS cihaz/turnike CSV içe alma (S-11, ACİL KARAR 16:52): elle dosya yükleme, ilk adım.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/device-logs")]
[Authorize]
public sealed class DeviceLogsController : ApiControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    // Multipart envelope on top of the file limit (DataTransferController'daki kalıp).
    private const long MaxRequestBytes = MaxFileSizeBytes + (1024 * 1024);

    /// <summary>Uploads a "employeeCode;timestamp;direction;deviceId" CSV (≤ 5 MB, UTF-8, ilk satır başlık).</summary>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<DeviceLogImportResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(Error.Validation(DeviceLogErrorCodes.FileRequired));
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return Problem(Error.Rule(DeviceLogErrorCodes.FileTooLarge, ("max", "5 MB")));
        }

        using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var content = await reader.ReadToEndAsync(ct);
        return FromResult(await Dispatcher.Send(new ImportDeviceLogsCommand(content), ct));
    }
}

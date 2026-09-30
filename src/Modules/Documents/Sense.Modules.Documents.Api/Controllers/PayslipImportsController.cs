using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.PayslipImports;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

/// <summary>External payslip PDF bulk upload (E3): PDFs and/or zips named <c>&lt;sicil&gt;_&lt;YYYY-MM&gt;.pdf</c>.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/documents/payslip-imports")]
[Authorize]
public sealed class PayslipImportsController : ApiControllerBase
{
    private const long MaxRequestBytes = 50L * 1024 * 1024;
    public const string FilesRequiredCode = "payslip_import.files_required";
    public const string TooManyFilesCode = "payslip_import.too_many_files";

    /// <summary>Uploads payslip PDFs/zips; returns a per-file result report (unmatched files are reported, not errors).</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<PayslipImportResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(List<IFormFile>? files, CancellationToken ct)
    {
        if (files is null || files.Count == 0 || files.All(f => f.Length == 0))
        {
            return Problem(Error.Validation(FilesRequiredCode));
        }

        if (files.Count > ImportPayslipFilesValidator.MaxUploadFiles)
        {
            return Problem(Error.Validation(TooManyFilesCode, ("max", ImportPayslipFilesValidator.MaxUploadFiles)));
        }

        var uploads = new List<PayslipUploadFile>(files.Count);
        foreach (var file in files.Where(f => f.Length > 0))
        {
            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, ct);
            uploads.Add(new PayslipUploadFile(file.FileName, buffer.ToArray()));
        }

        return FromResult(await Dispatcher.Send(new ImportPayslipFilesCommand(uploads), ct));
    }
}

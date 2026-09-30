using System.IO.Compression;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.PayslipImports;

/// <summary>One uploaded file: a PDF or a zip of PDFs.</summary>
public sealed record PayslipUploadFile(string FileName, byte[] Content);

public enum PayslipImportFileStatus
{
    Imported,
    Skipped,
    Unmatched,
    Terminated,
    Duplicate,
    Failed,
}

/// <summary>One line of the result report (every uploaded PDF / zip entry gets one).</summary>
public sealed record PayslipImportFileResult(
    string FileName,
    PayslipImportFileStatus Status,
    string? Reason,
    Guid? EmployeeId,
    string? Period,
    Guid? DocumentId);

public sealed record PayslipImportResult(int Total, int Imported, IReadOnlyList<PayslipImportFileResult> Files);

/// <summary>
/// E3: bulk-uploads externally produced payslip PDFs (single PDFs and/or zips), matches each to an employee by the
/// registration number in <c>&lt;sicil&gt;_&lt;YYYY-MM&gt;.pdf</c> (K8), delivers it into the employee's documents
/// (<c>SourceModule = Payroll</c>) and raises <see cref="PayslipDocumentUploaded"/> for the notification.
/// Unmatched/duplicate/terminated/skipped files never fail the request; they are listed in the report.
/// </summary>
[RequiresPermission("payroll.payslip.import")]
public sealed record ImportPayslipFilesCommand(IReadOnlyList<PayslipUploadFile> Files) : ICommand<PayslipImportResult>;

public sealed class ImportPayslipFilesValidator : AbstractValidator<ImportPayslipFilesCommand>
{
    public const int MaxUploadFiles = 50;

    public ImportPayslipFilesValidator()
    {
        RuleFor(x => x.Files).NotEmpty().Must(f => f.Count <= MaxUploadFiles);
        RuleForEach(x => x.Files).ChildRules(f =>
        {
            f.RuleFor(x => x.FileName).NotEmpty();
            f.RuleFor(x => x.Content).NotEmpty();
        });
    }
}

/// <summary>Flattens uploads into candidate PDFs, reporting non-PDF entries with a reason instead of failing.</summary>
public static class PayslipUploadExpander
{
    public const int MaxZipEntries = 1000;
    public const int MaxPdfBytes = 5 * 1024 * 1024;
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    public static (IReadOnlyList<PayslipCandidate> Candidates, IReadOnlyList<PayslipImportFileResult> Skipped) Expand(
        IReadOnlyList<PayslipUploadFile> uploads)
    {
        var candidates = new List<PayslipCandidate>();
        var skipped = new List<PayslipImportFileResult>();

        foreach (var upload in uploads)
        {
            if (upload.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExpandZip(upload, candidates, skipped);
            }
            else
            {
                Classify(upload.FileName, upload.Content, candidates, skipped);
            }
        }

        return (candidates, skipped);
    }

    private static void ExpandZip(PayslipUploadFile zipFile, List<PayslipCandidate> candidates, List<PayslipImportFileResult> skipped)
    {
        try
        {
            using var stream = new MemoryStream(zipFile.Content);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var seen = 0;
            foreach (var entry in zip.Entries)
            {
                var name = Path.GetFileName(entry.FullName.Replace('\\', '/'));
                if (name.Length == 0 || entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal) || name.StartsWith('.'))
                {
                    continue; // directory or archive metadata, not a file the user uploaded
                }

                if (++seen > MaxZipEntries)
                {
                    skipped.Add(Skip($"{zipFile.FileName}", PayslipImportReasons.TooManyEntries));
                    return;
                }

                if (entry.Length > MaxPdfBytes)
                {
                    skipped.Add(Skip(name, PayslipImportReasons.EntryTooLarge));
                    continue;
                }

                var content = ReadBounded(entry);
                if (content is null)
                {
                    skipped.Add(Skip(name, PayslipImportReasons.EntryTooLarge));
                    continue;
                }

                Classify(name, content, candidates, skipped);
            }
        }
        catch (InvalidDataException)
        {
            skipped.Add(Skip(zipFile.FileName, PayslipImportReasons.ZipCorrupt));
        }
    }

    // Declared entry length can lie in a crafted archive; never inflate past the cap.
    private static byte[]? ReadBounded(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxPdfBytes)
            {
                return null;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static void Classify(string fileName, byte[] content, List<PayslipCandidate> candidates, List<PayslipImportFileResult> skipped)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !content.AsSpan().StartsWith(PdfMagic))
        {
            skipped.Add(Skip(name, PayslipImportReasons.NotPdf));
            return;
        }

        if (content.Length > MaxPdfBytes)
        {
            skipped.Add(Skip(name, PayslipImportReasons.EntryTooLarge));
            return;
        }

        var parsed = PayslipFileMatcher.ParseFileName(name);
        if (parsed is null)
        {
            skipped.Add(Skip(name, PayslipImportReasons.InvalidFileName));
            return;
        }

        candidates.Add(new PayslipCandidate(name, parsed.Value.RegistrationNumber, parsed.Value.Year, parsed.Value.Month, content));
    }

    private static PayslipImportFileResult Skip(string name, string reason) =>
        new(name, PayslipImportFileStatus.Skipped, reason, null, null, null);
}

public sealed class ImportPayslipFilesHandler(
    PayslipFileMatcher matcher,
    ISystemDocumentDeliveryService delivery,
    IIntegrationEventOutbox outbox,
    ITenantContext tenant,
    ILogger<ImportPayslipFilesHandler> logger) : ICommandHandler<ImportPayslipFilesCommand, PayslipImportResult>
{
    private const string SourceModule = "Payroll";

    public async Task<Result<PayslipImportResult>> Handle(ImportPayslipFilesCommand command, CancellationToken cancellationToken)
    {
        var (candidates, skipped) = PayslipUploadExpander.Expand(command.Files);
        var outcomes = await matcher.MatchAsync(tenant.TenantId, candidates, cancellationToken);

        var report = new List<PayslipImportFileResult>(skipped);
        foreach (var outcome in outcomes)
        {
            report.Add(await ProcessAsync(outcome, cancellationToken));
        }

        return new PayslipImportResult(report.Count, report.Count(r => r.Status == PayslipImportFileStatus.Imported), report);
    }

    private async Task<PayslipImportFileResult> ProcessAsync(PayslipMatchOutcome outcome, CancellationToken cancellationToken)
    {
        var c = outcome.Candidate;
        var period = $"{c.Year:D4}-{c.Month:D2}";

        switch (outcome.Status)
        {
            case PayslipMatchStatus.Duplicate:
                return new(c.FileName, PayslipImportFileStatus.Duplicate, PayslipImportReasons.DuplicateRegistration, null, period, null);
            case PayslipMatchStatus.Terminated:
                return new(c.FileName, PayslipImportFileStatus.Terminated, PayslipImportReasons.TerminatedEmployee, outcome.EmployeeId, period, null);
            case PayslipMatchStatus.Unmatched:
                return new(c.FileName, PayslipImportFileStatus.Unmatched, PayslipImportReasons.UnknownRegistration, null, period, null);
        }

        var employeeId = outcome.EmployeeId!.Value;

        // Re-uploading the same (employee, period) replaces the earlier copy: the delivery service is idempotent per source ref.
        var delivered = await delivery.DeliverGeneratedDocumentAsync(
            tenant.TenantId,
            employeeId,
            title: $"Ücret pusulası {period}",
            fileName: c.FileName,
            contentType: "application/pdf",
            content: c.Content,
            sourceModule: SourceModule,
            sourceRef: $"payslip-import:{employeeId:N}:{period}",
            cancellationToken);

        if (delivered.IsFailure)
        {
            logger.LogWarning("Payslip import delivery failed for {FileName}: {ErrorCode}", c.FileName, delivered.Error.Code);
            return new(c.FileName, PayslipImportFileStatus.Failed, PayslipImportReasons.DeliveryFailed, employeeId, period, null);
        }

        outbox.Enqueue(new PayslipDocumentUploaded(tenant.TenantId, employeeId, delivered.Value, c.Year, c.Month));
        return new(c.FileName, PayslipImportFileStatus.Imported, null, employeeId, period, delivered.Value);
    }
}

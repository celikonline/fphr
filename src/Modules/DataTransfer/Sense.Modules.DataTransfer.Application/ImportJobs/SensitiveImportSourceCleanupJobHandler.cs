using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.DataTransfer.Application.ImportJobs;

/// <summary>
/// ADR-0004 handler for <see cref="JobType.ImportSensitiveSourceCleanup"/> (Hangfire, Sense.Api; per-tenant hourly seed): deletes the
/// uploaded files of templates with sensitive columns (<see cref="ImportColumn.IsSensitive"/>, e.g. terminal card/PIN) once their job has
/// finished or <see cref="ImportRetentionOptions.SensitiveSourceFileTtl"/> after upload - even if the job was never run. A run already deletes
/// the file itself; this sweep covers abandoned jobs (uploaded/mapped, never run) and runs that failed before reaching that point. Other
/// templates keep their files (result workbook echoes the source cells). A null-tenant invocation is a no-op. Logs counts only.
/// </summary>
public sealed partial class SensitiveImportSourceCleanupJobHandler(
    IEnumerable<IImportTemplate> templates,
    IImportJobRepository jobs,
    IOptions<ImportRetentionOptions> options,
    TimeProvider clock,
    ILogger<SensitiveImportSourceCleanupJobHandler> logger) : IScheduledJobHandler
{
    public JobType Handles => JobType.ImportSensitiveSourceCleanup;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        if (context.TenantId is not { } tenantId)
        {
            return;
        }

        var sensitiveCodes = await SensitiveTemplateCodesAsync(ct).ConfigureAwait(false);
        var uploadedBefore = clock.GetUtcNow().UtcDateTime - options.Value.SensitiveSourceFileTtl;
        var deleted = await jobs.DeleteSourceFilesAsync(sensitiveCodes, uploadedBefore, ct).ConfigureAwait(false);
        if (deleted > 0)
        {
            LogDeleted(logger, tenantId, deleted);
        }
    }

    private async Task<IReadOnlyCollection<string>> SensitiveTemplateCodesAsync(CancellationToken ct)
    {
        var codes = new List<string>();
        foreach (var template in templates)
        {
            try
            {
                if (ImportTemplateCatalog.HasSensitiveColumns(await template.GetColumnsAsync(ct).ConfigureAwait(false)))
                {
                    codes.Add(template.Code);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A template whose (tenant-dynamic) columns cannot be read here is skipped; sensitive templates have static columns.
                LogTemplateSkipped(logger, template.Code, ex);
            }
        }

        return codes;
    }

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information,
        Message = "Deleted {Count} uploaded import file(s) of sensitive templates for tenant {TenantId}")]
    private static partial void LogDeleted(ILogger logger, Guid tenantId, int count);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning,
        Message = "Import template {TemplateCode} skipped by the sensitive source file cleanup: its columns could not be read")]
    private static partial void LogTemplateSkipped(ILogger logger, string templateCode, Exception exception);
}

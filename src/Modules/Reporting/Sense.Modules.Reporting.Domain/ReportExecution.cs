using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Domain;

public enum ReportExecutionStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>
/// One request to generate a report (roadmap #70's "Kendin oluştur" + prebuilt reports). Runs synchronously today
/// (R0) - the same Queued -&gt; Running -&gt; Succeeded/Failed state machine a future Hangfire-backed async path
/// (<c>JobType.ExportReport</c>, currently a stub in <c>BackgroundJobExecutor</c>) would drive; nothing here assumes
/// synchronous execution.
/// </summary>
public sealed class ReportExecution : TenantAggregateRoot<Guid>
{
    private ReportExecution()
    {
    }

    private ReportExecution(Guid id, Guid tenantId, string reportKey, string? parametersJson, Guid requestedByUserId, DateTime requestedAt)
        : base(id, tenantId)
    {
        ReportKey = reportKey;
        ParametersJson = parametersJson;
        RequestedByUserId = requestedByUserId;
        RequestedAt = requestedAt;
        Status = ReportExecutionStatus.Queued;
    }

    /// <summary>Matches an <c>IReportProvider.Key</c> (Reporting.Contracts), e.g. "documents.missingDocuments".</summary>
    public string ReportKey { get; private set; } = string.Empty;

    /// <summary>Provider-specific parameters, serialized; opaque to this aggregate.</summary>
    public string? ParametersJson { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTime RequestedAt { get; private set; }

    public ReportExecutionStatus Status { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public string? ResultFileName { get; private set; }

    public string? ResultContentType { get; private set; }

    /// <summary>Generated file bytes, stored inline (R0: small CSV/Excel reports only - no object storage yet).</summary>
    public byte[]? ResultContent { get; private set; }

    public string? ErrorMessage { get; private set; }

    public static Result<ReportExecution> Create(Guid tenantId, string reportKey, string? parametersJson, Guid requestedByUserId, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(reportKey))
        {
            return Error.Validation(ReportingDomainErrors.ReportKeyRequired);
        }

        return new ReportExecution(Guid.CreateVersion7(), tenantId, reportKey.Trim(), parametersJson, requestedByUserId, now);
    }

    public void Start(DateTime now)
    {
        Status = ReportExecutionStatus.Running;
        StartedAt = now;
    }

    public void Complete(string fileName, string contentType, byte[] content, DateTime now)
    {
        Status = ReportExecutionStatus.Succeeded;
        ResultFileName = fileName;
        ResultContentType = contentType;
        ResultContent = content;
        CompletedAt = now;
    }

    public void Fail(string errorMessage, DateTime now)
    {
        Status = ReportExecutionStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = now;
    }
}

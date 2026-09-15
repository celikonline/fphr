namespace Sense.Shared.Contracts.Notifications;

/// <summary>
/// Notification type.
/// </summary>
public enum NotificationType
{
    /// <summary>Informational notification.</summary>
    Info = 1,

    /// <summary>Warning notification.</summary>
    Warning = 2,

    /// <summary>Error notification.</summary>
    Error = 3,

    /// <summary>Success notification.</summary>
    Success = 4,
}

/// <summary>
/// Notification urgency level.
/// </summary>
public enum NotificationUrgency
{
    /// <summary>Low urgency.</summary>
    Low = 1,

    /// <summary>Medium urgency.</summary>
    Medium = 2,

    /// <summary>High urgency.</summary>
    High = 3,
}

/// <summary>
/// Data transfer object for notifications sent via SignalR.
/// </summary>
public record NotificationDto(
    string Title,
    string Message,
    NotificationType Type,
    NotificationUrgency Urgency,
    DateTime CreatedAt = default,
    string? ActionUrl = null,
    Dictionary<string, object>? Metadata = null)
{
    /// <summary>
    /// Creates a new NotificationDto with current timestamp.
    /// </summary>
    public static NotificationDto Create(
        string title,
        string message,
        NotificationType type,
        NotificationUrgency urgency,
        string? actionUrl = null,
        Dictionary<string, object>? metadata = null) =>
        new(title, message, type, urgency, DateTime.UtcNow, actionUrl, metadata);
}

/// <summary>
/// Data transfer object for job progress updates sent via SignalR.
/// </summary>
public record JobProgressDto(
    string JobName,
    string JobId,
    int PercentComplete,
    string Status,
    DateTime UpdatedAt = default,
    string? Message = null,
    Dictionary<string, object>? Metadata = null)
{
    /// <summary>
    /// Creates a new JobProgressDto with current timestamp.
    /// </summary>
    public static JobProgressDto Create(
        string jobName,
        string jobId,
        int percentComplete,
        string status,
        string? message = null,
        Dictionary<string, object>? metadata = null) =>
        new(jobName, jobId, percentComplete, status, DateTime.UtcNow, message, metadata);
}

/// <summary>
/// Data transfer object for audit alerts sent via SignalR.
/// </summary>
public record AuditAlertDto(
    string Action,
    string Resource,
    string Actor,
    DateTime Timestamp = default,
    string? Details = null,
    Dictionary<string, object>? Metadata = null)
{
    /// <summary>
    /// Creates a new AuditAlertDto with current timestamp.
    /// </summary>
    public static AuditAlertDto Create(
        string action,
        string resource,
        string actor,
        string? details = null,
        Dictionary<string, object>? metadata = null) =>
        new(action, resource, actor, DateTime.UtcNow, details, metadata);
}

namespace Sense.Shared.Contracts.Context;

/// <summary>
/// What triggered the current unit of work, for audit log Source stamping (E-01/KVKK m.12: "kim yaptı" is not enough
/// on its own - the audit trail should also say how the action reached the system). Ambient via AsyncLocal, the same
/// way <see cref="ITenantContext"/>/<see cref="ICurrentUser"/> are, so it flows through an async handler pipeline
/// without threading a parameter everywhere. <see cref="Current"/> is an explicit override; when nothing has set one
/// (most request/job paths never call <see cref="BeginScope"/>), the audit writer falls back to inferring the source
/// from the current actor (see IdentityAuditLogInterceptor) - Web/Api requests and System jobs don't need every call
/// site to opt in, only the ones a plain actor check can't disambiguate (e.g. a bulk import run by a real user).
/// </summary>
public interface IAuditSourceAccessor
{
    string? Current { get; }

    IDisposable BeginScope(string source);
}

/// <summary>
/// Fixed vocabulary for <see cref="IAuditSourceAccessor"/>. A free string column, not an enum/check-constraint
/// (plan decision, senseik-018 §3), so a new source is a one-line constant here rather than a migration.
/// </summary>
public static class AuditSources
{
    public const string Web = "Web";
    public const string Api = "Api";
    public const string Import = "Import";
    public const string System = "System";
}

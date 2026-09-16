namespace Sense.Shared.Contracts.BackgroundJobs;

/// <summary>
/// ADR-0004: <c>BackgroundJobExecutor</c>'ın (Sense.Shared.Infrastructure) merkezi switch yerine DI ile
/// keşfettiği job işleyicisi. Her modül, işlediği <see cref="JobType"/> için tek bir
/// <see cref="IScheduledJobHandler"/> kaydeder (<c>services.AddScoped&lt;IScheduledJobHandler, XyzJobHandler&gt;()</c>,
/// birden fazla implementasyon <c>TryAddEnumerable</c> yerine <c>AddScoped</c> ile eklenmelidir ki executor hepsini görsün).
/// Aynı <see cref="Handles"/> değeri iki kez kayıtlıysa executor başlangıçta (ilk job çalıştığında)
/// <see cref="InvalidOperationException"/> fırlatır (fail-fast); hiç kayıt yoksa executor uyarı loglar ve no-op döner.
/// </summary>
public interface IScheduledJobHandler
{
    /// <summary>Bu işleyicinin sorumlu olduğu job türü.</summary>
    JobType Handles { get; }

    /// <summary>Job'u çalıştırır. Tenant/correlation bağlamı executor tarafından zaten kurulmuştur.</summary>
    Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct);
}

/// <summary>
/// <see cref="IScheduledJobHandler.ExecuteAsync"/>'e geçirilen çalıştırma bağlamı.
/// </summary>
/// <param name="TenantId">Job tetiklenirken bilinen kiracı; sistem geneli job'larda null olabilir.</param>
/// <param name="PayloadJson">Job'a özgü, serileştirilmiş (JSON) veri; yoksa null.</param>
/// <param name="TriggeredAt">Job'un çalıştırılmaya başladığı an (UTC).</param>
public sealed record ScheduledJobContext(Guid? TenantId, string? PayloadJson, DateTimeOffset TriggeredAt);

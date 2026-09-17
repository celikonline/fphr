namespace Sense.Shared.Contracts.BackgroundJobs;

/// <summary>
/// Yazılabilir (Trial/Active) durumdaki tüm kiracıların kimlik listesi. Kiracı bazlı başlangıç görevleri için
/// (ör. <c>BackgroundJobSeedSynchronizer</c>, G-1-13) Identity modülü uygular; Shared bu portu yalnız tüketir.
/// </summary>
public interface IActiveTenantDirectory
{
    Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken ct);
}

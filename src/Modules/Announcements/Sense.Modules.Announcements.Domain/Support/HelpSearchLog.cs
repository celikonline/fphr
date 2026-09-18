using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Announcements.Domain.Support;

/// <summary>
/// 11.3.13: HelpSearchLog{Term, ResultCount} [Aday] — sıfır sonuçlu aramalar, hangi Yardım Merkezi makalesinin
/// yazılması gerektiğini gösteren tek objektif sinyal (content-as-code geri besleme döngüsü).
/// </summary>
public sealed class HelpSearchLog : TenantEntity<Guid>
{
    // ReSharper disable once UnusedMember.Local
    private HelpSearchLog() { }

    public string Term { get; private set; } = null!;
    public int ResultCount { get; private set; }
    public string? Route { get; private set; }
    public DateTime SearchedAt { get; private set; }

    public static HelpSearchLog Create(Guid tenantId, string term, int resultCount, string? route) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Term = term.Trim().ToLowerInvariant(),
            ResultCount = resultCount,
            Route = route,
            SearchedAt = DateTime.UtcNow,
        };
}

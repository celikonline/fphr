using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Announcements.Domain.Support;

/// <summary>
/// Help Center category - **platform-scoped** (no TenantId, not ITenantEntity, so ModuleDbContext applies no Tenant
/// query filter and AuditTenantInterceptor never stamps a tenant; same shape as Leave's LegislationParameter). One row
/// per locale (tr/en), like HelpArticle; managed by the Sense team (platform editor is Faz 8), read-only for tenants.
/// </summary>
public sealed class HelpCategory : Entity<Guid>
{
    private HelpCategory()
    {
    }

    public HelpCategory(Guid id, string slug, string locale, string name, int sortOrder)
        : base(id)
    {
        Slug = slug;
        Locale = locale;
        Name = name;
        SortOrder = sortOrder;
    }

    /// <summary>Stable identifier within a locale (e.g. "data-transfer").</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Content locale: "tr" or "en" (<see cref="HelpLocales"/>).</summary>
    public string Locale { get; private set; } = HelpLocales.Default;

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }
}

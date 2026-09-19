using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

/// <summary>
/// Announcements module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "announcements"). Hosts
/// the tenant-scoped Announcement/SupportTicket aggregates and the platform-scoped Help Center tables
/// (help_categories/help_articles: plain Entity&lt;Guid&gt;, not ITenantEntity, so ModuleDbContext adds no Tenant query
/// filter and no tenant_id column - same as Leave's legislation_parameters).
/// </summary>
public sealed class AnnouncementsDbContext(DbContextOptions<AnnouncementsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IAnnouncementsUnitOfWork
{
    public const string SchemaName = "announcements";

    public override string Schema => SchemaName;

    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<HelpCategory> HelpCategories => Set<HelpCategory>();

    public DbSet<HelpArticle> HelpArticles => Set<HelpArticle>();

    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
}

internal static class AnnouncementsTables
{
    public const string Announcements = "announcements";
    public const string HelpCategories = "help_categories";
    public const string HelpArticles = "help_articles";
    public const string SupportTickets = "support_tickets";

    public const int EnumLength = 30;
    public const int LocaleLength = 5;
    public const string TextColumnType = "text";
    public const string TextArrayColumnType = "text[]";
    public const string ArrayDefault = "'{}'::text[]";
}

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.ToTable(AnnouncementsTables.Announcements);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(AnnouncementsLimits.TitleMaxLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(AnnouncementsLimits.BodyMaxLength).HasColumnType(AnnouncementsTables.TextColumnType).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Location).HasMaxLength(AnnouncementsLimits.LocationMaxLength);
        b.Property(x => x.MeetingUrl).HasMaxLength(AnnouncementsLimits.UrlMaxLength);

        b.HasIndex(x => new { x.TenantId, x.Status, x.IsPinned, x.PublishedAt });
        b.HasIndex(x => new { x.TenantId, x.Category, x.EventStartsAt });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class HelpCategoryConfiguration : IEntityTypeConfiguration<HelpCategory>
{
    public void Configure(EntityTypeBuilder<HelpCategory> b)
    {
        b.ToTable(AnnouncementsTables.HelpCategories);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Slug).HasMaxLength(AnnouncementsLimits.SlugMaxLength).IsRequired();
        b.Property(x => x.Locale).HasMaxLength(AnnouncementsTables.LocaleLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(AnnouncementsLimits.TitleMaxLength).IsRequired();

        b.HasIndex(x => new { x.Locale, x.Slug }).IsUnique();

        b.HasData(HelpCenterSeed.Categories.Select(c => new
        {
            c.Id,
            c.Slug,
            c.Locale,
            c.Name,
            c.SortOrder,
            CreatedAt = HelpCenterSeed.PublishedAt,
        }));
    }
}

public sealed class HelpArticleConfiguration : IEntityTypeConfiguration<HelpArticle>
{
    public void Configure(EntityTypeBuilder<HelpArticle> b)
    {
        b.ToTable(AnnouncementsTables.HelpArticles);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Slug).HasMaxLength(AnnouncementsLimits.SlugMaxLength).IsRequired();
        b.Property(x => x.Title).HasMaxLength(AnnouncementsLimits.TitleMaxLength).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(AnnouncementsLimits.SummaryMaxLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(AnnouncementsLimits.HelpBodyMaxLength).HasColumnType(AnnouncementsTables.TextColumnType).IsRequired();
        b.Property(x => x.Locale).HasMaxLength(AnnouncementsTables.LocaleLength).IsRequired();
        b.Property(x => x.VideoUrl).HasMaxLength(AnnouncementsLimits.UrlMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();

        // Native PostgreSQL text[] (not the jsonb+converter used elsewhere) so tag search translates to SQL
        // (EXISTS over unnest(tags) with ILIKE) in HelpArticleRepository.SearchPublishedAsync.
        b.Property(x => x.Tags).HasColumnType(AnnouncementsTables.TextArrayColumnType).HasDefaultValueSql(AnnouncementsTables.ArrayDefault).IsRequired();
        b.Property(x => x.RelatedRoutes).HasColumnType(AnnouncementsTables.TextArrayColumnType).HasDefaultValueSql(AnnouncementsTables.ArrayDefault).IsRequired();

        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.Locale, x.Status, x.CategoryId });
        b.HasOne<HelpCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

        b.HasData(HelpCenterSeed.Articles.Select(a => new
        {
            a.Id,
            a.Slug,
            a.Locale,
            a.CategoryId,
            a.Title,
            a.Summary,
            a.Body,
            Tags = a.Tags.ToList(),
            RelatedRoutes = a.RelatedRoutes.ToList(),
            VideoUrl = (string?)null,
            Status = HelpArticleStatus.Published,
            PublishedAt = (DateTime?)HelpCenterSeed.PublishedAt,
            CreatedAt = HelpCenterSeed.PublishedAt,
        }));
    }
}

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable(AnnouncementsTables.SupportTickets);
        b.HasKey(x => x.Id);

        b.Property(x => x.Subject).HasMaxLength(AnnouncementsLimits.TicketSubjectMaxLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(AnnouncementsLimits.TicketBodyMaxLength).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(AnnouncementsTables.EnumLength).IsRequired();
        b.Property(x => x.PageContext).HasMaxLength(AnnouncementsLimits.PageContextMaxLength);

        b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });

        b.Ignore(x => x.DomainEvents);
    }
}

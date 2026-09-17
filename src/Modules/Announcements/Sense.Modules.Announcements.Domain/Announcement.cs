using Sense.Shared.Kernel;

namespace Sense.Modules.Announcements.Domain;

/// <summary>
/// Kurumsal duyuru agregat kökü.
/// Duyuru başlığı, içeriği, hedef kitle ve yayın yönetimi.
/// </summary>
public sealed class Announcement : TenantAggregateRoot, IAuditLogged
{
    // ReSharper disable UnusedMember.Local
    private Announcement() { }

    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public AnnouncementType Type { get; private set; }
    public AnnouncementPriority Priority { get; private set; }
    public AnnouncementStatus Status { get; private set; }
    public Guid PublishedBy { get; private set; }
    public DateTime PublishedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public string? TargetAudience { get; private set; }

    /// <summary>
    /// Duyuru oluştur (taslak).
    /// </summary>
    public static Result<Announcement> Create(
        Guid tenantId,
        string title,
        string content,
        AnnouncementType type,
        AnnouncementPriority priority,
        string? targetAudience = null)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return AnnouncementDomainErrors.TitleRequired;

        if (string.IsNullOrWhiteSpace(content))
            return AnnouncementDomainErrors.ContentRequired;

        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Content = content.Trim(),
            Type = type,
            Priority = priority,
            Status = AnnouncementStatus.Draft,
            TargetAudience = targetAudience,
            PublishedBy = Guid.Empty,
            PublishedAt = DateTime.UtcNow
        };

        return announcement;
    }

    /// <summary>
    /// Duyuruyu yayınla.
    /// </summary>
    public Result Publish(Guid publishedBy, DateTime? expiresAt = null)
    {
        if (Status != AnnouncementStatus.Draft)
            return AnnouncementDomainErrors.CannotPublish;

        Status = AnnouncementStatus.Published;
        PublishedBy = publishedBy;
        PublishedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;

        return Result.Success();
    }

    /// <summary>
    /// Duyuruyu arşivle.
    /// </summary>
    public Result Archive()
    {
        if (Status == AnnouncementStatus.Archived)
            return AnnouncementDomainErrors.AlreadyArchived;

        Status = AnnouncementStatus.Archived;
        return Result.Success();
    }
}

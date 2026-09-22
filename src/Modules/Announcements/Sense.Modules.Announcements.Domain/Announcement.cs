using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain;

/// <summary>Kurumsal duyuru agregat kökü: başlık, içerik, hedef kitle ve yayın yönetimi.</summary>
public sealed class Announcement : TenantAggregateRoot<Guid>, IAuditLogged
{
    // ReSharper disable once UnusedMember.Local
    private Announcement() { }

    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public AnnouncementType Type { get; private set; }
    public AnnouncementPriority Priority { get; private set; }
    public AnnouncementStatus Status { get; private set; }
    public Guid PublishedBy { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public string? TargetAudience { get; private set; }

    public static Result<Announcement> Create(
        Guid tenantId, string title, string content, AnnouncementType type, AnnouncementPriority priority, string? targetAudience = null)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return Error.Validation(AnnouncementDomainErrors.TitleRequired);
        }

        if (string.IsNullOrWhiteSpace(content) || content.Length > AnnouncementsLimits.ContentMaxLength)
        {
            return Error.Validation(AnnouncementDomainErrors.ContentRequired);
        }

        return new Announcement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Content = content.Trim(),
            Type = type,
            Priority = priority,
            Status = AnnouncementStatus.Draft,
            TargetAudience = targetAudience,
        };
    }

    /// <summary>Taslak duyuruyu düzenle (tam sayfa editör: başlık/zengin içerik/tür/öncelik/hedef kitle). Yayınlanmış
    /// bir duyuru artık okuyanlarca görülmüş sayılır; düzenleme yalnızca taslak durumdayken serbesttir.</summary>
    public Result Update(string title, string content, AnnouncementType type, AnnouncementPriority priority, string? targetAudience = null)
    {
        if (Status != AnnouncementStatus.Draft)
        {
            return Error.Conflict(AnnouncementDomainErrors.CannotEditPublished);
        }

        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return Error.Validation(AnnouncementDomainErrors.TitleRequired);
        }

        if (string.IsNullOrWhiteSpace(content) || content.Length > AnnouncementsLimits.ContentMaxLength)
        {
            return Error.Validation(AnnouncementDomainErrors.ContentRequired);
        }

        Title = title.Trim();
        Content = content.Trim();
        Type = type;
        Priority = priority;
        TargetAudience = targetAudience;
        return Result.Success();
    }

    /// <summary>Taslak duyuruyu yayınla.</summary>
    public Result Publish(Guid publishedBy, DateTime? expiresAt = null)
    {
        if (Status != AnnouncementStatus.Draft)
        {
            return Error.Conflict(AnnouncementDomainErrors.CannotPublish);
        }

        Status = AnnouncementStatus.Published;
        PublishedBy = publishedBy;
        PublishedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
        return Result.Success();
    }

    /// <summary>Yayınlanmış veya taslak duyuruyu arşivle.</summary>
    public Result Archive()
    {
        if (Status == AnnouncementStatus.Archived)
        {
            return Error.Conflict(AnnouncementDomainErrors.AlreadyArchived);
        }

        Status = AnnouncementStatus.Archived;
        return Result.Success();
    }
}

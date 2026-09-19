using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain.Announcements;

public enum AnnouncementCategory
{
    General = 0,
    Event = 1,
    PolicyUpdate = 2,
}

public enum AnnouncementStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

/// <summary>Editable announcement content; event fields are only valid (and StartsAt required) for Category=Event.</summary>
public sealed record AnnouncementContent(
    string Title,
    string Body,
    AnnouncementCategory Category,
    DateTime? EventStartsAt = null,
    DateTime? EventEndsAt = null,
    string? Location = null,
    string? MeetingUrl = null);

/// <summary>
/// Company announcement (roadmap 3.12 / #71): Draft -> Published -> Archived, optional pin. Category=Event carries
/// EventStartsAt/EventEndsAt/Location/MeetingUrl (11.3.13 "Etkinlik takvimi kaynağı") and feeds the home widget.
/// All event times are UTC.
/// </summary>
public sealed class Announcement : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Announcement()
    {
    }

    private Announcement(Guid id, Guid tenantId)
        : base(id, tenantId)
    {
        Status = AnnouncementStatus.Draft;
    }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public AnnouncementCategory Category { get; private set; }

    public AnnouncementStatus Status { get; private set; }

    public bool IsPinned { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public Guid? PublishedByUserId { get; private set; }

    public DateTime? EventStartsAt { get; private set; }

    public DateTime? EventEndsAt { get; private set; }

    public string? Location { get; private set; }

    public string? MeetingUrl { get; private set; }

    public static Result<Announcement> CreateDraft(Guid tenantId, AnnouncementContent content)
    {
        var announcement = new Announcement(Guid.CreateVersion7(), tenantId);
        var applied = announcement.Apply(content);
        return applied.IsFailure ? applied.Error : announcement;
    }

    /// <summary>Edits content while Draft or Published; archived announcements are read-only.</summary>
    public Result Update(AnnouncementContent content)
    {
        if (Status == AnnouncementStatus.Archived)
        {
            return Error.Rule(AnnouncementsDomainErrors.AlreadyArchived);
        }

        return Apply(content);
    }

    /// <summary>Draft -> Published.</summary>
    public Result Publish(Guid? publishedByUserId, DateTime publishedAtUtc)
    {
        if (Status != AnnouncementStatus.Draft)
        {
            return Error.Rule(AnnouncementsDomainErrors.NotDraft);
        }

        Status = AnnouncementStatus.Published;
        PublishedAt = AsUtc(publishedAtUtc);
        PublishedByUserId = publishedByUserId;
        return Result.Success();
    }

    /// <summary>Draft/Published -> Archived (also unpins).</summary>
    public Result Archive()
    {
        if (Status == AnnouncementStatus.Archived)
        {
            return Error.Rule(AnnouncementsDomainErrors.AlreadyArchived);
        }

        Status = AnnouncementStatus.Archived;
        IsPinned = false;
        return Result.Success();
    }

    /// <summary>Only a Published announcement can be pinned; unpinning is always allowed.</summary>
    public Result SetPinned(bool pinned)
    {
        if (pinned && Status != AnnouncementStatus.Published)
        {
            return Error.Rule(AnnouncementsDomainErrors.NotPublished);
        }

        IsPinned = pinned;
        return Result.Success();
    }

    private Result Apply(AnnouncementContent content)
    {
        if (string.IsNullOrWhiteSpace(content.Title))
        {
            return Error.Validation(AnnouncementsDomainErrors.TitleRequired);
        }

        if (string.IsNullOrWhiteSpace(content.Body))
        {
            return Error.Validation(AnnouncementsDomainErrors.BodyRequired);
        }

        var location = Normalize(content.Location, AnnouncementsLimits.LocationMaxLength);
        var meetingUrl = Normalize(content.MeetingUrl, AnnouncementsLimits.UrlMaxLength);

        if (content.Category == AnnouncementCategory.Event)
        {
            if (content.EventStartsAt is null)
            {
                return Error.Validation(AnnouncementsDomainErrors.EventStartRequired);
            }

            if (content.EventEndsAt is { } end && AsUtc(end) < AsUtc(content.EventStartsAt.Value))
            {
                return Error.Validation(AnnouncementsDomainErrors.EventEndBeforeStart);
            }

            if (meetingUrl is not null && !IsHttpUrl(meetingUrl))
            {
                return Error.Validation(AnnouncementsDomainErrors.MeetingUrlInvalid);
            }
        }
        else if (content.EventStartsAt is not null || content.EventEndsAt is not null || location is not null || meetingUrl is not null)
        {
            return Error.Validation(AnnouncementsDomainErrors.EventFieldsNotAllowed);
        }

        Title = Guard.MaxLength(content.Title.Trim(), AnnouncementsLimits.TitleMaxLength);
        Body = Guard.MaxLength(content.Body.Trim(), AnnouncementsLimits.BodyMaxLength);
        Category = content.Category;
        EventStartsAt = content.EventStartsAt is { } s ? AsUtc(s) : null;
        EventEndsAt = content.EventEndsAt is { } e ? AsUtc(e) : null;
        Location = location;
        MeetingUrl = meetingUrl;
        return Result.Success();
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? Normalize(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

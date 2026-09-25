using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain;

/// <summary>
/// Schedule and logistics of a company event (E4). <paramref name="Mode"/> is null on legacy all-day events (no
/// location/URL rule applies); a non-null mode enforces onsite=location, online=URL.
/// </summary>
public sealed record CompanyEventPlan(
    DateTime StartsAt,
    DateTime? EndsAt,
    bool IsAllDay,
    CompanyEventMode? Mode = null,
    string? Location = null,
    string? OnlineUrl = null,
    bool NotifyInvitees = false,
    Guid? CategoryId = null)
{
    /// <summary>All-day plan for a single legacy date.</summary>
    public static CompanyEventPlan AllDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), null, true);
}

/// <summary>
/// Manually created company calendar entry (senseik-027, roadmap #71/P-19): title, date, type (CompanyEvent or
/// Meeting only - see <see cref="CompanyEventType"/>) and visibility (tenant-wide when <see cref="VisibilityOrgUnitId"/>
/// is null, otherwise restricted to that one org unit - no subtree walk in this MVP, see handoff notes).
/// Birthdays and leave starts are never persisted here; they are derived at query time in
/// <c>GetCompanyCalendarQuery</c>.
/// </summary>
public sealed class CompanyEvent : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<CompanyEventInvitee> _invitees = [];

    // ReSharper disable once UnusedMember.Local
    private CompanyEvent() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>Start date; always derived from <see cref="StartsAt"/> (kept for legacy readers).</summary>
    public DateOnly EventDate { get; private set; }
    public CompanyEventType Type { get; private set; }
    public Guid? VisibilityOrgUnitId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    /// <summary>Null only on rows not yet backfilled; the migration sets it to EventDate 00:00.</summary>
    public DateTime? StartsAt { get; private set; }
    public DateTime? EndsAt { get; private set; }
    public bool IsAllDay { get; private set; }
    public CompanyEventMode? Mode { get; private set; }
    public string? Location { get; private set; }
    public string? OnlineUrl { get; private set; }
    public bool NotifyInvitees { get; private set; }
    public Guid? CategoryId { get; private set; }

    public IReadOnlyCollection<CompanyEventInvitee> Invitees => _invitees;

    /// <summary>Last day the event occupies (inclusive).</summary>
    public DateOnly EndDate => DateOnly.FromDateTime(EndsAt ?? StartsAt ?? EventDate.ToDateTime(TimeOnly.MinValue));

    public static Result<CompanyEvent> Create(
        Guid tenantId, string title, string? description, DateOnly eventDate, CompanyEventType type,
        Guid? visibilityOrgUnitId, Guid createdByUserId) =>
        Create(tenantId, title, description, type, visibilityOrgUnitId, createdByUserId, CompanyEventPlan.AllDay(eventDate));

    public static Result<CompanyEvent> Create(
        Guid tenantId, string title, string? description, CompanyEventType type,
        Guid? visibilityOrgUnitId, Guid createdByUserId, CompanyEventPlan plan)
    {
        var companyEvent = new CompanyEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VisibilityOrgUnitId = visibilityOrgUnitId,
            CreatedByUserId = createdByUserId,
        };

        var applied = companyEvent.Apply(title, description, type, plan);
        return applied.IsFailure ? applied.Error : companyEvent;
    }

    /// <summary>Manuel etkinliği düzenle (senseik-027 takip: roadmap #71 - önceden yalnız sil-yeniden oluşturma
    /// vardı). Sil-yeniden oluşturmanın aksine kimliği ve oluşturanı korur.</summary>
    public Result Update(
        string title, string? description, DateOnly eventDate, CompanyEventType type, Guid? visibilityOrgUnitId) =>
        Update(title, description, type, visibilityOrgUnitId, CompanyEventPlan.AllDay(eventDate));

    public Result Update(
        string title, string? description, CompanyEventType type, Guid? visibilityOrgUnitId, CompanyEventPlan plan)
    {
        var applied = Apply(title, description, type, plan);
        if (applied.IsFailure)
        {
            return applied;
        }

        VisibilityOrgUnitId = visibilityOrgUnitId;
        return Result.Success();
    }

    /// <summary>Replaces the invitee set; returns the employees added by this call (the ones to notify).</summary>
    public IReadOnlyList<Guid> SetInvitees(IEnumerable<Guid> employeeIds)
    {
        var wanted = employeeIds.Distinct().ToHashSet();
        _invitees.RemoveAll(i => !wanted.Contains(i.EmployeeId));
        var existing = _invitees.Select(i => i.EmployeeId).ToHashSet();
        var added = wanted.Where(id => !existing.Contains(id)).ToList();
        _invitees.AddRange(added.Select(id => CompanyEventInvitee.Create(TenantId, Id, id)));
        return added;
    }

    private Result Apply(string title, string? description, CompanyEventType type, CompanyEventPlan plan)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > CompanyEventLimits.TitleMaxLength)
        {
            return Error.Validation(CompanyEventDomainErrors.TitleRequired);
        }

        if (description is { Length: > 0 } && description.Length > CompanyEventLimits.DescriptionMaxLength)
        {
            return Error.Validation(CompanyEventDomainErrors.DescriptionTooLong);
        }

        if (type is CompanyEventType.Birthday or CompanyEventType.LeaveStart)
        {
            return Error.Validation(CompanyEventDomainErrors.TypeNotManual);
        }

        var starts = plan.IsAllDay ? plan.StartsAt.Date : plan.StartsAt;
        var ends = plan.IsAllDay ? plan.EndsAt?.Date : plan.EndsAt;
        if (ends is { } end && end < starts)
        {
            return Error.Validation(CompanyEventDomainErrors.EndsBeforeStarts);
        }

        var location = string.IsNullOrWhiteSpace(plan.Location) ? null : plan.Location.Trim();
        var url = string.IsNullOrWhiteSpace(plan.OnlineUrl) ? null : plan.OnlineUrl.Trim();
        var logistics = ValidateLogistics(plan.Mode, location, url);
        if (logistics.IsFailure)
        {
            return logistics;
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Type = type;
        StartsAt = DateTime.SpecifyKind(starts, DateTimeKind.Utc);
        EndsAt = ends is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null;
        IsAllDay = plan.IsAllDay;
        EventDate = DateOnly.FromDateTime(starts);
        Mode = plan.Mode;
        Location = plan.Mode == CompanyEventMode.Online ? null : location;
        OnlineUrl = plan.Mode == CompanyEventMode.Onsite ? null : url;
        NotifyInvitees = plan.NotifyInvitees;
        CategoryId = plan.CategoryId;
        return Result.Success();
    }

    private static Result ValidateLogistics(CompanyEventMode? mode, string? location, string? url)
    {
        if (location is { Length: > CompanyEventLimits.LocationMaxLength })
        {
            return Error.Validation(CompanyEventDomainErrors.LocationTooLong);
        }

        return mode switch
        {
            CompanyEventMode.Online when url is null || url.Length > CompanyEventLimits.OnlineUrlMaxLength
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                => Error.Validation(CompanyEventDomainErrors.OnlineUrlRequired),
            CompanyEventMode.Onsite when location is null => Error.Validation(CompanyEventDomainErrors.LocationRequired),
            _ => Result.Success(),
        };
    }
}

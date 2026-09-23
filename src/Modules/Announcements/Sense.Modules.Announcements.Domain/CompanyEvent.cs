using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain;

/// <summary>
/// Manually created company calendar entry (senseik-027, roadmap #71/P-19): title, date, type (CompanyEvent or
/// Meeting only - see <see cref="CompanyEventType"/>) and visibility (tenant-wide when <see cref="VisibilityOrgUnitId"/>
/// is null, otherwise restricted to that one org unit - no subtree walk in this MVP, see handoff notes).
/// Birthdays and leave starts are never persisted here; they are derived at query time in
/// <c>GetCompanyCalendarQuery</c>.
/// </summary>
public sealed class CompanyEvent : TenantAggregateRoot<Guid>, IAuditLogged
{
    // ReSharper disable once UnusedMember.Local
    private CompanyEvent() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateOnly EventDate { get; private set; }
    public CompanyEventType Type { get; private set; }
    public Guid? VisibilityOrgUnitId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public static Result<CompanyEvent> Create(
        Guid tenantId, string title, string? description, DateOnly eventDate, CompanyEventType type,
        Guid? visibilityOrgUnitId, Guid createdByUserId)
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

        return new CompanyEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            EventDate = eventDate,
            Type = type,
            VisibilityOrgUnitId = visibilityOrgUnitId,
            CreatedByUserId = createdByUserId,
        };
    }

    /// <summary>Manuel etkinliği düzenle (senseik-027 takip: roadmap #71 - önceden yalnız sil-yeniden oluşturma
    /// vardı). Sil-yeniden oluşturmanın aksine kimliği ve oluşturanı korur.</summary>
    public Result Update(
        string title, string? description, DateOnly eventDate, CompanyEventType type, Guid? visibilityOrgUnitId)
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

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        EventDate = eventDate;
        Type = type;
        VisibilityOrgUnitId = visibilityOrgUnitId;
        return Result.Success();
    }
}

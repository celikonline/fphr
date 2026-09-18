using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Duyuruları listele. publishedOnly=true iken yalnız yayınlanmış (ve süresi geçmemiş) duyurular döner.</summary>
[RequiresPermission("announcements.announcements.view")]
public sealed record ListAnnouncementsQuery(bool? PublishedOnly = null) : IQuery<IReadOnlyList<AnnouncementDto>>;

public sealed class ListAnnouncementsHandler(IAnnouncementRepository announcements, ITenantContext tenant)
    : IQueryHandler<ListAnnouncementsQuery, IReadOnlyList<AnnouncementDto>>
{
    public async Task<Result<IReadOnlyList<AnnouncementDto>>> Handle(ListAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        var list = query.PublishedOnly == true
            ? await announcements.GetPublishedAsync(tenant.TenantId, cancellationToken)
            : await announcements.GetAllAsync(tenant.TenantId, cancellationToken);

        IReadOnlyList<AnnouncementDto> dtos = list.Select(ToDto).ToList();
        return Result.Success(dtos);
    }

    internal static AnnouncementDto ToDto(Announcement a) => new(
        a.Id, a.Title, a.Content, a.Type.ToString(), a.Priority.ToString(), a.Status.ToString(),
        a.PublishedBy, a.PublishedAt, a.ExpiresAt, a.TargetAudience, a.CreatedAt);
}

/// <summary>Duyuruyu id'ye göre getir.</summary>
[RequiresPermission("announcements.announcements.view")]
public sealed record GetAnnouncementQuery(Guid AnnouncementId) : IQuery<AnnouncementDto>;

public sealed class GetAnnouncementHandler(IAnnouncementRepository announcements, ITenantContext tenant)
    : IQueryHandler<GetAnnouncementQuery, AnnouncementDto>
{
    public async Task<Result<AnnouncementDto>> Handle(GetAnnouncementQuery query, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, query.AnnouncementId, cancellationToken);
        return announcement is null
            ? Error.NotFound(AnnouncementDomainErrors.NotFound)
            : ListAnnouncementsHandler.ToDto(announcement);
    }
}

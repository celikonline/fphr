using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Akıştaki duyuru: yönetim alanları (durum, yayınlayan) olmadan, çağıranın okundu bilgisiyle.</summary>
public sealed record AnnouncementFeedItemDto(
    Guid Id,
    string Title,
    string Content,
    string Type,
    string Priority,
    DateTime? PublishedAt,
    DateTime? ExpiresAt,
    bool IsRead);

/// <summary>Çalışanın duyuru akışı: yalnız yayındaki ve süresi geçmemiş duyurular, en yeni önce.</summary>
[RequiresPermission("announcements.my.feed")]
public sealed record ListMyAnnouncementFeedQuery : IQuery<IReadOnlyList<AnnouncementFeedItemDto>>;

public sealed class ListMyAnnouncementFeedHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReadRecordRepository readRecords,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : IQueryHandler<ListMyAnnouncementFeedQuery, IReadOnlyList<AnnouncementFeedItemDto>>
{
    public async Task<Result<IReadOnlyList<AnnouncementFeedItemDto>>> Handle(
        ListMyAnnouncementFeedQuery query, CancellationToken cancellationToken)
    {
        var published = await announcements.GetPublishedAsync(tenant.TenantId, cancellationToken);

        // Çalışan kaydı olmayan kullanıcı (ör. yalnız yönetici hesabı) okundu kaydı tutamaz; hepsi okunmamış görünür.
        HashSet<Guid> readIds = currentUser.EmployeeId is { } employeeId
            ? [.. await readRecords.GetReadAnnouncementIdsAsync(tenant.TenantId, employeeId, cancellationToken)]
            : [];

        IReadOnlyList<AnnouncementFeedItemDto> items = published
            .Select(a => new AnnouncementFeedItemDto(
                a.Id, a.Title, a.Content, a.Type.ToString(), a.Priority.ToString(), a.PublishedAt, a.ExpiresAt, readIds.Contains(a.Id)))
            .ToList();
        return Result.Success(items);
    }
}

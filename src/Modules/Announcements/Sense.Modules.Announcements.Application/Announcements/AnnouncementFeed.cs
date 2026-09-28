using System.Text.Json;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Akıştaki öğe türü: duyuru veya çalışanlar arası takdir/teşekkür mesajı (roadmap #73).</summary>
public static class AnnouncementFeedItemKind
{
    public const string Announcement = "announcement";
    public const string Recognition = "recognition";
}

/// <summary>
/// Akıştaki öğe: yönetim alanları (durum, yayınlayan) olmadan, çağıranın okundu bilgisiyle. <see cref="Kind"/>
/// "recognition" iken <see cref="Title"/> gönderenin adı, <see cref="Content"/> mesaj metni, <see cref="IsRead"/>
/// her zaman true'dur (takdir mesajları için okundu takibi tutulmaz). <see cref="Type"/>/<see cref="Priority"/>
/// diğer uçlarla tutarlı camelCase değerler taşır (enum tipi değil `string`; bkz. QA-006/Karar 21) - üretilirken
/// <see cref="JsonNamingPolicy.CamelCase"/> ile dönüştürülür, `.ToString()` PascalCase üye adı kullanılmaz.
/// </summary>
public sealed record AnnouncementFeedItemDto(
    Guid Id,
    string Title,
    string Content,
    string Type,
    string Priority,
    DateTime? PublishedAt,
    DateTime? ExpiresAt,
    bool IsRead,
    string Kind = AnnouncementFeedItemKind.Announcement,
    Guid? SenderEmployeeId = null,
    Guid? ReceiverEmployeeId = null);

/// <summary>Çalışanın duyuru akışı: yayındaki ve süresi geçmemiş duyurular + son takdir/teşekkür mesajları, en yeni önce.</summary>
[RequiresPermission("announcements.my.feed")]
public sealed record ListMyAnnouncementFeedQuery : IQuery<IReadOnlyList<AnnouncementFeedItemDto>>;

public sealed class ListMyAnnouncementFeedHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReadRecordRepository readRecords,
    IRecognitionRepository recognitions,
    IEmployeeContactLookup employeeContacts,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : IQueryHandler<ListMyAnnouncementFeedQuery, IReadOnlyList<AnnouncementFeedItemDto>>
{
    /// <summary>Akışa dahil edilecek en yeni takdir/teşekkür mesajı sayısı (duyuru sayısı gibi ayrıca sınırlanmaz).</summary>
    private const int RecentRecognitionsTake = 50;

    /// <summary>
    /// Enum üye adını (ör. "HR") diğer uçlarla tutarlı camelCase dizgiye ("hr") çevirir; global
    /// `JsonStringEnumConverter(CamelCase)` yalnız gerçek enum alanlarına uygulandığından `string` alanlarda bu
    /// dönüşüm elle yapılır (QA-006/Karar 21).
    /// </summary>
    private static string ToCamelCase<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public async Task<Result<IReadOnlyList<AnnouncementFeedItemDto>>> Handle(
        ListMyAnnouncementFeedQuery query, CancellationToken cancellationToken)
    {
        var published = await announcements.GetPublishedAsync(tenant.TenantId, cancellationToken);

        // Çalışan kaydı olmayan kullanıcı (ör. yalnız yönetici hesabı) okundu kaydı tutamaz; hepsi okunmamış görünür.
        HashSet<Guid> readIds = currentUser.EmployeeId is { } employeeId
            ? [.. await readRecords.GetReadAnnouncementIdsAsync(tenant.TenantId, employeeId, cancellationToken)]
            : [];

        IEnumerable<AnnouncementFeedItemDto> announcementItems = published
            .Select(a => new AnnouncementFeedItemDto(
                a.Id, a.Title, a.Content, ToCamelCase(a.Type), ToCamelCase(a.Priority), a.PublishedAt, a.ExpiresAt, readIds.Contains(a.Id)));

        var recognitionItems = await BuildRecognitionItemsAsync(cancellationToken);

        IReadOnlyList<AnnouncementFeedItemDto> items = announcementItems
            .Concat(recognitionItems)
            .OrderByDescending(i => i.PublishedAt)
            .ToList();
        return Result.Success(items);
    }

    private async Task<IReadOnlyList<AnnouncementFeedItemDto>> BuildRecognitionItemsAsync(CancellationToken cancellationToken)
    {
        var recent = await recognitions.GetRecentAsync(tenant.TenantId, RecentRecognitionsTake, cancellationToken);
        if (recent.Count == 0)
        {
            return [];
        }

        var senderIds = recent.Select(r => r.SenderEmployeeId).Distinct().ToList();
        var senderNames = (await employeeContacts.GetContactsAsync(tenant.TenantId, senderIds, cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        return recent
            .Select(r => new AnnouncementFeedItemDto(
                r.Id,
                Title: senderNames.GetValueOrDefault(r.SenderEmployeeId, string.Empty),
                Content: r.Message,
                Type: AnnouncementFeedItemKind.Recognition,
                Priority: ToCamelCase(AnnouncementPriority.Normal),
                PublishedAt: r.CreatedAt,
                ExpiresAt: null,
                IsRead: true,
                Kind: AnnouncementFeedItemKind.Recognition,
                SenderEmployeeId: r.SenderEmployeeId,
                ReceiverEmployeeId: r.ReceiverEmployeeId))
            .ToList();
    }
}

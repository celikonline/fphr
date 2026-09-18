using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.ReadRecords;

public sealed record AnnouncementReadRecordDto(Guid Id, Guid AnnouncementId, Guid EmployeeId, DateTime ReadAt);

/// <summary>Bir duyurunun okunma kayıtlarını listele (İK analytics).</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record ListAnnouncementReadRecordsQuery(Guid AnnouncementId) : IQuery<IReadOnlyList<AnnouncementReadRecordDto>>;

public sealed class ListAnnouncementReadRecordsHandler(IAnnouncementReadRecordRepository readRecords, ITenantContext tenant)
    : IQueryHandler<ListAnnouncementReadRecordsQuery, IReadOnlyList<AnnouncementReadRecordDto>>
{
    public async Task<Result<IReadOnlyList<AnnouncementReadRecordDto>>> Handle(ListAnnouncementReadRecordsQuery query, CancellationToken cancellationToken)
    {
        var records = await readRecords.GetByAnnouncementIdAsync(tenant.TenantId, query.AnnouncementId, cancellationToken);
        IReadOnlyList<AnnouncementReadRecordDto> dtos = records
            .Select(r => new AnnouncementReadRecordDto(r.Id, r.AnnouncementId, r.EmployeeId, r.ReadAt))
            .ToList();

        return Result.Success(dtos);
    }
}

/// <summary>Duyurunun kaç kişi tarafından okunduğunu say.</summary>
[RequiresPermission("announcements.announcements.manage")]
public sealed record CountAnnouncementReadersQuery(Guid AnnouncementId) : IQuery<int>;

public sealed class CountAnnouncementReadersHandler(IAnnouncementReadRecordRepository readRecords, ITenantContext tenant)
    : IQueryHandler<CountAnnouncementReadersQuery, int>
{
    public async Task<Result<int>> Handle(CountAnnouncementReadersQuery query, CancellationToken cancellationToken) =>
        await readRecords.CountByAnnouncementIdAsync(tenant.TenantId, query.AnnouncementId, cancellationToken);
}

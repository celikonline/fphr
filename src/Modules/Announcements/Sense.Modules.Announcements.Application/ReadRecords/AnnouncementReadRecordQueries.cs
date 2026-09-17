using FluentValidation;
using Sense.Shared.Contracts.Queries;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Tenants;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.ReadRecords;

/// <summary>Duyuru okunma kayıtlarını listele (analytics).</summary>
[RequiresPermission("announcements.manage")]
public sealed record ListAnnouncementReadRecordsQuery(
    Guid TenantId,
    Guid AnnouncementId
) : IQuery<List<AnnouncementReadRecordDto>>;

public sealed record AnnouncementReadRecordDto(
    Guid Id,
    Guid AnnouncementId,
    Guid EmployeeId,
    DateTime ReadAt
);

public sealed class ListAnnouncementReadRecordsValidator : AbstractValidator<ListAnnouncementReadRecordsQuery>
{
    public ListAnnouncementReadRecordsValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEqual(Guid.Empty).WithMessage("Kiracı gereklidir.");

        RuleFor(x => x.AnnouncementId)
            .NotEqual(Guid.Empty).WithMessage("Duyuru gereklidir.");
    }
}

/// <summary>Duyuru okunma kayıtlarını listele — repository'den veri al.</summary>
public sealed class ListAnnouncementReadRecordsHandler(IAnnouncementReadRecordRepository readRecords, ITenantContext tenant)
    : IQueryHandler<ListAnnouncementReadRecordsQuery, List<AnnouncementReadRecordDto>>
{
    public async Task<Result<List<AnnouncementReadRecordDto>>> Handle(ListAnnouncementReadRecordsQuery request, CancellationToken cancellationToken)
    {
        var records = await readRecords.GetByAnnouncementIdAsync(tenant.TenantId, request.AnnouncementId, cancellationToken);

        var dtos = records
            .Select(r => new AnnouncementReadRecordDto(
                r.Id,
                r.AnnouncementId,
                r.EmployeeId,
                r.ReadAt
            ))
            .ToList();

        return dtos;
    }
}

/// <summary>Duyurunun kaç kişi tarafından okunduğunu say.</summary>
[RequiresPermission("announcements.manage")]
public sealed record CountAnnouncementReadersQuery(
    Guid TenantId,
    Guid AnnouncementId
) : IQuery<int>;

public sealed class CountAnnouncementReadersValidator : AbstractValidator<CountAnnouncementReadersQuery>
{
    public CountAnnouncementReadersValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEqual(Guid.Empty).WithMessage("Kiracı gereklidir.");

        RuleFor(x => x.AnnouncementId)
            .NotEqual(Guid.Empty).WithMessage("Duyuru gereklidir.");
    }
}

/// <summary>Duyurunun kaç kişi tarafından okunduğunu say — repository'den say.</summary>
public sealed class CountAnnouncementReadersHandler(IAnnouncementReadRecordRepository readRecords, ITenantContext tenant)
    : IQueryHandler<CountAnnouncementReadersQuery, int>
{
    public async Task<Result<int>> Handle(CountAnnouncementReadersQuery request, CancellationToken cancellationToken)
    {
        var count = await readRecords.CountByAnnouncementIdAsync(tenant.TenantId, request.AnnouncementId, cancellationToken);
        return count;
    }
}

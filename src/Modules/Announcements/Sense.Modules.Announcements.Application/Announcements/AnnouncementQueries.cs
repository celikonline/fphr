using Sense.Shared.Contracts.Queries;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Tenants;
using Sense.Shared.Kernel.Results;
using Sense.Modules.Announcements.Domain;
using FluentValidation;

namespace Sense.Modules.Announcements.Application.Announcements;

public record AnnouncementDto(
    Guid Id,
    string Title,
    string Content,
    DateTime CreatedAt,
    DateTime? PublishedAt
);

public record ListAnnouncementsQuery(
    Guid TenantId,
    bool? PublishedOnly = null
) : IQuery<List<AnnouncementDto>>;

public class ListAnnouncementsValidator : AbstractValidator<ListAnnouncementsQuery>
{
    public ListAnnouncementsValidator()
    {
        // Basic validation; extend as needed
    }
}

public sealed class ListAnnouncementsHandler(IAnnouncementRepository announcements, ITenantContext tenant)
    : IQueryHandler<ListAnnouncementsQuery, List<AnnouncementDto>>
{
    public async Task<Result<List<AnnouncementDto>>> Handle(ListAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        var announcementList = await announcements.GetAllAsync(tenant.TenantId, cancellationToken);

        var dtos = announcementList
            .Select(a => new AnnouncementDto(
                a.Id,
                a.Title,
                a.Content,
                a.CreatedAt,
                a.PublishedAt
            ))
            .ToList();

        return dtos;
    }
}

/// <summary>Duyuruyu ID'ye göre getir.</summary>
[RequiresPermission("announcements.view")]
public sealed record GetAnnouncementQuery(
    Guid AnnouncementId,
    Guid TenantId
) : IQuery<AnnouncementDto>;

public sealed class GetAnnouncementValidator : AbstractValidator<GetAnnouncementQuery>
{
    public GetAnnouncementValidator()
    {
        RuleFor(x => x.AnnouncementId)
            .NotEqual(Guid.Empty).WithMessage("Duyuru gereklidir.");

        RuleFor(x => x.TenantId)
            .NotEqual(Guid.Empty).WithMessage("Kiracı gereklidir.");
    }
}

/// <summary>Duyuruyu getir — repository'den veri al.</summary>
public sealed class GetAnnouncementHandler(IAnnouncementRepository announcements, ITenantContext tenant)
    : IQueryHandler<GetAnnouncementQuery, AnnouncementDto>
{
    public async Task<Result<AnnouncementDto>> Handle(GetAnnouncementQuery request, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, request.AnnouncementId, cancellationToken);
        if (announcement is null)
            return AnnouncementsErrors.AnnouncementNotFound;

        return new AnnouncementDto(
            announcement.Id,
            announcement.Title,
            announcement.Content,
            announcement.CreatedAt,
            announcement.PublishedAt
        );
    }
}

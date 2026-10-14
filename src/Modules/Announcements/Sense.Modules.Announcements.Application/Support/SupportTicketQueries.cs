using Sense.Modules.Announcements.Contracts;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Support;

/// <summary>Kendi destek taleplerimi listele.</summary>
[RequiresPermission("announcements.my.support_tickets")]
public sealed record ListMySupportTicketsQuery : IQuery<IReadOnlyList<SupportTicketDto>>;

public sealed class ListMySupportTicketsHandler(ITenantContext tenant, ICurrentUser currentUser, ISupportTicketRepository tickets,
    ISupportPortalBridge? bridge = null)
    : IQueryHandler<ListMySupportTicketsQuery, IReadOnlyList<SupportTicketDto>>
{
    public async Task<Result<IReadOnlyList<SupportTicketDto>>> Handle(ListMySupportTicketsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            IReadOnlyList<SupportTicketDto> empty = [];
            return Result.Success(empty);
        }

        var list = await tickets.ListByRequesterAsync(tenant.TenantId, userId, cancellationToken);
        var portal = bridge?.Enabled == true ? await bridge.ListAsync(list, cancellationToken) : null;
        IReadOnlyList<SupportTicketDto> dtos = list.Select(t => ToDto(t) with
        {
            Portal = portal is not null && portal.TryGetValue(t.Id, out var state) ? state : null
        }).ToList();
        return Result.Success(dtos);
    }

    internal static SupportTicketDto ToDto(SupportTicket t) => new(
        t.Id, t.RequestedByUserId, t.Subject, t.Body, t.Category, t.Channel, t.Status,
        t.SourceRoute, t.Resolution, t.AnsweredAt, t.ClosedAt, t.CreatedAt);
}

/// <summary>Kiracının tüm destek taleplerini listele (İK/Admin), isteğe bağlı durum filtresiyle.</summary>
[RequiresPermission("announcements.support_tickets.manage")]
public sealed record ListSupportTicketsQuery(SupportTicketStatus? Status = null) : IQuery<IReadOnlyList<SupportTicketDto>>;

public sealed class ListSupportTicketsHandler(ITenantContext tenant, ISupportTicketRepository tickets)
    : IQueryHandler<ListSupportTicketsQuery, IReadOnlyList<SupportTicketDto>>
{
    public async Task<Result<IReadOnlyList<SupportTicketDto>>> Handle(ListSupportTicketsQuery query, CancellationToken cancellationToken)
    {
        var list = await tickets.ListAsync(tenant.TenantId, query.Status, cancellationToken);
        IReadOnlyList<SupportTicketDto> dtos = list.Select(ListMySupportTicketsHandler.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>
/// Tek bir talebi getir. Sahibi kendi talebini "announcements.my.support_tickets" ile görebilir; başkasının
/// talebini görmek "announcements.support_tickets.manage" ister — RequiresPermission yalnız EN AZ birini şart
/// koşamadığından (attribute AND'lenir), yetki kararı burada handler içinde veriliyor.
/// </summary>
public sealed record GetSupportTicketQuery(Guid TicketId) : IQuery<SupportTicketDto>;

public sealed class GetSupportTicketHandler(
    ITenantContext tenant, ICurrentUser currentUser, IPermissionService permissions, ISupportTicketRepository tickets,
    ISupportPortalBridge? bridge = null)
    : IQueryHandler<GetSupportTicketQuery, SupportTicketDto>
{
    public async Task<Result<SupportTicketDto>> Handle(GetSupportTicketQuery query, CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetByIdAsync(tenant.TenantId, query.TicketId, cancellationToken);
        if (ticket is null)
        {
            return Error.NotFound(AnnouncementsErrors.TicketNotFound);
        }

        var isOwner = currentUser.UserId == ticket.RequestedByUserId;
        var canManage = currentUser.UserId is { } uid
            && await permissions.HasAsync(uid, AnnouncementsPermissions.ManageSupportTickets.Key, cancellationToken);

        if (!isOwner && !canManage)
        {
            // Varlık sızıntısı olmasın diye NotFound (Forbidden değil).
            return Error.NotFound(AnnouncementsErrors.TicketNotFound);
        }

        return ListMySupportTicketsHandler.ToDto(ticket) with
        {
            Portal = bridge?.Enabled == true ? await bridge.ReadAsync(ticket, cancellationToken) : null
        };
    }
}

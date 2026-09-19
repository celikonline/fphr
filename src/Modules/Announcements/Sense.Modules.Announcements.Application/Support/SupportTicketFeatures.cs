using FluentValidation;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Support;

/// <summary>
/// "Desteğe bağlan": records a tenant-scoped SupportTicket for the caller, hands it to <see cref="ISupportChannel"/>
/// (e-mail by default) and enqueues SupportTicketCreated in this module's outbox (same transaction as the ticket).
/// </summary>
[RequiresPermission("announcements.my.tickets")]
public sealed record CreateSupportTicketCommand(string Subject, string Body, SupportTicketCategory Category, string? PageContext) : ICommand<Guid>;

public sealed class CreateSupportTicketValidator : AbstractValidator<CreateSupportTicketCommand>
{
    public CreateSupportTicketValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(AnnouncementsLimits.TicketSubjectMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(AnnouncementsLimits.TicketBodyMaxLength);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.PageContext).MaximumLength(AnnouncementsLimits.PageContextMaxLength);
    }
}

public sealed class CreateSupportTicketHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    ISupportTicketRepository tickets,
    ISupportChannel channel,
    IIntegrationEventOutbox outbox)
    : ICommandHandler<CreateSupportTicketCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateSupportTicketCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(AnnouncementsErrors.UserRequired);
        }

        var created = SupportTicket.Create(
            tenant.TenantId, userId, command.Subject, command.Body, command.Category, SupportTicketChannel.Email, command.PageContext);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var ticket = created.Value;
        tickets.Add(ticket);

        await channel.SubmitTicketAsync(
            ticket, new SupportRequester(tenant.TenantId, userId, currentUser.Email, currentUser.DisplayName), cancellationToken);

        outbox.Enqueue(new SupportTicketCreated(
            ticket.TenantId, ticket.Id, userId, ticket.Subject, ticket.Category.ToString(), ticket.Channel.ToString(), ticket.PageContext));

        return ticket.Id;
    }
}

/// <summary>"Taleplerim": the caller's own support tickets, newest first.</summary>
[RequiresPermission("announcements.my.tickets")]
public sealed record ListMySupportTicketsQuery(PagedQuery Paging) : IQuery<PagedResult<SupportTicketDto>>;

public sealed class ListMySupportTicketsHandler(ICurrentUser currentUser, ISupportTicketRepository tickets)
    : IQueryHandler<ListMySupportTicketsQuery, PagedResult<SupportTicketDto>>
{
    public async Task<Result<PagedResult<SupportTicketDto>>> Handle(ListMySupportTicketsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(AnnouncementsErrors.UserRequired);
        }

        var total = await tickets.CountByUserAsync(userId, cancellationToken);
        var list = await tickets.ListByUserAsync(userId, query.Paging.Skip, query.Paging.PageSize, cancellationToken);
        var items = list.Select(SupportMapper.ToDto).ToList();
        return new PagedResult<SupportTicketDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }
}

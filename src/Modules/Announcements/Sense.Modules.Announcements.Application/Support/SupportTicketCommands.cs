using FluentValidation;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Modules.Announcements.Contracts.Events;

namespace Sense.Modules.Announcements.Application.Support;

/// <summary>
/// "Desteğe bağlan" formu (11.3.13 açık nokta 4: Faz 4'te canlı sohbet yerine tanı bağlamlı talep formu — kanal,
/// widget entegrasyonu ve KVKK aydınlatma metni gerektirdiğinden Faz 8 kararına bırakıldı). "*.my.*" self-service
/// izniyle her temel role (Employee/Intern dahil) ulaşır.
/// </summary>
[RequiresPermission("announcements.my.support_tickets")]
public sealed record CreateSupportTicketCommand(
    string Subject, string Body, SupportTicketCategory Category, string? SourceRoute = null) : ICommand<Guid>;

public sealed class CreateSupportTicketValidator : AbstractValidator<CreateSupportTicketCommand>
{
    public CreateSupportTicketValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(AnnouncementsLimits.SubjectMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(AnnouncementsLimits.BodyMaxLength);
    }
}

public sealed class CreateSupportTicketHandler(
    ITenantContext tenant, ICurrentUser currentUser, ISupportTicketRepository tickets, IIntegrationEventOutbox outbox)
    : ICommandHandler<CreateSupportTicketCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateSupportTicketCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var created = SupportTicket.Create(
            tenant.TenantId, userId, command.Subject, command.Body, command.Category, command.SourceRoute, currentUser.CorrelationId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        tickets.Add(created.Value);
        outbox.Enqueue(new SupportTicketCreated(tenant.TenantId, created.Value.Id, userId, created.Value.Subject, created.Value.Category.ToString()));
        return created.Value.Id;
    }
}

/// <summary>Destek ekibi/İK bir talebi yanıtlar; talep "Answered" olur ve açan kullanıcıya bildirim gider.</summary>
[RequiresPermission("announcements.support_tickets.manage")]
public sealed record RespondToSupportTicketCommand(Guid TicketId, string Resolution) : ICommand;

public sealed class RespondToSupportTicketValidator : AbstractValidator<RespondToSupportTicketCommand>
{
    public RespondToSupportTicketValidator() => RuleFor(x => x.Resolution).NotEmpty().MaximumLength(AnnouncementsLimits.ResolutionMaxLength);
}

public sealed class RespondToSupportTicketHandler(ITenantContext tenant, ISupportTicketRepository tickets, IIntegrationEventOutbox outbox)
    : ICommandHandler<RespondToSupportTicketCommand>
{
    public async Task<Result> Handle(RespondToSupportTicketCommand command, CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetByIdAsync(tenant.TenantId, command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return Error.NotFound(AnnouncementsErrors.TicketNotFound);
        }

        var result = ticket.Respond(command.Resolution);
        if (result.IsFailure)
        {
            return result;
        }

        outbox.Enqueue(new SupportTicketAnswered(tenant.TenantId, ticket.Id, ticket.RequestedByUserId, ticket.Subject));
        return Result.Success();
    }
}

/// <summary>Talebi kapat (destek ekibi/İK).</summary>
[RequiresPermission("announcements.support_tickets.manage")]
public sealed record CloseSupportTicketCommand(Guid TicketId) : ICommand;

public sealed class CloseSupportTicketHandler(ITenantContext tenant, ISupportTicketRepository tickets)
    : ICommandHandler<CloseSupportTicketCommand>
{
    public async Task<Result> Handle(CloseSupportTicketCommand command, CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetByIdAsync(tenant.TenantId, command.TicketId, cancellationToken);
        return ticket is null ? Error.NotFound(AnnouncementsErrors.TicketNotFound) : ticket.Close();
    }
}

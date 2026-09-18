using NSubstitute;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class SupportTicketCommandsTests
{
    [Fact]
    public async Task CreateSupportTicket_WithValidCommand_ShouldSucceedAndEnqueueEvent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var tickets = Substitute.For<ISupportTicketRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var handler = new CreateSupportTicketHandler(tenant, currentUser, tickets, outbox);
        var command = new CreateSupportTicketCommand("Veri aktarımı hata veriyor", "Şablon yüklenmiyor.", SupportTicketCategory.DataTransfer, "/admin/settings/import");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        tickets.Received(1).Add(Arg.Is<SupportTicket>(t => t.TenantId == tenantId && t.RequestedByUserId == userId && t.Status == SupportTicketStatus.Open));
        outbox.Received(1).Enqueue(Arg.Is<SupportTicketCreated>(e => e.TenantId == tenantId && e.RequestedByUserId == userId));
    }

    [Fact]
    public async Task RespondToSupportTicket_WhenOpen_ShouldAnswerAndEnqueueEvent()
    {
        var tenantId = Guid.NewGuid();
        var ticket = SupportTicket.Create(tenantId, Guid.NewGuid(), "Konu", "Gövde", SupportTicketCategory.General).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var tickets = Substitute.For<ISupportTicketRepository>();
        tickets.GetByIdAsync(tenantId, ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var handler = new RespondToSupportTicketHandler(tenant, tickets, outbox);
        var result = await handler.Handle(new RespondToSupportTicketCommand(ticket.Id, "Şablonu güncelledik."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SupportTicketStatus.Answered, ticket.Status);
        outbox.Received(1).Enqueue(Arg.Any<SupportTicketAnswered>());
    }

    [Fact]
    public async Task CloseSupportTicket_WhenAlreadyClosed_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var ticket = SupportTicket.Create(tenantId, Guid.NewGuid(), "Konu", "Gövde", SupportTicketCategory.General).Value;
        ticket.Close();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var tickets = Substitute.For<ISupportTicketRepository>();
        tickets.GetByIdAsync(tenantId, ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);

        var handler = new CloseSupportTicketHandler(tenant, tickets);
        var result = await handler.Handle(new CloseSupportTicketCommand(ticket.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SupportDomainErrors.AlreadyClosed, result.Error.Code);
    }
}

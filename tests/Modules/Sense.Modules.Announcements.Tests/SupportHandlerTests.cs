using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Modules.Announcements.Infrastructure.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Notifications;
using Shouldly;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public sealed class SupportHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static ICurrentUser User(Guid? userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.Email.Returns("ayse@acme.test");
        user.DisplayName.Returns("Ayşe Yılmaz");
        return user;
    }

    [Fact]
    public async Task CreateSupportTicket_AddsTicket_SubmitsToChannel_AndEnqueuesEvent()
    {
        var tickets = Substitute.For<ISupportTicketRepository>();
        var channel = Substitute.For<ISupportChannel>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new CreateSupportTicketHandler(Tenant(), User(UserId), tickets, channel, outbox);

        var result = await handler.Handle(
            new CreateSupportTicketCommand("İzin bakiyesi yanlış", "Bakiyem 2 gün eksik görünüyor.", SupportTicketCategory.Problem, "/app/leave"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        tickets.Received(1).Add(Arg.Is<SupportTicket>(t => t.Id == result.Value && t.UserId == UserId && t.TenantId == TenantId && t.PageContext == "/app/leave"));
        await channel.Received(1).SubmitTicketAsync(
            Arg.Is<SupportTicket>(t => t.Id == result.Value),
            Arg.Is<SupportRequester>(r => r.UserId == UserId && r.TenantId == TenantId && r.Email == "ayse@acme.test"),
            Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(Arg.Is<SupportTicketCreated>(e =>
            e.TicketId == result.Value && e.TenantId == TenantId && e.UserId == UserId
            && e.Category == nameof(SupportTicketCategory.Problem) && e.Channel == nameof(SupportTicketChannel.Email)
            && e.PageContext == "/app/leave"));
    }

    [Fact]
    public async Task CreateSupportTicket_WithoutUser_FailsWithoutSideEffects()
    {
        var tickets = Substitute.For<ISupportTicketRepository>();
        var channel = Substitute.For<ISupportChannel>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new CreateSupportTicketHandler(Tenant(), User(null), tickets, channel, outbox);

        var result = await handler.Handle(new CreateSupportTicketCommand("Konu", "Metin", SupportTicketCategory.Question, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AnnouncementsErrors.UserRequired);
        tickets.DidNotReceive().Add(Arg.Any<SupportTicket>());
        await channel.DidNotReceive().SubmitTicketAsync(Arg.Any<SupportTicket>(), Arg.Any<SupportRequester>(), Arg.Any<CancellationToken>());
        outbox.DidNotReceive().Enqueue(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task EmailSupportChannel_SendsToConfiguredSupportInbox()
    {
        var sender = Substitute.For<IEmailSender>();
        var channel = new EmailSupportChannel(
            sender, Options.Create(new SupportOptions { SupportEmail = "destek@example.test" }), NullLogger<EmailSupportChannel>.Instance);
        var ticket = SupportTicket.Create(TenantId, UserId, "Konu", "Detay metni", SupportTicketCategory.Question, SupportTicketChannel.Email, "/app/payments").Value;

        await channel.SubmitTicketAsync(ticket, new SupportRequester(TenantId, UserId, "ayse@acme.test", "Ayşe"), CancellationToken.None);

        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToAddress == "destek@example.test" && m.Subject.Contains("Konu") && m.TextBody.Contains("Detay metni")
                && m.TextBody.Contains("ayse@acme.test") && m.TextBody.Contains("/app/payments")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailSupportChannel_WhenNotConfiguredOrSendFails_DoesNotThrow()
    {
        var ticket = SupportTicket.Create(TenantId, UserId, "Konu", "Metin", SupportTicketCategory.Other, SupportTicketChannel.Email, null).Value;
        var requester = new SupportRequester(TenantId, UserId, null, null);

        var unconfiguredSender = Substitute.For<IEmailSender>();
        var unconfigured = new EmailSupportChannel(unconfiguredSender, Options.Create(new SupportOptions()), NullLogger<EmailSupportChannel>.Instance);
        await unconfigured.SubmitTicketAsync(ticket, requester, CancellationToken.None);
        await unconfiguredSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());

        var failingSender = Substitute.For<IEmailSender>();
        failingSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("smtp down"));
        var failing = new EmailSupportChannel(
            failingSender, Options.Create(new SupportOptions { SupportEmail = "destek@example.test" }), NullLogger<EmailSupportChannel>.Instance);
        await Should.NotThrowAsync(() => failing.SubmitTicketAsync(ticket, requester, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(7, 7)]
    [InlineData(0, 1)]
    [InlineData(10000, 365)]
    public async Task GetUpcomingEvents_UsesClampedWindowFromNow(int? requestedDays, int expectedDays)
    {
        var now = new DateTimeOffset(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.ListUpcomingEventsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Announcement>());
        var handler = new GetUpcomingEventsHandler(repository, new FakeTimeProvider(now));

        var result = await handler.Handle(new GetUpcomingEventsQuery(requestedDays, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await repository.Received(1).ListUpcomingEventsAsync(
            now.UtcDateTime, now.UtcDateTime.AddDays(expectedDays), GetUpcomingEventsHandler.DefaultLimit, Arg.Any<CancellationToken>());
    }
}

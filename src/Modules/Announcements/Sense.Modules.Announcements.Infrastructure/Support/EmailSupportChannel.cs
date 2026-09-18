using Microsoft.Extensions.Options;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Modules.Announcements.Infrastructure.Support;

/// <summary>
/// 11.3.13's default ISupportChannel adapter: plain e-mail (SupportOptions.SupportEmail for new tickets, the
/// requester's own address for answers). WebWidgetSupportChannel/InAppChatSupportChannel are Faz 8 [Aday] and not
/// implemented here — SupportOptions.LiveChat exists only as an unused config placeholder for that decision.
/// </summary>
public sealed class EmailSupportChannel(
    IEmailSender sender, IOptions<SupportOptions> options, ITenantContext tenant, IUserProfileLookup userProfiles) : ISupportChannel
{
    public async Task NotifyTicketCreatedAsync(Guid ticketId, string subject, string category, CancellationToken cancellationToken = default)
    {
        var to = options.Value.SupportEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        var body = $"Yeni destek talebi ({category}): {subject}\nTalep: {ticketId}\nKiracı: {tenant.TenantId}";
        await sender.SendAsync(new EmailMessage(to, ToName: null, $"[Destek] {subject}", body), cancellationToken);
    }

    public async Task NotifyTicketAnsweredAsync(Guid ticketId, Guid requestedByUserId, string subject, CancellationToken cancellationToken = default)
    {
        var profiles = await userProfiles.GetAsync(tenant.TenantId, [requestedByUserId], cancellationToken);
        if (!profiles.TryGetValue(requestedByUserId, out var profile))
        {
            return;
        }

        var body = $"Destek talebiniz yanıtlandı: {subject}\nDetaylar için Destek > Taleplerim bölümüne bakın.";
        await sender.SendAsync(new EmailMessage(profile.Email, profile.DisplayName, $"[Destek] {subject}", body), cancellationToken);
    }
}

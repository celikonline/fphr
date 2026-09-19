using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Announcements.Application;

/// <summary>Announcements module's UnitOfWork surface for the command pipeline; implemented by AnnouncementsDbContext.</summary>
public interface IAnnouncementsUnitOfWork : IUnitOfWork;

/// <summary>Who opened a support ticket - resolved from ICurrentUser, passed to the channel so support can reply.</summary>
public sealed record SupportRequester(Guid TenantId, Guid UserId, string? Email, string? DisplayName);

/// <summary>
/// Delivery port for "Desteğe bağlan" tickets (11.3.13 ISupportChannel, trimmed to ticket submission). Default adapter:
/// Infrastructure's EmailSupportChannel (mails SupportOptions.SupportEmail). Live chat providers (web widget / in-app
/// chat) are Faz 8 adapters of the same port. Implementations must not throw for delivery failures: the ticket is
/// already recorded and stays visible in "Taleplerim".
/// </summary>
public interface ISupportChannel
{
    Task SubmitTicketAsync(SupportTicket ticket, SupportRequester requester, CancellationToken ct);
}

/// <summary>Support contact settings, bound from the "Support" configuration section (no addresses/URLs in code).</summary>
public sealed class SupportOptions
{
    public const string Section = "Support";

    /// <summary>Inbox that receives ticket e-mails (EmailSupportChannel).</summary>
    public string SupportEmail { get; set; } = string.Empty;

    /// <summary>General contact address shown on the Destek page.</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>Help video channel (e.g. YouTube) linked from the Help Center.</summary>
    public string HelpVideoChannelUrl { get; set; } = string.Empty;
}

namespace Sense.Shared.Contracts.Notifications;

/// <summary>An outgoing SMS. <paramref name="ToNumber"/> is E.164 (e.g. +905551112233).</summary>
public sealed record SmsMessage(string ToNumber, string Text);

/// <summary>
/// SMS gönderim portu. Sağlayıcı yapılandırılmamışsa <see cref="IsAvailable"/> false döner ve çağıranlar SMS kanalını
/// reddeder (K5); gerçek sağlayıcı adaptörü sonraki fazda eklenir.
/// </summary>
public interface ISmsSender
{
    bool IsAvailable { get; }

    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}

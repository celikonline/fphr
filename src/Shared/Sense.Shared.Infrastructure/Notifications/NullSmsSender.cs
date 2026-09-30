using Microsoft.Extensions.Logging;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Shared.Infrastructure.Notifications;

/// <summary>SMS sağlayıcısı yokken varsayılan: kullanılamaz; yanlışlıkla çağrılırsa yalnız loglar.</summary>
public sealed partial class NullSmsSender(ILogger<NullSmsSender> logger) : ISmsSender
{
    public bool IsAvailable => false;

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        Log.Skipped(logger, message.ToNumber);
        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1402, Level = LogLevel.Warning, Message = "SMS provider not configured; skipped SMS to {To}")]
        public static partial void Skipped(ILogger logger, string to);
    }
}

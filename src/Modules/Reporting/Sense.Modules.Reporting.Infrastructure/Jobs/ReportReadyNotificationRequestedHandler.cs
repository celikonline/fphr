using System.Globalization;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Reporting.Infrastructure.Jobs;

/// <summary>Outbox delivery keeps notification retry independent from the already persisted report result.</summary>
public sealed class ReportReadyNotificationRequestedHandler(IReportMailer mailer)
    : IIntegrationEventHandler<ReportReadyNotificationRequested>
{
    public async Task Handle(ReportReadyNotificationRequested notification, CancellationToken cancellationToken)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(notification.CultureName);
            await mailer.SendReportReadyAsync(notification.Email, notification.DisplayName, notification.ExecutionId,
                notification.ReportKey, cancellationToken);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}

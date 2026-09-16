using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Reporting.Application;

/// <summary>Reporting module's UnitOfWork surface for the command pipeline; implemented by ReportingDbContext.</summary>
public interface IReportingUnitOfWork : IUnitOfWork;

/// <summary>Sends the "your report is ready" email; implemented in Infrastructure over the shared IEmailSender (mirrors Identity's IIdentityMailer).</summary>
public interface IReportMailer
{
    Task SendReportReadyAsync(string toEmail, string? toName, Guid executionId, string reportKey, CancellationToken cancellationToken);
}

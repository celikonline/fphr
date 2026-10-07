using System.Text.Json;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Reporting.Infrastructure.Jobs;

public sealed class ReportGenerationRequestedHandler(IBackgroundJobService jobs) : IIntegrationEventHandler<ReportGenerationRequested>
{
    public async Task Handle(ReportGenerationRequested request, CancellationToken cancellationToken) =>
        await jobs.EnqueueAsync(JobType.ExportReport, request.TenantId, JsonSerializer.Serialize(request));
}

using Sense.Modules.Reporting.Contracts;

namespace Sense.Modules.Reporting.Infrastructure.Jobs;

public sealed class ReportGenerationContext : IReportGenerationContext
{
    public bool IsBackground { get; private set; }
    public IReadOnlySet<Guid>? VisibleEmployeeIds { get; private set; }
    public bool CanViewSensitive { get; private set; }

    public void Set(ReportGenerationRequested request)
    {
        IsBackground = true;
        VisibleEmployeeIds = request.VisibleEmployeeIds?.ToHashSet();
        CanViewSensitive = request.CanViewSensitive;
    }
}

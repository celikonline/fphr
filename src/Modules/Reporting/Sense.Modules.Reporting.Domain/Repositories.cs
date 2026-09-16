using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Domain;

public interface IReportExecutionRepository
{
    void Add(ReportExecution execution);

    Task<Result<ReportExecution>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ReportExecution> Items, int Total)> ListByRequesterAsync(
        Guid requestedByUserId, int skip, int take, CancellationToken cancellationToken = default);
}

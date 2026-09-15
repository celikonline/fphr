using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

/// <summary>"Bana atanan formlar" (Uygulamalar → Performans self-servis).</summary>
[RequiresPermission("performance.my.assignments")]
public sealed record ListMyAssignmentsQuery : IQuery<IReadOnlyList<MyAssignmentDto>>;

public sealed class ListMyAssignmentsHandler(ICurrentUser currentUser, IReviewCycleRepository cycles)
    : IQueryHandler<ListMyAssignmentsQuery, IReadOnlyList<MyAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<MyAssignmentDto>>> Handle(ListMyAssignmentsQuery query, CancellationToken cancellationToken)
    {
        var employeeId = currentUser.EmployeeId ?? Guid.Empty;
        var list = await cycles.ListWithAssignmentsForEvaluatorAsync(employeeId, cancellationToken);

        IReadOnlyList<MyAssignmentDto> dtos = list
            .SelectMany(cycle => cycle.Participants
                .SelectMany(p => p.Assignments.Where(a => a.EvaluatorEmployeeId == employeeId))
                .Select(a => new MyAssignmentDto(
                    cycle.Id, cycle.Name, cycle.PeriodStart, cycle.PeriodEnd,
                    a.Id, a.Relation, FindParticipant(cycle, a.Id), a.Status)))
            .ToList();

        return Result.Success(dtos);
    }

    private static Guid FindParticipant(ReviewCycle cycle, Guid assignmentId) =>
        cycle.Participants.First(p => p.Assignments.Any(a => a.Id == assignmentId)).EmployeeId;
}

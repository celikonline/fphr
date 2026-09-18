using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.JobRequisitions;

/// <summary>Lists job requisitions, optionally filtered to one status.</summary>
[RequiresPermission("recruitment.requisitions.read")]
public sealed record ListJobRequisitionsQuery(string? Status, int Take = PagingDefaults.MaxPageSize) : IQuery<IReadOnlyList<JobRequisitionDto>>;

public sealed class ListJobRequisitionsHandler(IJobRequisitionRepository requisitions)
    : IQueryHandler<ListJobRequisitionsQuery, IReadOnlyList<JobRequisitionDto>>
{
    public async Task<Result<IReadOnlyList<JobRequisitionDto>>> Handle(ListJobRequisitionsQuery query, CancellationToken cancellationToken)
    {
        RequisitionStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<RequisitionStatus>(query.Status, ignoreCase: true, out var parsed))
            {
                return Error.Validation(ErrorCodes.ValidationError, ("Status", query.Status));
            }

            status = parsed;
        }

        var list = await requisitions.ListAsync(status, query.Take, cancellationToken);
        IReadOnlyList<JobRequisitionDto> dtos = list.Select(JobRequisitionMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>A single job requisition by id.</summary>
[RequiresPermission("recruitment.requisitions.read")]
public sealed record GetJobRequisitionQuery(Guid RequisitionId) : IQuery<JobRequisitionDto>;

public sealed class GetJobRequisitionHandler(IJobRequisitionRepository requisitions) : IQueryHandler<GetJobRequisitionQuery, JobRequisitionDto>
{
    public async Task<Result<JobRequisitionDto>> Handle(GetJobRequisitionQuery query, CancellationToken cancellationToken)
    {
        var existing = await requisitions.GetByIdAsync(query.RequisitionId, cancellationToken);
        return existing.IsFailure ? existing.Error : JobRequisitionMapper.ToDto(existing.Value);
    }
}

internal static class JobRequisitionMapper
{
    public static JobRequisitionDto ToDto(JobRequisition requisition) =>
        new(
            requisition.Id,
            requisition.Title,
            requisition.OrgUnitId,
            requisition.EmploymentType.ToString(),
            requisition.HeadcountRequested,
            requisition.Reason.ToString(),
            requisition.Justification,
            requisition.RequesterEmployeeId,
            requisition.Status.ToString(),
            requisition.ApprovalRequestId,
            requisition.DecidedAt);
}

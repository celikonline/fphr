using Sense.Modules.Assets.Domain;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Assets;

/// <summary>An asset's assignment ledger, newest first ("bu cihaz kimdeydi").</summary>
[RequiresPermission("assets.assets.view")]
public sealed record GetAssetHistoryQuery(Guid AssetId) : IQuery<IReadOnlyList<AssetAssignmentDto>>;

public sealed class GetAssetHistoryHandler(IAssetRepository assets) : IQueryHandler<GetAssetHistoryQuery, IReadOnlyList<AssetAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<AssetAssignmentDto>>> Handle(GetAssetHistoryQuery query, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(query.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var ledger = await assets.ListAssignmentsAsync(query.AssetId, cancellationToken);
        IReadOnlyList<AssetAssignmentDto> dtos = ledger.Select(AssetMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Every asset the employee ever held (open and closed), newest first - the employee card's history (HR/manager view).</summary>
[RequiresPermission("assets.assets.view")]
public sealed record GetEmployeeAssetHistoryQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeeAssetHistoryDto>>;

public sealed class GetEmployeeAssetHistoryHandler(IAssetRepository assets)
    : IQueryHandler<GetEmployeeAssetHistoryQuery, IReadOnlyList<EmployeeAssetHistoryDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeAssetHistoryDto>>> Handle(GetEmployeeAssetHistoryQuery query, CancellationToken cancellationToken)
    {
        var history = await assets.ListEmployeeHistoryAsync(query.EmployeeId, cancellationToken);
        IReadOnlyList<EmployeeAssetHistoryDto> dtos = history.Select(AssetMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Self-service: every asset the caller ever held, newest first.</summary>
[RequiresPermission("assets.my.assets")]
[RequiresAccessSetting(AccessSettingKeys.HomeShowAssets)]
public sealed record GetMyAssetHistoryQuery : IQuery<IReadOnlyList<EmployeeAssetHistoryDto>>;

public sealed class GetMyAssetHistoryHandler(ICurrentUser currentUser, IAssetRepository assets)
    : IQueryHandler<GetMyAssetHistoryQuery, IReadOnlyList<EmployeeAssetHistoryDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeAssetHistoryDto>>> Handle(GetMyAssetHistoryQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AssetsErrors.NoEmployeeForUser);
        }

        var history = await assets.ListEmployeeHistoryAsync(employeeId, cancellationToken);
        IReadOnlyList<EmployeeAssetHistoryDto> dtos = history.Select(AssetMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

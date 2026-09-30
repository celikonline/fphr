using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// One direct report on "Ekibim" (FG-007). Same non-sensitive fields as the company directory (<see cref="DirectoryEntryDto"/>);
/// Title/Department come from the org placement chain so the caller needs no organization.units.read.
/// </summary>
public sealed record MyTeamMemberDto(Guid EmployeeId, string DisplayName, string? TitleName, string? DepartmentName, string? WorkEmail);

/// <summary>
/// "Ekibim" (FG-007): the caller's direct reports (ManagerEmployeeId == caller), not terminated, ordered by name. The caller is
/// always <see cref="ICurrentUser.EmployeeId"/> - no employee id is taken from the request - and a caller who manages nobody
/// (or has no linked employee) gets an empty list.
/// </summary>
[RequiresPermission(EmployeePermissions.MyTeamKey)]
public sealed record GetMyTeamQuery : IQuery<IReadOnlyList<MyTeamMemberDto>>;

public sealed class GetMyTeamHandler(ICurrentUser currentUser, IEmployeeReadStore store, OrgPlacementLoader placementLoader)
    : IQueryHandler<GetMyTeamQuery, IReadOnlyList<MyTeamMemberDto>>
{
    public async Task<Result<IReadOnlyList<MyTeamMemberDto>>> Handle(GetMyTeamQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } managerId)
        {
            return Result.Success<IReadOnlyList<MyTeamMemberDto>>([]);
        }

        var rows = await store.ListAllAsync(new EmployeeCriteria(ManagerEmployeeId: managerId, ActiveOnly: true), cancellationToken);
        if (rows.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyTeamMemberDto>>([]);
        }

        var placement = rows.Any(r => r.OrgUnitId is not null) ? await placementLoader.LoadAsync(cancellationToken) : null;
        return rows
            .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new MyTeamMemberDto(
                r.Id,
                r.DisplayName,
                placement?.LevelUnitFor(r.OrgUnitId, "Title")?.Name,
                placement?.LevelUnitFor(r.OrgUnitId, "Department")?.Name,
                r.WorkEmail))
            .ToList();
    }
}

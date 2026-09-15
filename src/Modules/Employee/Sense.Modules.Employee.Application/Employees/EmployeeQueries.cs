using FluentValidation;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Grid-standard employee list (?page&amp;pageSize&amp;sort&amp;q + filters), filtered by the caller's data scope.</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record ListEmployeesQuery(PagedQuery Paging, EmployeeFilter Filter) : IQuery<PagedResult<EmployeeListItemDto>>;

public sealed class ListEmployeesValidator : AbstractValidator<ListEmployeesQuery>
{
    public ListEmployeesValidator()
    {
        RuleFor(x => x.Filter.Status)
            .Must(s => EmployeeSelectionResolver.TryParseStatus(s, out _))
            .WithMessage(EmployeeErrors.StatusInvalid);
    }
}

public sealed class ListEmployeesHandler(
    ITenantContext tenant,
    IEmployeeReadStore store,
    EmployeeSelectionResolver resolver,
    IUserAccountLookup accounts) : IQueryHandler<ListEmployeesQuery, PagedResult<EmployeeListItemDto>>
{
    public async Task<Result<PagedResult<EmployeeListItemDto>>> Handle(ListEmployeesQuery query, CancellationToken cancellationToken)
    {
        var criteria = await resolver.BuildCriteriaAsync(tenant.TenantId, query.Filter, EmployeePermissions.EmployeesReadKey, cancellationToken);
        if (criteria is null)
        {
            return PagedResult<EmployeeListItemDto>.Empty(query.Paging);
        }

        var page = await store.ListAsync(criteria, query.Paging, cancellationToken);

        var managerIds = page.Items.Where(e => e.ManagerEmployeeId is not null).Select(e => e.ManagerEmployeeId!.Value).Distinct().ToList();
        var managerNames = await store.GetDisplayNamesAsync(managerIds, cancellationToken);

        var userIds = page.Items.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).Distinct().ToList();
        var accountInfo = userIds.Count == 0
            ? new Dictionary<Guid, UserAccountInfo>()
            : await accounts.GetAsync(tenant.TenantId, userIds, cancellationToken);

        return page.Map(e => new EmployeeListItemDto(
            e.Id,
            e.FirstName,
            e.LastName,
            e.DisplayName,
            Domain.Employees.Employee.MaskNationalId(e.NationalId),
            e.WorkEmail,
            e.PersonalEmail,
            e.WorkPhone,
            e.Status.ToString(),
            e.OrgUnitId,
            e.ManagerEmployeeId,
            e.ManagerEmployeeId is { } m ? managerNames.GetValueOrDefault(m) : null,
            e.UserId,
            e.UserId is { } u && accountInfo.TryGetValue(u, out var info) ? info.Status : UserAccountStatus.None,
            e.HireDate));
    }
}

/// <summary>Employee card; out-of-scope targets return 404 (IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record GetEmployeeQuery(Guid Id) : IQuery<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class GetEmployeeHandler(EmployeeDetailBuilder builder, IEmployeeReadStore store) : IQueryHandler<GetEmployeeQuery, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(GetEmployeeQuery query, CancellationToken cancellationToken)
    {
        var row = await store.GetAsync(query.Id, cancellationToken);
        if (row is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        return await builder.BuildAsync(row, cancellationToken);
    }
}

/// <summary>Builds <see cref="EmployeeDetailDto"/> (manager name + Identity account info); masks sensitive fields per employee.sensitive.view.</summary>
public sealed class EmployeeDetailBuilder(
    ITenantContext tenant,
    IEmployeeReadStore store,
    IUserAccountLookup accounts,
    EmployeeSensitiveAccess sensitiveAccess,
    CustomFieldValueReader customFields,
    TimeProvider clock)
{
    /// <summary>From a tracked aggregate (command responses, before SaveChanges): career records are in memory, so PositionStartDate is derived here.</summary>
    public Task<EmployeeDetailDto> BuildAsync(Domain.Employees.Employee employee, CancellationToken ct) =>
        BuildAsync(EmployeeRow.From(employee, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), customFieldValuesOverride: null, ct);

    /// <summary>
    /// From a tracked aggregate whose custom field values were just written in this same command (self-service profile
    /// edit): <paramref name="customFieldValues"/> must come from <see cref="CustomFieldValueReader.BuildFromAggregateAsync"/>
    /// on that same aggregate - a plain read-store lookup would still see the pre-commit (stale) row.
    /// </summary>
    public Task<EmployeeDetailDto> BuildAsync(Domain.Employees.Employee employee, IReadOnlyList<CustomFieldValueDto> customFieldValues, CancellationToken ct) =>
        BuildAsync(EmployeeRow.From(employee, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), customFieldValues, ct);

    public Task<EmployeeDetailDto> BuildAsync(EmployeeRow e, CancellationToken ct) => BuildAsync(e, customFieldValuesOverride: null, ct);

    private async Task<EmployeeDetailDto> BuildAsync(EmployeeRow e, IReadOnlyList<CustomFieldValueDto>? customFieldValuesOverride, CancellationToken ct)
    {
        string? managerName = null;
        if (e.ManagerEmployeeId is { } managerId)
        {
            var names = await store.GetDisplayNamesAsync([managerId], ct).ConfigureAwait(false);
            managerName = names.GetValueOrDefault(managerId);
        }

        UserAccountInfo? account = null;
        if (e.UserId is { } userId)
        {
            var infos = await accounts.GetAsync(tenant.TenantId, [userId], ct).ConfigureAwait(false);
            account = infos.GetValueOrDefault(userId);
        }

        var canViewSensitive = await sensitiveAccess.CanViewAsync(ct).ConfigureAwait(false);
        var customFieldValues = customFieldValuesOverride ?? await customFields.BuildAsync(e, ct).ConfigureAwait(false);

        return new EmployeeDetailDto(
            e.Id,
            e.FirstName,
            e.LastName,
            e.DisplayName,
            canViewSensitive ? e.NationalId : Domain.Employees.Employee.MaskNationalId(e.NationalId),
            e.WorkEmail,
            e.PersonalEmail,
            e.WorkPhone,
            e.HireDate,
            e.BirthDate,
            e.Status.ToString(),
            e.OrgUnitId,
            e.ManagerEmployeeId,
            managerName,
            e.UserId,
            account?.Status ?? UserAccountStatus.None,
            account?.AccessType,
            account?.LastInvitationSentAt,
            e.Gender,
            e.MaritalStatus,
            e.ChildCount,
            e.EducationStatus,
            e.HighestEducationLevel,
            canViewSensitive ? e.MilitaryStatus : null,
            canViewSensitive ? e.DisabilityDegree : null,
            canViewSensitive ? e.BloodType : null,
            e.MobilePhone,
            e.HomePhone,
            e.AddressLine,
            e.City,
            e.PostalCode,
            e.ContractType,
            e.EmploymentType,
            e.SuspensionReason?.ToString(),
            e.LeaveAccrualStartDate,
            e.PositionStartDate,
            customFieldValues,
            e.TerminationDate,
            e.TerminationReasonId,
            e.TerminationType,
            e.SgkExitCode);
    }
}

/// <summary>The caller's own employee card ("Kendi kartım"); 404 when the caller has no linked employee record.</summary>
[RequiresPermission(EmployeePermissions.MyViewKey)]
public sealed record GetMyEmployeeQuery : IQuery<EmployeeDetailDto>;

public sealed class GetMyEmployeeHandler(ICurrentUser currentUser, IEmployeeReadStore store, EmployeeDetailBuilder builder)
    : IQueryHandler<GetMyEmployeeQuery, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(GetMyEmployeeQuery query, CancellationToken cancellationToken)
    {
        // The eid claim can lag behind a link made after sign-in; fall back to the user link on the employee row.
        var row = currentUser.EmployeeId is { } employeeId ? await store.GetAsync(employeeId, cancellationToken) : null;
        if (row is null && currentUser.UserId is { } userId)
        {
            row = await store.GetByUserIdAsync(userId, cancellationToken);
        }

        if (row is null || (row.UserId is { } linkedUserId && linkedUserId != currentUser.UserId))
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        return await builder.BuildAsync(row, cancellationToken);
    }
}

/// <summary>
/// Company directory: active employees with work contact only. Deliberately not data-scope filtered (a Self-scoped
/// Employee role must still see colleagues); exposes no sensitive fields. WorkPhone is masked when the tenant's
/// "Account.ShowWorkPhoneNumbers" access setting is off for the caller.
/// </summary>
[RequiresPermission(EmployeePermissions.DirectoryViewKey)]
public sealed record GetEmployeeDirectoryQuery(PagedQuery Paging, Guid? OrgUnitId) : IQuery<PagedResult<DirectoryEntryDto>>;

public sealed class GetEmployeeDirectoryHandler(IEmployeeReadStore store, IAccessSettingsService accessSettings)
    : IQueryHandler<GetEmployeeDirectoryQuery, PagedResult<DirectoryEntryDto>>
{
    public async Task<Result<PagedResult<DirectoryEntryDto>>> Handle(GetEmployeeDirectoryQuery query, CancellationToken cancellationToken)
    {
        var criteria = new EmployeeCriteria(
            Q: query.Paging.Q,
            OrgUnitIds: query.OrgUnitId is { } orgUnitId ? [orgUnitId] : null,
            ActiveOnly: true,
            SearchNationalId: false);

        var paging = query.Paging with { Sort = EmployeeSortFields.DisplayName, Filters = [] };
        var page = await store.ListAsync(criteria, paging, cancellationToken);
        var showPhones = await accessSettings.IsEnabledForCurrentUserAsync(AccessSettingKeys.AccountShowWorkPhoneNumbers, cancellationToken);
        return page.Map(e => new DirectoryEntryDto(e.Id, e.DisplayName, e.OrgUnitId, e.WorkEmail, showPhones ? e.WorkPhone : null));
    }
}

/// <summary>Organization chart by manager hierarchy (roots = active employees without an active manager). Company-wide like the directory.</summary>
[RequiresPermission(EmployeePermissions.OrgChartViewKey)]
[RequiresAccessSetting(AccessSettingKeys.AccountShowOrgChart)]
public sealed record GetOrgChartQuery : IQuery<IReadOnlyList<OrgChartNodeDto>>;

public sealed class GetOrgChartHandler(IEmployeeReadStore store) : IQueryHandler<GetOrgChartQuery, IReadOnlyList<OrgChartNodeDto>>
{
    public async Task<Result<IReadOnlyList<OrgChartNodeDto>>> Handle(GetOrgChartQuery query, CancellationToken cancellationToken)
    {
        var rows = await store.ListAllAsync(new EmployeeCriteria(ActiveOnly: true), cancellationToken);
        return Result.Success(OrgChartBuilder.Build(rows));
    }
}

public static class OrgChartBuilder
{
    public static IReadOnlyList<OrgChartNodeDto> Build(IReadOnlyList<EmployeeRow> rows)
    {
        var ids = rows.Select(r => r.Id).ToHashSet();
        var ordered = rows.OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var children = ordered
            .Where(r => r.ManagerEmployeeId is { } m && ids.Contains(m))
            .ToLookup(r => r.ManagerEmployeeId!.Value);

        var visited = new HashSet<Guid>();
        var roots = new List<OrgChartNodeDto>();
        foreach (var root in ordered.Where(r => r.ManagerEmployeeId is not { } m || !ids.Contains(m)))
        {
            roots.Add(BuildNode(root, children, visited));
        }

        // Rows only reachable through a (corrupt) manager loop would otherwise disappear from the chart.
        foreach (var orphan in ordered.Where(r => !visited.Contains(r.Id)))
        {
            if (!visited.Contains(orphan.Id))
            {
                roots.Add(BuildNode(orphan, children, visited));
            }
        }

        return roots;
    }

    private static OrgChartNodeDto BuildNode(EmployeeRow row, ILookup<Guid, EmployeeRow> children, HashSet<Guid> visited)
    {
        visited.Add(row.Id);
        var childNodes = children[row.Id]
            .Where(c => !visited.Contains(c.Id))
            .Select(c => BuildNode(c, children, visited))
            .ToList();
        return new OrgChartNodeDto(row.Id, row.DisplayName, row.OrgUnitId, childNodes);
    }
}

/// <summary>Active employees whose next birthday falls within [today, today + days]. Company-wide like the directory.</summary>
[RequiresPermission(EmployeePermissions.DirectoryViewKey)]
[RequiresAccessSetting(AccessSettingKeys.HomeShowUpcomingBirthdays)]
public sealed record GetUpcomingBirthdaysQuery(int Days = GetUpcomingBirthdaysQuery.DefaultDays) : IQuery<IReadOnlyList<UpcomingBirthdayDto>>
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;
}

public sealed class GetUpcomingBirthdaysValidator : AbstractValidator<GetUpcomingBirthdaysQuery>
{
    public GetUpcomingBirthdaysValidator() =>
        RuleFor(x => x.Days).InclusiveBetween(0, GetUpcomingBirthdaysQuery.MaxDays).WithMessage(EmployeeErrors.DaysOutOfRange);
}

public sealed class GetUpcomingBirthdaysHandler(IEmployeeReadStore store, TimeProvider clock) : IQueryHandler<GetUpcomingBirthdaysQuery, IReadOnlyList<UpcomingBirthdayDto>>
{
    public async Task<Result<IReadOnlyList<UpcomingBirthdayDto>>> Handle(GetUpcomingBirthdaysQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rows = await store.ListAllAsync(new EmployeeCriteria(ActiveOnly: true), cancellationToken);
        return Result.Success(BirthdayCalculator.Upcoming(rows, today, query.Days));
    }
}

/// <summary>
/// Headcount of employees in <see cref="EmployeeStatus.Active"/> status (Draft and Suspended are not counted) per
/// Department-level org unit ("Çalışan Dağılımı" ana sayfa widget'ı). Company-wide
/// like the directory. Employee.OrgUnitId is always the deepest assignment (usually "Title"), so this walks each
/// employee's ancestor chain to find the Department-level unit, the same way the career screen splits Title/Department.
/// </summary>
[RequiresPermission(EmployeePermissions.DirectoryViewKey)]
public sealed record GetDepartmentDistributionQuery : IQuery<IReadOnlyList<DepartmentDistributionItemDto>>;

public sealed class GetDepartmentDistributionHandler(IEmployeeReadStore store, OrgPlacementLoader placementLoader)
    : IQueryHandler<GetDepartmentDistributionQuery, IReadOnlyList<DepartmentDistributionItemDto>>
{
    public async Task<Result<IReadOnlyList<DepartmentDistributionItemDto>>> Handle(GetDepartmentDistributionQuery query, CancellationToken cancellationToken)
    {
        var rows = await store.ListAllAsync(new EmployeeCriteria(Status: EmployeeStatus.Active), cancellationToken);
        var placement = await placementLoader.LoadAsync(cancellationToken);

        // "Department" = Organization.Domain.OrgUnitTypeCodes.Department (Şirket > Şube > Departman > Ünvan sabit sistem
        // kodu); Employee.Application, Modules_TalkOnlyThroughContracts kuralı gereği Organization.Domain'e değil bu
        // Contracts DTO'sundaki Code alanına bakıyor.
        var departmentLevel = placement.Types.FirstOrDefault(t => t.Code == "Department");

        var groups = rows
            .Select(r => departmentLevel is null ? null : placement.ChainFor(r.OrgUnitId).FirstOrDefault(a => a.OrgUnitTypeId == departmentLevel.Id))
            .GroupBy(a => a?.OrgUnitId)
            .Select(g => new DepartmentDistributionItemDto(g.Key, g.Key is { } id ? placement.UnitsById.GetValueOrDefault(id)?.Name : null, g.Count()))
            .OrderByDescending(d => d.Count)
            .ToList();

        return Result.Success<IReadOnlyList<DepartmentDistributionItemDto>>(groups);
    }
}

public static class BirthdayCalculator
{
    public static IReadOnlyList<UpcomingBirthdayDto> Upcoming(IEnumerable<EmployeeRow> rows, DateOnly today, int days)
    {
        var until = today.AddDays(days);
        return rows
            .Where(r => r.BirthDate is not null)
            .Select(r => (Row: r, Next: NextBirthday(r.BirthDate!.Value, today)))
            .Where(x => x.Next <= until)
            .OrderBy(x => x.Next)
            .ThenBy(x => x.Row.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new UpcomingBirthdayDto(x.Row.Id, x.Row.DisplayName, x.Row.OrgUnitId, x.Next))
            .ToList();
    }

    public static DateOnly NextBirthday(DateOnly birthDate, DateOnly today)
    {
        var candidate = InYear(birthDate, today.Year);
        return candidate < today ? InYear(birthDate, today.Year + 1) : candidate;
    }

    // 29 Şubat doğumlular artık olmayan yıllarda 28 Şubat'ta kutlanır.
    private static DateOnly InYear(DateOnly birthDate, int year) =>
        new(year, birthDate.Month, Math.Min(birthDate.Day, DateTime.DaysInMonth(year, birthDate.Month)));
}

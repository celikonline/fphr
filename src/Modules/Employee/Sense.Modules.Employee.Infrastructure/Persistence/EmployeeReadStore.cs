using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Infrastructure.Querying;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Infrastructure.Persistence;

/// <summary>
/// Read side: grid-standard (whitelisted sort/filter) EF queries over employees. Career records are auto-included on
/// the aggregate; list/grid queries opt out (IgnoreAutoIncludes) and only single-card reads load them.
/// </summary>
public sealed class EmployeeReadStore(EmployeeDbContext db, TimeProvider clock) : IEmployeeReadStore
{
    private const char LikeWildcard = '%';
    private const char LikeEscape = '\\';

    private static readonly GridFieldMap<EmployeeEntity> Fields = GridFieldMap<EmployeeEntity>.Create()
        .Field(EmployeeSortFields.DisplayName, e => e.DisplayName)
        .Field(EmployeeSortFields.FirstName, e => e.FirstName)
        .Field(EmployeeSortFields.LastName, e => e.LastName)
        .Field(EmployeeSortFields.HireDate, e => e.HireDate)
        .Field(EmployeeSortFields.Status, e => e.Status);

    public async Task<PagedResult<EmployeeRow>> ListAsync(EmployeeCriteria criteria, PagedQuery paging, CancellationToken ct)
    {
        var query = Apply(criteria)
            .ApplyFilters(paging.Filters, Fields)
            .ApplySort(paging.SortClauses, Fields, e => e.DisplayName, defaultDescending: false);

        var page = await query.ToPagedResultAsync(paging, ct).ConfigureAwait(false);
        return page.Map(e => EmployeeRow.From(e));
    }

    public async Task<IReadOnlyList<Guid>> ListIdsAsync(EmployeeCriteria criteria, CancellationToken ct) =>
        await Apply(criteria).Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<EmployeeRow>> ListAllAsync(EmployeeCriteria criteria, CancellationToken ct)
    {
        var list = await Apply(criteria).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(e => EmployeeRow.From(e)).ToList();
    }

    public async Task<EmployeeRow?> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Employees.AsNoTracking().AsSplitQuery().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : EmployeeRow.From(entity, Today);
    }

    public async Task<EmployeeRow?> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var entity = await db.Employees.AsNoTracking().AsSplitQuery().FirstOrDefaultAsync(e => e.UserId == userId, ct).ConfigureAwait(false);
        return entity is null ? null : EmployeeRow.From(entity, Today);
    }

    public async Task<EmployeeEntity?> GetWithCareerAsync(Guid id, CancellationToken ct) =>
        await db.Employees.AsNoTracking().AsSplitQuery().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

    public async Task<EmployeeEntity?> GetWithPaymentsAsync(Guid id, CancellationToken ct) =>
        await db.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Include(e => e.SalaryRecords)
            .Include(e => e.BankAccounts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

    public async Task<EmployeeEntity?> GetWithEmploymentPeriodsAsync(Guid id, CancellationToken ct) =>
        await db.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Include(e => e.EmploymentPeriods)
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, string?>> GetCustomFieldValuesAsync(Guid employeeId, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Include(e => e.CustomFieldValues)
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct).ConfigureAwait(false);
        return employee is null
            ? new Dictionary<Guid, string?>()
            : employee.CustomFieldValues.ToDictionary(v => v.DefinitionId, v => v.Value);
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var list = ids.ToList();
        return await db.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Where(e => list.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct).ConfigureAwait(false);
    }

    private IQueryable<EmployeeEntity> Apply(EmployeeCriteria c)
    {
        var query = db.Employees.AsNoTracking().IgnoreAutoIncludes();

        if (c.Ids is not null)
        {
            var ids = c.Ids.ToList();
            query = query.Where(e => ids.Contains(e.Id));
        }

        if (c.VisibleIds is not null)
        {
            var visible = c.VisibleIds.ToList();
            query = query.Where(e => visible.Contains(e.Id));
        }

        if (c.OrgUnitIds is not null)
        {
            var units = c.OrgUnitIds.ToList();
            query = query.Where(e => e.OrgUnitId != null && units.Contains(e.OrgUnitId.Value));
        }

        if (c.Status is { } status)
        {
            query = query.Where(e => e.Status == status);
        }

        if (c.ActiveOnly)
        {
            query = query.Where(e => e.Status != EmployeeStatus.Terminated);
        }

        if (c.HasUserAccount is { } hasUser)
        {
            query = hasUser ? query.Where(e => e.UserId != null) : query.Where(e => e.UserId == null);
        }

        if (c.ManagerEmployeeId is { } managerId)
        {
            query = query.Where(e => e.ManagerEmployeeId == managerId);
        }

        if (!string.IsNullOrWhiteSpace(c.Q))
        {
            var pattern = ContainsPattern(c.Q);
            var searchNationalId = c.SearchNationalId;
            query = query.Where(e =>
                EF.Functions.ILike(e.DisplayName, pattern, LikeEscape.ToString()) ||
                EF.Functions.ILike(e.FirstName, pattern, LikeEscape.ToString()) ||
                EF.Functions.ILike(e.LastName, pattern, LikeEscape.ToString()) ||
                (e.WorkEmail != null && EF.Functions.ILike(e.WorkEmail, pattern, LikeEscape.ToString())) ||
                (e.PersonalEmail != null && EF.Functions.ILike(e.PersonalEmail, pattern, LikeEscape.ToString())) ||
                (searchNationalId && e.NationalId != null && EF.Functions.ILike(e.NationalId, pattern, LikeEscape.ToString())));
        }

        return query;
    }

    private static string ContainsPattern(string term)
    {
        var escaped = term.Trim()
            .Replace(LikeEscape.ToString(), string.Concat(LikeEscape, LikeEscape), StringComparison.Ordinal)
            .Replace(LikeWildcard.ToString(), string.Concat(LikeEscape, LikeWildcard), StringComparison.Ordinal)
            .Replace("_", string.Concat(LikeEscape, "_"), StringComparison.Ordinal);
        return string.Concat(LikeWildcard, escaped, LikeWildcard);
    }
}

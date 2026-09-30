using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Paging;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>EmployeeFilter/EmployeeSelection resolution against an in-memory store that applies the criteria fields the resolver sets.</summary>
public sealed class EmployeeSelectionResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid UnitA = Guid.NewGuid();
    private static readonly Guid UnitAChild = Guid.NewGuid();
    private static readonly Guid UnitB = Guid.NewGuid();

    private readonly Guid _inA = Guid.NewGuid();
    private readonly Guid _inChild = Guid.NewGuid();
    private readonly Guid _inB = Guid.NewGuid();
    private readonly Guid _terminatedInA = Guid.NewGuid();
    private readonly FakeStore _store;
    private readonly EmployeeSelectionResolver _resolver;

    public EmployeeSelectionResolverTests()
    {
        _store = new FakeStore(
            (_inA, UnitA, EmployeeStatus.Active),
            (_inChild, UnitAChild, EmployeeStatus.Active),
            (_inB, UnitB, EmployeeStatus.Active),
            (_terminatedInA, UnitA, EmployeeStatus.Terminated));
        _resolver = new EmployeeSelectionResolver(_store, new FakeUnits(), new FakeDataScope());
    }

    private static EmployeeFilter Filter(
        Guid? orgUnitId = null, bool includeSubUnits = false, string? status = null,
        IReadOnlyList<Guid>? orgUnitIds = null, bool includeTerminated = false) =>
        new(null, orgUnitId, includeSubUnits, status, null, null, OrgUnitIds: orgUnitIds, IncludeTerminated: includeTerminated);

    private Task<IReadOnlyList<Guid>> ResolveAsync(EmployeeSelection selection) =>
        _resolver.ResolveAsync(Tenant, selection, "employee.employees.read", CancellationToken.None);

    [Fact]
    public async Task OrgUnitIds_union_with_subunits()
    {
        var ids = await ResolveAsync(new EmployeeSelection(null, Filter(orgUnitIds: [UnitA, UnitB], includeSubUnits: true), null));

        ids.ShouldBe([_inA, _inChild, _inB], ignoreOrder: true);
    }

    [Fact]
    public async Task OrgUnitIds_union_with_single_OrgUnitId_without_subunits()
    {
        var ids = await ResolveAsync(new EmployeeSelection(null, Filter(orgUnitId: UnitA, orgUnitIds: [UnitB]), null));

        ids.ShouldBe([_inA, _inB], ignoreOrder: true);
    }

    [Fact]
    public async Task Terminated_excluded_by_default()
    {
        var byDefault = await ResolveAsync(new EmployeeSelection(null, Filter(orgUnitId: UnitA), null));
        var included = await ResolveAsync(new EmployeeSelection(null, Filter(orgUnitId: UnitA, includeTerminated: true), null));
        var explicitStatus = await ResolveAsync(new EmployeeSelection(null, Filter(orgUnitId: UnitA, status: "Terminated"), null));

        byDefault.ShouldBe([_inA]);
        included.ShouldBe([_inA, _terminatedInA], ignoreOrder: true);
        explicitStatus.ShouldBe([_terminatedInA]);
    }

    [Fact]
    public async Task Select_all_with_exclusions()
    {
        var ids = await ResolveAsync(new EmployeeSelection(null, Filter(), [_inB]));

        ids.ShouldBe([_inA, _inChild], ignoreOrder: true);
    }

    [Fact]
    public async Task Empty_filter_result_is_empty_not_all()
    {
        var noMatch = await ResolveAsync(new EmployeeSelection(null, Filter(orgUnitId: Guid.NewGuid()), null));
        var emptyIds = await ResolveAsync(new EmployeeSelection([], Filter(), null));
        var nothing = await ResolveAsync(new EmployeeSelection(null, null, null));
        var allExcluded = await ResolveAsync(new EmployeeSelection(null, Filter(orgUnitId: UnitB), [_inB]));

        noMatch.ShouldBeEmpty();
        emptyIds.ShouldBeEmpty();
        nothing.ShouldBeEmpty();
        allExcluded.ShouldBeEmpty();
    }

    [Fact]
    public async Task Existing_filter_shape_without_new_fields_still_resolves()
    {
        var filter = new EmployeeFilter(null, UnitA, true, null, null, null);

        var ids = await ResolveAsync(new EmployeeSelection(null, filter, null));

        ids.ShouldBe([_inA, _inChild], ignoreOrder: true);
    }

    private sealed class FakeStore(params (Guid Id, Guid Unit, EmployeeStatus Status)[] rows) : IEmployeeReadStore
    {
        public Task<IReadOnlyList<Guid>> ListIdsAsync(EmployeeCriteria c, CancellationToken ct)
        {
            IEnumerable<(Guid Id, Guid Unit, EmployeeStatus Status)> q = rows;
            if (c.Ids is not null)
            {
                q = q.Where(r => c.Ids.Contains(r.Id));
            }

            if (c.OrgUnitIds is not null)
            {
                q = q.Where(r => c.OrgUnitIds.Contains(r.Unit));
            }

            if (c.Status is { } status)
            {
                q = q.Where(r => r.Status == status);
            }

            if (c.ActiveOnly)
            {
                q = q.Where(r => r.Status != EmployeeStatus.Terminated);
            }

            return Task.FromResult<IReadOnlyList<Guid>>(q.Select(r => r.Id).ToList());
        }

        public Task<PagedResult<EmployeeRow>> ListAsync(EmployeeCriteria criteria, PagedQuery paging, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<EmployeeRow>> ListAllAsync(EmployeeCriteria criteria, CancellationToken ct) => throw new NotSupportedException();
        public Task<EmployeeRow?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<EmployeeRow?> GetByUserIdAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Domain.Employees.Employee?> GetWithCareerAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Domain.Employees.Employee?> GetWithPaymentsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Domain.Employees.Employee?> GetWithEmploymentPeriodsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Domain.Employees.Employee?> GetWithUnionAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string?>> GetCustomFieldValuesAsync(Guid employeeId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUnits : IOrgUnitLookup
    {
        public Task<IReadOnlyList<Guid>> GetSelfAndDescendantIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken ct = default)
        {
            var result = orgUnitIds.ToList();
            if (orgUnitIds.Contains(UnitA))
            {
                result.Add(UnitAChild);
            }

            return Task.FromResult<IReadOnlyList<Guid>>(result.Distinct().ToList());
        }

        public Task<bool> ExistsActiveAsync(Guid tenantId, Guid orgUnitId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrgUnitTypeInfo>> ListActiveTypesAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrgUnitInfo>> ListActiveUnitsAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Guid?> GetEffectiveLocationIdAsync(Guid tenantId, Guid orgUnitId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, Guid>> GetEffectiveLocationMapAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

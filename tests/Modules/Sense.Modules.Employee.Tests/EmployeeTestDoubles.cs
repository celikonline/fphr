using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Tests;

/// <summary>Identity is not loaded in these tests: every permission is granted, scope is whatever the test sets.</summary>
internal sealed class GrantAllPermissions : IPermissionService
{
    public Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    public Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(DataScope.Company);
}

internal sealed class FakeDataScope : IDataScopeService
{
    /// <summary>null = Company scope.</summary>
    public IReadOnlySet<Guid>? Visible { get; set; }

    public Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(Visible);

    public Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visible is null || Visible.Contains(employeeId));

    /// <summary>null = unrestricted.</summary>
    public IReadOnlySet<Guid>? AccessibleOrgUnits { get; set; }

    public Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(AccessibleOrgUnits);
}

internal sealed class AllAccessSettingsEnabled : IAccessSettingsService
{
    public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

internal sealed class FakeUserAccountLookup : IUserAccountLookup
{
    public Dictionary<Guid, UserAccountInfo> Accounts { get; } = [];

    public Task<IReadOnlyDictionary<Guid, UserAccountInfo>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, UserAccountInfo>>(
            userIds.Where(Accounts.ContainsKey).ToDictionary(id => id, id => Accounts[id]));
}

internal static class Tckn
{
    private static int _seed = 100_000_000;

    /// <summary>Unique checksum-valid TCKN per call.</summary>
    public static string Next()
    {
        var d = Interlocked.Increment(ref _seed).ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => c - '0').ToList();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        d.Add((((odd * 7) - even) % 10 + 10) % 10);
        d.Add(d.Sum() % 10);
        return string.Concat(d);
    }
}

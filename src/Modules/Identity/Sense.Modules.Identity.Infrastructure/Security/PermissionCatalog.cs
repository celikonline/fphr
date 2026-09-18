using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.UnitAuthorizations;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Caching;
using Sense.Shared.Infrastructure.Messaging.Behaviours;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Tüm modüllerin izin katalogları (IModule.Permissions) birleşimi.</summary>
public sealed class PermissionCatalog : IPermissionCatalog
{
    private readonly Lazy<IReadOnlyList<Permission>> _all;
    private readonly Lazy<HashSet<string>> _keys;

    public PermissionCatalog(IReadOnlyList<IModule> modules)
    {
        _all = new Lazy<IReadOnlyList<Permission>>(() =>
            modules.SelectMany(m => m.Permissions)
                .GroupBy(p => p.Key, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToList());
        _keys = new Lazy<HashSet<string>>(() => _all.Value.Select(p => p.Key).ToHashSet(StringComparer.Ordinal));
    }

    public IReadOnlyList<Permission> All => _all.Value;

    public bool Exists(string permission) => _keys.Value.Contains(permission);
}

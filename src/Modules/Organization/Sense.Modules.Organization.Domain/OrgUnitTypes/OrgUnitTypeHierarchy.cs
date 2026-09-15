using System.Globalization;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.OrgUnitTypes;

/// <summary>
/// Level-set rules over a tenant's full OrgUnitType list (11.3.02-sirket-yapisi.md): active levels are numbered
/// contiguously 1..n with the Company level first, inactive levels follow (n+1..m) in their previous order,
/// "+" inserts a level right after another active one, and the total row count is capped.
/// </summary>
public sealed class OrgUnitTypeHierarchy
{
    public const string CustomCodePrefix = "Custom_";

    private readonly List<OrgUnitType> _types;
    private readonly int _maxLevels;

    public OrgUnitTypeHierarchy(IEnumerable<OrgUnitType> types, int maxLevels = OrganizationLimits.MaxUnitTypesDefault)
    {
        _types = [.. types];
        _maxLevels = maxLevels;
    }

    /// <summary>All levels, active first, each group by SortOrder.</summary>
    public IReadOnlyList<OrgUnitType> Types => [.. Ordered(_types.Where(t => t.IsActive)), .. Ordered(_types.Where(t => !t.IsActive))];

    public IReadOnlyList<OrgUnitType> Active => Ordered(_types.Where(t => t.IsActive));

    /// <summary>Adds a tenant-custom level right after <paramref name="insertAfterTypeId"/> (null = at the end) and renumbers.</summary>
    public Result<OrgUnitType> AddCustom(Guid tenantId, string name, Guid? insertAfterTypeId)
    {
        var validName = OrgUnitType.ValidateName(name);
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        if (_types.Count >= _maxLevels)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitTypeLimitReached, ("max", _maxLevels));
        }

        var active = Active.ToList();
        var position = InsertPosition(active, insertAfterTypeId);
        if (position.IsFailure)
        {
            return position.Error;
        }

        var created = OrgUnitType.Create(tenantId, NextCustomCode(), validName.Value, sortOrder: 0, isSystem: false);
        if (created.IsFailure)
        {
            return created.Error;
        }

        active.Insert(position.Value, created.Value);
        _types.Add(created.Value);
        Renumber(active);
        return created.Value;
    }

    /// <summary>Re-activates an inactive level right after <paramref name="insertAfterTypeId"/> (null = at the end) and renumbers.</summary>
    public Result<OrgUnitType> Activate(Guid typeId, Guid? insertAfterTypeId)
    {
        var type = Find(typeId);
        if (type is null)
        {
            return Error.NotFound(OrganizationDomainErrors.OrgUnitTypeNotFound);
        }

        if (type.IsActive)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitTypeAlreadyActive);
        }

        var active = Active.ToList();
        var position = InsertPosition(active, insertAfterTypeId);
        if (position.IsFailure)
        {
            return position.Error;
        }

        type.Activate();
        active.Insert(position.Value, type);
        Renumber(active);
        return type;
    }

    /// <summary>Applies a new order to the active levels; the ids must be exactly the active set and Company must stay first.</summary>
    public Result Reorder(IReadOnlyList<Guid> orderedTypeIds)
    {
        var active = Active;
        if (orderedTypeIds.Count != active.Count || orderedTypeIds.Distinct().Count() != active.Count ||
            !orderedTypeIds.All(id => active.Any(t => t.Id == id)))
        {
            return Error.Validation(OrganizationDomainErrors.OrgUnitTypeOrderInvalid);
        }

        var reordered = orderedTypeIds.Select(id => active.First(t => t.Id == id)).ToList();
        if (active.Any(t => t.IsCompanyLevel) && !reordered[0].IsCompanyLevel)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitTypeCompanyMustBeFirst);
        }

        Renumber(reordered);
        return Result.Success();
    }

    /// <summary>Deactivates a level (never Company) and closes the gap in the active numbering.</summary>
    public Result Remove(Guid typeId)
    {
        var type = Find(typeId);
        if (type is null)
        {
            return Error.NotFound(OrganizationDomainErrors.OrgUnitTypeNotFound);
        }

        var active = Active.ToList();
        var deactivated = type.Deactivate();
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        active.Remove(type);
        Renumber(active);
        return Result.Success();
    }

    private OrgUnitType? Find(Guid typeId) => _types.FirstOrDefault(t => t.Id == typeId);

    private static List<OrgUnitType> Ordered(IEnumerable<OrgUnitType> types) =>
        [.. types.OrderBy(t => t.SortOrder).ThenBy(t => t.Code, StringComparer.Ordinal)];

    private static Result<int> InsertPosition(List<OrgUnitType> active, Guid? insertAfterTypeId)
    {
        if (insertAfterTypeId is not { } afterId)
        {
            return active.Count;
        }

        var index = active.FindIndex(t => t.Id == afterId);
        return index < 0 ? Error.Validation(OrganizationDomainErrors.OrgUnitTypeInsertPositionInvalid) : index + 1;
    }

    private void Renumber(List<OrgUnitType> activeInOrder)
    {
        var inactive = Ordered(_types.Where(t => !t.IsActive));
        var sortOrder = 1;
        foreach (var type in activeInOrder.Concat(inactive))
        {
            type.Reorder(sortOrder++);
        }
    }

    private string NextCustomCode()
    {
        var max = _types
            .Select(t => t.Code)
            .Where(c => c.StartsWith(CustomCodePrefix, StringComparison.Ordinal))
            .Select(c => int.TryParse(c.AsSpan(CustomCodePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return CustomCodePrefix + (max + 1).ToString(CultureInfo.InvariantCulture);
    }
}

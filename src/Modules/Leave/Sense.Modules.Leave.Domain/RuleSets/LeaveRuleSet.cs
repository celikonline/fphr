using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.RuleSets;

/// <summary>
/// Kural seti = the leave type package assigned to an employee ("Örnek Türk İş Kanunu kuralı"). One default set per
/// tenant (new employees get it). Types are child entities; <see cref="Clone"/> deep-copies types and tiers.
/// </summary>
public sealed class LeaveRuleSet : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<LeaveType> _types = [];

    private LeaveRuleSet()
    {
    }

    private LeaveRuleSet(Guid id, Guid tenantId, string name, bool isDefault, bool isSystemSeed, bool unpaidLeaveShiftsAccrual)
        : base(id, tenantId)
    {
        Name = name;
        IsDefault = isDefault;
        IsSystemSeed = isSystemSeed;
        UnpaidLeaveShiftsAccrual = unpaidLeaveShiftsAccrual;
    }

    public string Name { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public bool IsSystemSeed { get; private set; }

    /// <summary>Periods not counted as service (unpaid leave etc.) shift the next accrual date.</summary>
    public bool UnpaidLeaveShiftsAccrual { get; private set; }

    public IReadOnlyList<LeaveType> Types => _types.AsReadOnly();

    public static Result<LeaveRuleSet> Create(
        Guid tenantId, string name, bool isDefault = false, bool isSystemSeed = false, bool unpaidLeaveShiftsAccrual = true)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > LeaveLimits.NameMaxLength)
        {
            return Error.Validation(LeaveDomainErrors.RuleSetNameRequired);
        }

        return new LeaveRuleSet(Guid.CreateVersion7(), tenantId, name.Trim(), isDefault, isSystemSeed, unpaidLeaveShiftsAccrual);
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > LeaveLimits.NameMaxLength)
        {
            return Error.Validation(LeaveDomainErrors.RuleSetNameRequired);
        }

        Name = name.Trim();
        return Result.Success();
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>"Varsayılan yap": this set becomes the only default among <paramref name="tenantSets"/> (which should include it).</summary>
    public void MakeDefault(IEnumerable<LeaveRuleSet> tenantSets)
    {
        foreach (var other in tenantSets.Where(s => s.Id != Id))
        {
            other.SetDefault(false);
        }

        IsDefault = true;
    }

    /// <summary>Delete guard: the default set and sets still assigned to employees cannot be deleted.</summary>
    public Result EnsureDeletable(int assignedEmployeeCount)
    {
        if (IsDefault)
        {
            return Error.Conflict(LeaveDomainErrors.RuleSetIsDefault);
        }

        return assignedEmployeeCount > 0 ? Error.Conflict(LeaveDomainErrors.RuleSetHasEmployees) : Result.Success();
    }

    /// <summary>The type of this set that corresponds to <paramref name="type"/> of another set: same Code, or for Custom types the same name.</summary>
    public LeaveType? FindCounterpart(LeaveType type) =>
        type.Code == LeaveTypeCode.Custom
            ? _types.FirstOrDefault(t => t.Code == LeaveTypeCode.Custom && string.Equals(t.Name, type.Name, StringComparison.OrdinalIgnoreCase))
            : _types.FirstOrDefault(t => t.Code == type.Code);

    public void SetUnpaidLeaveShiftsAccrual(bool value) => UnpaidLeaveShiftsAccrual = value;

    public Result<LeaveType> AddType(LeaveTypeSettings settings)
    {
        var valid = settings.Validate();
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        if (settings.Code != LeaveTypeCode.Custom && _types.Any(t => t.Code == settings.Code))
        {
            return Error.Conflict(LeaveDomainErrors.TypeCodeDuplicate);
        }

        var type = new LeaveType(Guid.CreateVersion7(), TenantId, Id, _types.Count + 1, settings);
        _types.Add(type);
        return type;
    }

    public Result UpdateType(Guid typeId, LeaveTypeSettings settings)
    {
        var type = _types.FirstOrDefault(t => t.Id == typeId);
        if (type is null)
        {
            return Error.NotFound(LeaveDomainErrors.TypeNotFound);
        }

        var valid = settings.Validate();
        if (valid.IsFailure)
        {
            return valid;
        }

        if (settings.Code != LeaveTypeCode.Custom && _types.Any(t => t.Id != typeId && t.Code == settings.Code))
        {
            return Error.Conflict(LeaveDomainErrors.TypeCodeDuplicate);
        }

        type.Update(settings);
        return Result.Success();
    }

    public Result SetTypeDisabled(Guid typeId, bool disabled)
    {
        var type = _types.FirstOrDefault(t => t.Id == typeId);
        if (type is null)
        {
            return Error.NotFound(LeaveDomainErrors.TypeNotFound);
        }

        type.SetDisabled(disabled);
        return Result.Success();
    }

    /// <summary>"Kopyala ve düzenle": a non-default, non-seed copy with every type (and its tiers) deep-copied.</summary>
    public Result<LeaveRuleSet> Clone(string newName)
    {
        var created = Create(TenantId, newName, isDefault: false, isSystemSeed: false, UnpaidLeaveShiftsAccrual);
        if (created.IsFailure)
        {
            return created;
        }

        foreach (var type in _types.OrderBy(t => t.SortOrder))
        {
            var added = created.Value.AddType(type.ToSettings());
            if (added.IsFailure)
            {
                return added.Error;
            }
        }

        return created;
    }
}

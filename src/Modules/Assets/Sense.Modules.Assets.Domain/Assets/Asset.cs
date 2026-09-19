using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Domain.Assets;

public enum AssetStatus
{
    Unassigned = 0,
    Assigned = 1,
    Retired = 2,
}

/// <summary>
/// Company equipment ("zimmet"): a category, serial number, description, and a single current-assignment slot
/// (no assignment history/ledger - matches the roadmap's simple "zimmet şablonu" scope, item #65). An employee
/// "currently has an open (unreturned) asset" is exactly Status == Assigned.
/// </summary>
public sealed class Asset : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Asset()
    {
    }

    private Asset(Guid id, Guid tenantId, Guid categoryId, string name, string? serialNumber, string? description)
        : base(id, tenantId)
    {
        CategoryId = categoryId;
        Name = name;
        SerialNumber = serialNumber;
        Description = description;
        Status = AssetStatus.Unassigned;
    }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? SerialNumber { get; private set; }

    public string? Description { get; private set; }

    public AssetStatus Status { get; private set; }

    public Guid? AssignedEmployeeId { get; private set; }

    public DateOnly? IssuedAt { get; private set; }

    /// <summary>Date of the most recent return; kept for display after Return, cleared again once reassigned.</summary>
    public DateOnly? ReturnedAt { get; private set; }

    public static Result<Asset> Create(Guid tenantId, Guid categoryId, string name, string? serialNumber, string? description)
    {
        var validated = ValidateName(name);
        if (validated is not null)
        {
            return validated;
        }

        return new Asset(
            Guid.CreateVersion7(),
            tenantId,
            categoryId,
            name.Trim(),
            Trim(serialNumber, AssetsLimits.SerialNumberMaxLength),
            Trim(description, AssetsLimits.DescriptionMaxLength));
    }

    public Result Update(string name, string? serialNumber, string? description)
    {
        var validated = ValidateName(name);
        if (validated is not null)
        {
            return validated;
        }

        Name = Guard.MaxLength(name.Trim(), AssetsLimits.NameMaxLength);
        SerialNumber = Trim(serialNumber, AssetsLimits.SerialNumberMaxLength);
        Description = Trim(description, AssetsLimits.DescriptionMaxLength);
        return Result.Success();
    }

    /// <summary>Unassigned/Retired -> Assigned. Must Return first if currently Assigned.</summary>
    public Result Assign(Guid employeeId, DateOnly issuedAt)
    {
        if (Status != AssetStatus.Unassigned)
        {
            return Error.Rule(AssetsDomainErrors.AlreadyAssigned);
        }

        Status = AssetStatus.Assigned;
        AssignedEmployeeId = employeeId;
        IssuedAt = issuedAt;
        ReturnedAt = null;
        return Result.Success();
    }

    /// <summary>Assigned -> Unassigned; clears the current assignment, keeps ReturnedAt as the "last returned" date for display.</summary>
    public Result Return(DateOnly returnedAt)
    {
        if (Status != AssetStatus.Assigned)
        {
            return Error.Rule(AssetsDomainErrors.NotAssigned);
        }

        if (IssuedAt is { } issuedAt && returnedAt < issuedAt)
        {
            return Error.Validation(AssetsDomainErrors.ReturnBeforeIssue);
        }

        Status = AssetStatus.Unassigned;
        AssignedEmployeeId = null;
        IssuedAt = null;
        ReturnedAt = returnedAt;
        return Result.Success();
    }

    /// <summary>Unassigned -> Retired. Must Return first if currently Assigned.</summary>
    public Result Retire()
    {
        if (Status == AssetStatus.Assigned)
        {
            return Error.Rule(AssetsDomainErrors.CannotRetireWhileAssigned);
        }

        Status = AssetStatus.Retired;
        return Result.Success();
    }

    private static Error? ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name) ? Error.Validation(AssetsDomainErrors.NameRequired) : null;

    private static string? Trim(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}

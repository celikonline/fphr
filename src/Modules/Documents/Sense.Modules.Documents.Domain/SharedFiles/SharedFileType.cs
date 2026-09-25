using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain.SharedFiles;

/// <summary>
/// Tenant-defined category for shared company files (E8: e.g. "Yönetmelik", "Form"). Carries the "mobilde görünsün"
/// default that files of this type inherit when they are uploaded.
/// </summary>
public sealed class SharedFileType : TenantAggregateRoot<Guid>, IAuditLogged
{
    public const int NameMaxLength = 100;

    private SharedFileType()
    {
    }

    private SharedFileType(Guid id, Guid tenantId, string name, bool visibleOnMobile, int sortOrder) : base(id, tenantId)
    {
        Name = name;
        VisibleOnMobile = visibleOnMobile;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Default for files of this type: show them in the mobile app's shared-file list.</summary>
    public bool VisibleOnMobile { get; private set; }

    public bool IsActive { get; private set; }

    public int SortOrder { get; private set; }

    public static Result<SharedFileType> Create(Guid tenantId, string name, bool visibleOnMobile, int sortOrder)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Error.Validation(SharedFilesDomainErrors.TypeNameRequired);
        }

        if (trimmed.Length > NameMaxLength)
        {
            return Error.Validation(SharedFilesDomainErrors.TypeNameTooLong);
        }

        return new SharedFileType(Guid.CreateVersion7(), tenantId, trimmed, visibleOnMobile, sortOrder);
    }

    public Result Update(string name, bool visibleOnMobile, int sortOrder)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Error.Validation(SharedFilesDomainErrors.TypeNameRequired);
        }

        if (trimmed.Length > NameMaxLength)
        {
            return Error.Validation(SharedFilesDomainErrors.TypeNameTooLong);
        }

        Name = trimmed;
        VisibleOnMobile = visibleOnMobile;
        SortOrder = sortOrder;
        return Result.Success();
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}

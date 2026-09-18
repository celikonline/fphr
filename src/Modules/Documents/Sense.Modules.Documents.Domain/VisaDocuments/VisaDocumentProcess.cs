using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain.VisaDocuments;

/// <summary>
/// VisaDocumentProcess aggregate root: represents a predefined visa process for a country/visa type.
/// Example: "Schengen Business Visa", "Turkish Work Permit", etc.
/// Tenant isolation: each process belongs to exactly one tenant.
/// </summary>
public sealed class VisaDocumentProcess : TenantAggregateRoot<Guid>, IAuditLogged
{
    private VisaDocumentProcess()
    {
    }

    private VisaDocumentProcess(
        Guid id,
        Guid tenantId,
        string name,
        string? countryCode,
        bool isActive) : base(id, tenantId)
    {
        Name = name;
        CountryCode = countryCode;
        IsActive = isActive;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Name of the visa process (e.g., "Schengen Business", "Turkish Work Permit").</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional country code this process is associated with (e.g., "TR", "DE", "CH").</summary>
    public string? CountryCode { get; private set; }

    /// <summary>Whether this process is active and available for new requests.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Timestamp of process creation.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Timestamp of last modification, if any.</summary>
    public DateTime? ModifiedAt { get; private set; }

    /// <summary>
    /// Factory method to create a new visa document process.
    /// </summary>
    public static Result<VisaDocumentProcess> Create(
        Guid tenantId,
        string name,
        string? countryCode = null)
    {
        // Validate name
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<VisaDocumentProcess>(
                new Error(VisaDomainErrors.VisaDocumentProcessNameRequired, "Process name is required"));
        }

        if (name.Length > VisaLimits.ProcessNameMaxLength)
        {
            return Result.Failure<VisaDocumentProcess>(
                new Error(VisaDomainErrors.VisaDocumentProcessNameTooLong, $"Process name cannot exceed {VisaLimits.ProcessNameMaxLength} characters"));
        }

        var process = new VisaDocumentProcess(
            id: Guid.NewGuid(),
            tenantId: tenantId,
            name: name,
            countryCode: countryCode,
            isActive: true);

        return Result.Success(process);
    }

    /// <summary>
    /// Deactivate the process. New requests cannot be created for inactive processes.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reactivate the process.
    /// </summary>
    public void Reactivate()
    {
        IsActive = true;
        ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Update the process name and country code.
    /// </summary>
    public Result Update(string name, string? countryCode = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(
                new Error(VisaDomainErrors.VisaDocumentProcessNameRequired, "Process name is required"));
        }

        if (name.Length > VisaLimits.ProcessNameMaxLength)
        {
            return Result.Failure(
                new Error(VisaDomainErrors.VisaDocumentProcessNameTooLong, $"Process name cannot exceed {VisaLimits.ProcessNameMaxLength} characters"));
        }

        Name = name;
        CountryCode = countryCode;
        ModifiedAt = DateTime.UtcNow;

        return Result.Success();
    }
}

using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Profile photo of an employee (at most one per employee: the aggregate id IS the employee id). Stored as a bytea row
/// in the Employee schema - the photo is small (<see cref="EmployeeLimits.PhotoMaxBytes"/>) and must be served through the
/// API so tenant isolation and the employee data scope apply; the bytes are deliberately not audit-logged.
/// </summary>
public sealed class EmployeePhoto : TenantAggregateRoot<Guid>
{
    private EmployeePhoto()
    {
    }

    private EmployeePhoto(Guid employeeId, Guid tenantId, string contentType, byte[] content)
        : base(employeeId, tenantId)
    {
        ContentType = contentType;
        Content = content;
    }

    /// <summary>Sniffed (not client-declared) raster image type: image/jpeg, image/png or image/webp.</summary>
    public string ContentType { get; private set; } = string.Empty;

    public byte[] Content { get; private set; } = [];

    public static EmployeePhoto Create(Guid tenantId, Guid employeeId, string contentType, byte[] content) =>
        new(employeeId, tenantId, contentType, content);

    public void Replace(string contentType, byte[] content)
    {
        ContentType = contentType;
        Content = content;
    }
}

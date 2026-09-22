using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Employee.Contracts;

/// <summary>
/// Çalışma belgesi (4857 s. İş Kanunu md.28): the employer must give a leaving employee a document stating the kind of
/// work and its start and end dates. Rendered on demand as PDF from the current employee record, so Notifications can
/// attach it to the exit e-mail and HR can download it from the employee card. Implemented by Employee.Infrastructure.
/// </summary>
public interface IWorkCertificateProvider
{
    /// <summary>The certificate PDF, or null when the employee does not exist for the tenant or was never terminated.</summary>
    Task<ExportFileResult?> RenderPdfAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}

/// <summary>System-level audience enumeration for company-wide notifications (announcements), no permission scoping.</summary>
public interface IEmployeeAudienceLookup
{
    /// <summary>Every employee id of the tenant that is neither Terminated nor Draft (Active and Suspended).</summary>
    Task<IReadOnlyList<Guid>> ListNotifiableEmployeeIdsAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

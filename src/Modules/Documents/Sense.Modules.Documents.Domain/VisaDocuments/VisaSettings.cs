using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Documents.Domain.VisaDocuments;

/// <summary>
/// Per-tenant visa feature configuration: one row per tenant, lazily seeded on first read (mirrors Attendance's
/// <c>ShiftSettings</c>). Holds the two things <see cref="VisaDocumentRequest"/> needs and that don't belong on the
/// request itself - who approves a submitted request, and which template documents get delivered into the
/// employee's folder once one is approved. There is no generic cross-module "TenantSettings" key/value store in
/// this codebase today (only Identity's boolean-only <c>IAccessSettingsService</c>), so - like ShiftSettings - this
/// is a small dedicated aggregate owned by the Documents schema rather than a config blob borrowed from another
/// module.
/// </summary>
public sealed class VisaSettings : TenantAggregateRoot<Guid>
{
    private readonly List<Guid> _templateDocumentIds = [];

    private VisaSettings()
    {
    }

    private VisaSettings(Guid id, Guid tenantId) : base(id, tenantId)
    {
    }

    /// <summary>Employee who approves visa document requests for this tenant. Null means "not configured yet" -
    /// <see cref="Application.VisaDocuments.SubmitVisaDocumentRequestHandler"/> treats that as a hard failure.</summary>
    public Guid? ApproverEmployeeId { get; private set; }

    /// <summary>Document rows to copy into the employee's folder once a request is approved. Empty is valid
    /// (nothing configured yet, or a tenant that doesn't want anything auto-delivered).</summary>
    public IReadOnlyList<Guid> TemplateDocumentIds => _templateDocumentIds;

    public static VisaSettings CreateDefault(Guid tenantId) => new(Guid.CreateVersion7(), tenantId);

    /// <summary><see cref="Guid.Empty"/> is normalized to null - both mean "not configured".</summary>
    public void SetApprover(Guid? approverEmployeeId) =>
        ApproverEmployeeId = approverEmployeeId is null || approverEmployeeId == Guid.Empty ? null : approverEmployeeId;

    /// <summary>Replaces the configured template set. Empty/default ids are dropped and duplicates are collapsed;
    /// order is not meaningful so no ordering guarantee is made.</summary>
    public void SetTemplateDocumentIds(IReadOnlyList<Guid> templateDocumentIds)
    {
        _templateDocumentIds.Clear();
        _templateDocumentIds.AddRange(templateDocumentIds.Where(id => id != Guid.Empty).Distinct());
    }
}

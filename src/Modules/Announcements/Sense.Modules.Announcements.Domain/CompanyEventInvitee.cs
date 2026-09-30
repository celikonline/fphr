using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Announcements.Domain;

/// <summary>An employee invited to a <see cref="CompanyEvent"/> (E4; picked with the E1 employee picker).</summary>
public sealed class CompanyEventInvitee : TenantEntity<Guid>
{
    // ReSharper disable once UnusedMember.Local
    private CompanyEventInvitee() { }

    public Guid CompanyEventId { get; private set; }
    public Guid EmployeeId { get; private set; }

    internal static CompanyEventInvitee Create(Guid tenantId, Guid companyEventId, Guid employeeId) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, CompanyEventId = companyEventId, EmployeeId = employeeId };
}

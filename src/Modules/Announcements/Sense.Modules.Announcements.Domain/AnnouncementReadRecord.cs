using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Announcements.Domain;

/// <summary>Bir çalışanın bir duyuruyu okuduğu kayıt (analytics: kaç kişi okudu).</summary>
public sealed class AnnouncementReadRecord : TenantEntity<Guid>
{
    // ReSharper disable once UnusedMember.Local
    private AnnouncementReadRecord() { }

    public Guid AnnouncementId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateTime ReadAt { get; private set; }

    public static AnnouncementReadRecord Create(Guid tenantId, Guid announcementId, Guid employeeId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AnnouncementId = announcementId,
            EmployeeId = employeeId,
            ReadAt = DateTime.UtcNow,
        };
}

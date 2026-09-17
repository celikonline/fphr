using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain;

/// <summary>
/// Duyuru okunma kaydı (analytics).
/// </summary>
public sealed class AnnouncementReadRecord : DomainEntity
{
    // ReSharper disable UnusedMember.Local
    private AnnouncementReadRecord() { }

    public Guid AnnouncementId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateTime ReadAt { get; private set; }

    public static Result<AnnouncementReadRecord> Create(
        Guid announcementId,
        Guid tenantId,
        Guid employeeId)
    {
        var record = new AnnouncementReadRecord
        {
            Id = Guid.NewGuid(),
            AnnouncementId = announcementId,
            TenantId = tenantId,
            EmployeeId = employeeId,
            ReadAt = DateTime.UtcNow
        };

        return record;
    }
}

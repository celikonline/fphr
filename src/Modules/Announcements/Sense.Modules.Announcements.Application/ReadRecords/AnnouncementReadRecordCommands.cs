using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.ReadRecords;

/// <summary>Çalışan bir duyuruyu okudu olarak işaretler; idempotent (zaten okunmuşsa tekrar yazmaz).</summary>
[RequiresPermission("announcements.my.feed")]
public sealed record MarkAnnouncementReadCommand(Guid AnnouncementId) : ICommand;

public sealed class MarkAnnouncementReadHandler(
    ITenantContext tenant, ICurrentUser currentUser, IAnnouncementRepository announcements, IAnnouncementReadRecordRepository readRecords)
    : ICommandHandler<MarkAnnouncementReadCommand>
{
    public async Task<Result> Handle(MarkAnnouncementReadCommand command, CancellationToken cancellationToken)
    {
        var employeeId = currentUser.EmployeeId;
        if (employeeId is null)
        {
            return Result.Success();
        }

        // Yalnız yayındaki bir duyuru okunabilir; taslak/arşiv id'si tahmin edilerek kayıt oluşturulamaz.
        var announcement = await announcements.GetByIdAsync(tenant.TenantId, command.AnnouncementId, cancellationToken);
        if (announcement is null || announcement.Status != AnnouncementStatus.Published)
        {
            return Error.NotFound(AnnouncementDomainErrors.NotFound);
        }

        if (await readRecords.ExistsAsync(tenant.TenantId, command.AnnouncementId, employeeId.Value, cancellationToken))
        {
            return Result.Success();
        }

        readRecords.Add(AnnouncementReadRecord.Create(tenant.TenantId, command.AnnouncementId, employeeId.Value));
        return Result.Success();
    }
}

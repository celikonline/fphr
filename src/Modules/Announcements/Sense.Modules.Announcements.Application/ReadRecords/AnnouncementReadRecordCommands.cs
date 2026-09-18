using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.ReadRecords;

/// <summary>Çalışan bir duyuruyu okudu olarak işaretler; idempotent (zaten okunmuşsa tekrar yazmaz).</summary>
public sealed record MarkAnnouncementReadCommand(Guid AnnouncementId) : ICommand;

public sealed class MarkAnnouncementReadHandler(
    ITenantContext tenant, ICurrentUser currentUser, IAnnouncementReadRecordRepository readRecords)
    : ICommandHandler<MarkAnnouncementReadCommand>
{
    public async Task<Result> Handle(MarkAnnouncementReadCommand command, CancellationToken cancellationToken)
    {
        var employeeId = currentUser.EmployeeId;
        if (employeeId is null)
        {
            return Result.Success();
        }

        if (await readRecords.ExistsAsync(tenant.TenantId, command.AnnouncementId, employeeId.Value, cancellationToken))
        {
            return Result.Success();
        }

        readRecords.Add(AnnouncementReadRecord.Create(tenant.TenantId, command.AnnouncementId, employeeId.Value));
        return Result.Success();
    }
}

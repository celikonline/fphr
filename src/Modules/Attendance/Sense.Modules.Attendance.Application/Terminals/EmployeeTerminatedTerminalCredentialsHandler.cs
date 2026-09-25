using Sense.Modules.Attendance.Domain;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// EmployeeTerminated → çalışanın aktif terminal kimliklerini pasife alır (Z5.4). Okutma ucu işten ayrılanın okutmasını zaten reddeder (T1,
/// employee_not_active); pasifleştirme kartın başka çalışana devredilebilmesini (<c>Reassign</c>) sağlar. Kayıt silinmez (denetim izi).
/// İdempotent: tekrar teslimde aktif kimlik kalmadığından hiçbir şey yapmaz. Kiracı kapsamını olay veriyolu kurar.
/// </summary>
public sealed class EmployeeTerminatedTerminalCredentialsHandler(ITerminalRepository terminals, IAttendanceUnitOfWork unitOfWork)
    : IIntegrationEventHandler<EmployeeTerminated>
{
    public async Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken)
    {
        var active = (await terminals.ListCredentialsForEmployeeAsync(integrationEvent.EmployeeId, cancellationToken))
            .Where(c => c.IsActive && c.TenantId == integrationEvent.TenantId)
            .ToList();
        if (active.Count == 0)
        {
            return;
        }

        foreach (var credential in active)
        {
            credential.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

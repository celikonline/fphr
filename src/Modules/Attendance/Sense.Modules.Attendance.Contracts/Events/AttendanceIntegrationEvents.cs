using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Contracts.Events;

/// <summary>An employee's work schedule changed, effective from a date (11.3.6 "SenseİK tasarım kararı").</summary>
public sealed record WorkScheduleAssigned(Guid TenantId, Guid EmployeeId, Guid WorkScheduleId, DateOnly EffectiveFrom)
    : IntegrationEvent(TenantId);

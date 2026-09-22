using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Employee.Contracts.Events;

/// <summary>
/// Daily (JobType.EmployeeBirthdayGreeting): the active employees whose birthday is <paramref name="Date"/> (29 February
/// birthdays are celebrated on 28 February in common years). Carries ids only - birth dates are KVKK-sensitive and never
/// travel on an event. Not raised when nobody has a birthday that day.
/// </summary>
public sealed record EmployeeBirthdaysToday(Guid TenantId, DateOnly Date, IReadOnlyList<Guid> EmployeeIds) : IntegrationEvent(TenantId);

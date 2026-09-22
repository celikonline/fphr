using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Employee.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily per-tenant pass (<see cref="JobType.EmployeeBirthdayGreeting"/>, registered in EmployeeModule - Hangfire runs in
/// Sense.Api): finds the active employees born on today's month/day and publishes <see cref="EmployeeBirthdaysToday"/>
/// for Notifications. Nothing is published when nobody has a birthday.
/// </summary>
public sealed class BirthdayGreetingJobHandler(
    EmployeeDbContext context,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : IScheduledJobHandler
{
    private const int February = 2;
    private const int LeapDay = 29;

    public JobType Handles => JobType.EmployeeBirthdayGreeting;

    public async Task ExecuteAsync(ScheduledJobContext jobContext, CancellationToken ct)
    {
        if (jobContext.TenantId is not { } tenantId)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var employeeIds = await FindBirthdaysAsync(tenantId, today, ct);
        if (employeeIds.Count == 0)
        {
            return;
        }

        outbox.Enqueue(new EmployeeBirthdaysToday(tenantId, today, employeeIds));
        await context.SaveChangesAsync(ct);
    }

    /// <summary>Born on today's month/day; on 28 February of a common year, 29 February birthdays are included too.</summary>
    public async Task<IReadOnlyList<Guid>> FindBirthdaysAsync(Guid tenantId, DateOnly today, CancellationToken ct)
    {
        var includeLeapDay = today is { Month: February, Day: LeapDay - 1 } && !DateTime.IsLeapYear(today.Year);
        var month = today.Month;
        var day = today.Day;

        return await context.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Where(e => e.TenantId == tenantId && e.Status == EmployeeStatus.Active && e.BirthDate != null)
            .Where(e => (e.BirthDate!.Value.Month == month && e.BirthDate.Value.Day == day)
                || (includeLeapDay && e.BirthDate!.Value.Month == February && e.BirthDate.Value.Day == LeapDay))
            .Select(e => e.Id)
            .ToListAsync(ct);
    }
}

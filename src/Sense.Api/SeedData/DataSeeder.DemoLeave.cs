using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Api.SeedData;

public partial class DataSeeder
{
    private sealed record DemoEmployee(Employee Employee, string Email, string? Title = null);

    private sealed record DemoLeaveSeed(Guid TenantId, string TenantSlug, IReadOnlyList<DemoEmployee> Employees);

    private enum DemoLeaveOutcome
    {
        Pending,
        Approved,
        Rejected,
    }

    /// <summary>
    /// Demo leave data inside a tenant scope (Leave's tenant query filters and the holiday calendar lookup need it): the
    /// default "Türk İş Kanunu" rule set, a leave profile with generated Accrual/CarryOver rows for every active employee
    /// (what Leave's EmployeeHiredHandler would do - it finds the profile and does nothing), 5 requests for each fixed
    /// login account (approved past annual in the previous and current period, approved past sick leave, approved future
    /// annual in the current period, rejected annual, pending excuse) and upcoming annual leave for 8 colleagues.
    /// Approved requests mirror CreateLeaveRequestOnBehalfHandler's BypassApproval path (Approved + Usage row).
    /// </summary>
    private async Task SeedDemoLeaveAsync(DemoLeaveSeed seed, CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContextSetter.BeginScope(seed.TenantId, seed.TenantSlug);

        var today = leaveBalances.Today;
        var ruleSet = await leaveBalances.EnsureDefaultRuleSetAsync(seed.TenantId, cancellationToken);
        var annual = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        var sick = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Sick);
        var excuse = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Excuse);

        var active = seed.Employees
            .Where(e => e.Employee.Status == EmployeeStatus.Active && e.Employee.HireDate is not null)
            .ToList();

        // Colleagues with at least two service years (≥ 14 accrued days), picked deterministically from the seeded Random.
        var colleagues = active
            .Where(e => !FixedDemoAccountTenure.ContainsKey(e.Email) && e.Employee.HireDate <= today.AddYears(-2))
            .Select(e => (Demo: e, Key: _random.Next()))
            .OrderBy(x => x.Key)
            .Take(ColleagueLeavePlan.Length)
            .Select((x, index) => (x.Demo.Employee.Id, Plan: ColleagueLeavePlan[index]))
            .ToDictionary(x => x.Id, x => x.Plan);

        var requestCount = 0;
        foreach (var (employee, email, _) in active)
        {
            var hireDate = employee.HireDate!.Value;
            var accrualStart = employee.LeaveAccrualStartDate ?? hireDate;
            var profile = EmployeeLeaveProfile.Create(seed.TenantId, employee.Id, ruleSet.Id, hireDate, accrualStart);
            // Mirrors what Leave's EmployeeHiredHandler now does on the real EmployeeHired path (senseik-007): without
            // this, every demo profile's BirthDate stays null and the ≤18/≥50 statutory age exception never fires here.
            profile.SetDates(hireDate, accrualStart, employee.BirthDate);
            leaveCtx.Profiles.Add(profile);

            var usage = new List<LeaveLedgerEntry>();
            var context = new DemoLeaveContext(seed.TenantId, employee.Id, today, usage);

            if (FixedDemoAccountTenure.TryGetValue(email, out var tenure))
            {
                // Each account's leave sits in a slightly different week (deterministic, from its tenure) so the
                // calendar does not show six identical rows.
                var shift = tenure.Years % 4;
                var period = LeaveBalanceEngine.PeriodContaining(annual.AccrualBasis, profile.LeaveAccrualStartDate, today);
                var previousPeriodWeek = WeekStart(period.From.AddDays(-120));
                var pastWeek = WeekStart(today.AddDays(-42 - (7 * shift)));
                var sickWeek = WeekStart(today.AddDays(-21 - shift));
                var rejectedWeek = WeekStart(today.AddDays(14 + (7 * (tenure.Years % 2))));
                var futureWeek = WeekStart(today.AddDays(21 + (7 * shift)));
                var excuseDay = NextWorkingDay(today.AddDays(5 + (tenure.Years % 6)));

                requestCount += await AddDemoLeaveRequestAsync(context, annual, previousPeriodWeek, previousPeriodWeek.AddDays(4), "Yaz tatili", DemoLeaveOutcome.Approved, cancellationToken);
                requestCount += await AddDemoLeaveRequestAsync(context, annual, pastWeek, pastWeek.AddDays(4), "Aile ziyareti", DemoLeaveOutcome.Approved, cancellationToken);
                requestCount += await AddDemoLeaveRequestAsync(context, sick, sickWeek.AddDays(1), sickWeek.AddDays(2), "Grip", DemoLeaveOutcome.Approved, cancellationToken);
                requestCount += await AddDemoLeaveRequestAsync(context, annual, futureWeek, futureWeek.AddDays(2), "Kısa tatil", DemoLeaveOutcome.Approved, cancellationToken);
                requestCount += await AddDemoLeaveRequestAsync(context, annual, rejectedWeek.AddDays(3), rejectedWeek.AddDays(4), "Uzun hafta sonu", DemoLeaveOutcome.Rejected, cancellationToken);
                requestCount += await AddDemoLeaveRequestAsync(context, excuse, excuseDay, excuseDay, "Resmi daire işlemleri", DemoLeaveOutcome.Pending, cancellationToken);
            }
            else if (colleagues.TryGetValue(employee.Id, out var plan))
            {
                var start = today.AddDays(plan.StartInDays);
                var end = AddWorkingDays(NextWorkingDay(start), plan.WorkingDays - 1);
                var outcome = plan.Approved ? DemoLeaveOutcome.Approved : DemoLeaveOutcome.Pending;
                requestCount += await AddDemoLeaveRequestAsync(context, annual, start, end, null, outcome, cancellationToken);
            }

            leaveCtx.LedgerEntries.AddRange(usage);
            await leaveBalances.RecalculateAsync(profile, ruleSet, cancellationToken, usage);
        }

        await leaveCtx.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Demo leave data seeded: 1 rule set, {ProfileCount} leave profiles, {RequestCount} leave requests ({ColleagueCount} colleagues with upcoming leave)",
            active.Count, requestCount, colleagues.Count);
    }

    private sealed record DemoLeaveContext(Guid TenantId, Guid EmployeeId, DateOnly Today, List<LeaveLedgerEntry> Usage);

    /// <summary>Adds one request (days from the real work schedule + holiday calendar); returns 1, or 0 when the range has no leave days.</summary>
    private async Task<int> AddDemoLeaveRequestAsync(
        DemoLeaveContext context, LeaveType type, DateOnly start, DateOnly end, string? description, DemoLeaveOutcome outcome, CancellationToken cancellationToken)
    {
        var count = await leaveDayCalculator.CalculateAsync(context.TenantId, context.EmployeeId, start, end, type.CountsNonWorkingDays, cancellationToken);
        if (count.Days <= 0m)
        {
            // Demo employees have no work schedule yet at seed time: Attendance assigns the default one when the Worker
            // processes EmployeeHired, which is after this seeder runs, so the calculator reads every day as weekly rest.
            // Fall back to the Monday–Friday assumption (the default schedule) with the tenant's holiday calendar.
            count = await CountWithMondayToFridayFallbackAsync(context.TenantId, start, end, type.CountsNonWorkingDays, cancellationToken);
        }

        if (count.Days <= 0m)
        {
            logger.LogDebug("Skipping demo leave {Start}-{End} for employee {EmployeeId}: no leave days", start, end, context.EmployeeId);
            return 0;
        }

        // Submitted two weeks before the start (at most yesterday), decided a day later (never in the future).
        var now = DateTime.UtcNow;
        var submittedOn = start.AddDays(-14) < context.Today ? start.AddDays(-14) : context.Today.AddDays(-1);
        var submittedAt = submittedOn.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Utc);
        var decidedAt = submittedAt.AddDays(1) < now ? submittedAt.AddDays(1) : now;

        var submitted = LeaveRequest.Submit(
            context.TenantId, context.EmployeeId, type.Id, type.Code, start, end, count.Days, description, LeaveRequestSource.SelfService, submittedAt, null, null);
        if (submitted.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed demo leave request: {submitted.Error}");
        }

        var request = submitted.Value;
        switch (outcome)
        {
            case DemoLeaveOutcome.Approved:
                ThrowIfFailed(request.ApproveWithoutWorkflow(decidedAt));
                context.Usage.Add(LeaveLedgerEntry.Usage(
                    request.TenantId, request.EmployeeId, request.LeaveTypeId, request.CalculatedDays, request.StartDate, LeaveLedgerSource.Request, request.Id));
                break;
            case DemoLeaveOutcome.Rejected:
                // No Workflow approval request exists for seed data; a placeholder id satisfies the decision guard
                // (the request detail simply shows no approval steps for an unknown id).
                var approvalId = Guid.CreateVersion7();
                request.AttachApproval(approvalId);
                ThrowIfFailed(request.Reject(approvalId, decidedAt));
                break;
        }

        leaveCtx.Requests.Add(request);
        return 1;
    }

    private async Task<LeaveDayCount> CountWithMondayToFridayFallbackAsync(
        Guid tenantId, DateOnly start, DateOnly end, bool countsNonWorkingDays, CancellationToken cancellationToken)
    {
        var calendar = await holidayCalendars.ResolveCalendarAsync(tenantId, null, null, cancellationToken);
        var occurrences = calendar is null ? [] : await holidayCalendars.GetHolidaysAsync(tenantId, calendar.Id, start, end, cancellationToken);
        var (holidayMap, halfDayHolidays) = HolidayDayMaps.Build(occurrences);

        return LeaveDayCounter.Count(start, end, LeaveDayCounter.MondayToFriday, holidayMap, countsNonWorkingDays, halfDayHolidays: halfDayHolidays);
    }

    private static void ThrowIfFailed(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed demo leave request: {result.Error}");
        }
    }

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static DateOnly NextWorkingDay(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static DateOnly AddWorkingDays(DateOnly date, int workingDays)
    {
        for (var added = 0; added < workingDays;)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                added++;
            }
        }

        return date;
    }
}

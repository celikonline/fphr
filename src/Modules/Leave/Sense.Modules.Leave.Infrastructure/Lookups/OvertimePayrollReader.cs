using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Leave.Contracts.IOvertimePayrollReader (kart senseik-012 §4). A request whose linked conversion is Posted+TimeOff
/// is excluded from the cash buckets (already paid out as leave); one whose conversion Expired (kart senseik-012 §3,
/// the 6-month m.41 window lapsed) falls back into its rate bucket - the employer owes cash pay once the free-time
/// obligation lapses unused. Mirrors LeaveDayReader's direct-EF-query style.
/// </summary>
public sealed class OvertimePayrollReader(LeaveDbContext context) : IOvertimePayrollReader
{
    public async Task<IReadOnlyList<OvertimePayrollLine>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var ids = employeeIds.ToList();
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var requests = await context.OvertimeRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.Status == OvertimeRequestStatus.Approved
                && r.WorkDate >= from && r.WorkDate <= to)
            .Select(r => new { r.EmployeeId, r.PayableMinutes, r.PayRateKind, r.ConversionId })
            .ToListAsync(cancellationToken);

        if (requests.Count == 0)
        {
            return [];
        }

        var conversionIds = requests.Where(r => r.ConversionId is not null).Select(r => r.ConversionId!.Value).ToList();
        var conversions = conversionIds.Count == 0
            ? new Dictionary<Guid, (OvertimeConversionKind Kind, int ResultMinutes, OvertimeConversionStatus Status)>()
            : await context.OvertimeConversions.AsNoTracking()
                .Where(c => conversionIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (c.Kind, c.ResultMinutes, c.Status), cancellationToken);

        return requests
            .GroupBy(r => r.EmployeeId)
            .Select(group =>
            {
                var rate25 = 0;
                var rate50 = 0;
                var timeOff = 0;
                foreach (var r in group)
                {
                    var conversion = r.ConversionId is { } id && conversions.TryGetValue(id, out var c)
                        ? c
                        : ((OvertimeConversionKind Kind, int ResultMinutes, OvertimeConversionStatus Status)?)null;
                    if (conversion is { Kind: OvertimeConversionKind.TimeOff, Status: OvertimeConversionStatus.Posted } posted)
                    {
                        timeOff += posted.ResultMinutes;
                        continue;
                    }

                    if (r.PayRateKind == OvertimePayRateKind.Rate25)
                    {
                        rate25 += r.PayableMinutes;
                    }
                    else
                    {
                        rate50 += r.PayableMinutes;
                    }
                }

                return new OvertimePayrollLine(group.Key, rate25, rate50, timeOff);
            })
            .ToList();
    }
}

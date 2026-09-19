using Sense.Modules.Leave.Domain.Overtime;

namespace Sense.Api.SeedData;

public partial class DataSeeder
{
    /// <summary>
    /// Demo overtime data (kart senseik-030): a few approved <see cref="OvertimeRequest"/>s for two fixed demo accounts
    /// inside the last two weeks, so the new "Aylık fazla mesai raporu" (leave.overtime.monthly - defaults to the
    /// current month when no Ay/Yıl filter is given) has something to show right after seeding, plus one explicit
    /// Kind=Pay conversion (kart senseik-030 §3, "fazla mesaiyi ödemeye çevir") to demonstrate the new feature.
    /// Directly builds domain aggregates with a placeholder approval id (no Workflow approval record exists in seed
    /// data) - mirrors <see cref="AddDemoLeaveRequestAsync"/>'s pattern for the Rejected leave case.
    /// </summary>
    private async Task SeedDemoOvertimeAsync(DemoLeaveSeed seed, CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContextSetter.BeginScope(seed.TenantId, seed.TenantSlug);

        var today = leaveBalances.Today;
        var byEmail = seed.Employees.ToDictionary(e => e.Email, e => e.Employee.Id, StringComparer.OrdinalIgnoreCase);
        if (!byEmail.TryGetValue("calisan@demo.local", out var employeeId) || !byEmail.TryGetValue("yonetici@demo.local", out var managerId))
        {
            logger.LogWarning("Skipping demo overtime data: fixed demo accounts not found");
            return;
        }

        var requestCount = 0;
        requestCount += AddDemoOvertimeRequest(
            seed.TenantId, employeeId, DemoOvertimeWorkDate(today, 12), new TimeOnly(18, 0), 120, OvertimePayRateKind.Rate25, "Ay sonu kapanışı", convertToPay: true);
        requestCount += AddDemoOvertimeRequest(
            seed.TenantId, employeeId, DemoOvertimeWorkDate(today, 5), new TimeOnly(19, 0), 90, OvertimePayRateKind.Rate50, null, convertToPay: false);
        requestCount += AddDemoOvertimeRequest(
            seed.TenantId, managerId, DemoOvertimeWorkDate(today, 8), new TimeOnly(18, 30), 150, OvertimePayRateKind.Rate50, "Denetim hazırlığı", convertToPay: false);

        await leaveCtx.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo overtime data seeded: {RequestCount} approved overtime requests (1 converted to pay)", requestCount);
    }

    /// <summary>
    /// Working day <paramref name="daysAgo"/> days before today, pulled forward to the month's first working day when it
    /// would fall into the previous month - so the monthly report's default (current month) shows the seeded rows. When
    /// the current month has no working day up to today yet, the unclamped (previous-month) date is kept.
    /// </summary>
    private static DateOnly DemoOvertimeWorkDate(DateOnly today, int daysAgo)
    {
        var candidate = NextWorkingDay(today.AddDays(-daysAgo));
        if (candidate.Year == today.Year && candidate.Month == today.Month)
        {
            return candidate;
        }

        var monthStart = NextWorkingDay(new DateOnly(today.Year, today.Month, 1));
        return monthStart <= today ? monthStart : candidate;
    }

    /// <summary>Adds one approved overtime request (placeholder approval id); optionally posts an explicit Kind=Pay conversion. Returns 1.</summary>
    private int AddDemoOvertimeRequest(
        Guid tenantId, Guid employeeId, DateOnly workDate, TimeOnly startTime, int durationMinutes, OvertimePayRateKind payRateKind, string? description,
        bool convertToPay)
    {
        var now = DateTime.UtcNow;
        var submittedAt = workDate.ToDateTime(new TimeOnly(21, 0), DateTimeKind.Utc);
        var decidedAt = submittedAt.AddDays(1) < now ? submittedAt.AddDays(1) : now;

        var submitted = OvertimeRequest.Submit(
            tenantId, employeeId, workDate, startTime, durationMinutes, OvertimeDayKind.Workday, 0, description,
            OvertimeRequestSource.Manual, submittedAt, null, null, payRateKind);
        if (submitted.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed demo overtime request: {submitted.Error}");
        }

        var request = submitted.Value;
        // No Workflow approval request exists for seed data; a placeholder id satisfies the decision guard (mirrors
        // AddDemoLeaveRequestAsync's Rejected branch).
        var approvalId = Guid.CreateVersion7();
        request.AttachApproval(approvalId);
        ThrowIfFailed(request.Approve(approvalId, decidedAt));

        if (convertToPay)
        {
            var conversion = OvertimeConversion.ToPay(request, OvertimeMinutesBasis.Payable, null, decidedAt, "Demo: nakde çevrildi");
            if (conversion.IsFailure)
            {
                throw new InvalidOperationException($"Failed to seed demo overtime conversion: {conversion.Error}");
            }

            ThrowIfFailed(request.MarkConverted(conversion.Value.Id));
            leaveCtx.OvertimeConversions.Add(conversion.Value);
        }

        leaveCtx.OvertimeRequests.Add(request);
        return 1;
    }
}

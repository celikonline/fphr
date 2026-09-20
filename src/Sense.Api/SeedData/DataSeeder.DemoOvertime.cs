using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Shared.Kernel.Results;

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

        var ruleSet = await leaveBalances.EnsureDefaultRuleSetAsync(seed.TenantId, cancellationToken);
        var annualTypeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;

        var requestCount = 0;
        // Employee: 2 sa × 1,25 → 2 sa 30 dk yıllık izne çevrilmiş (İzne dönüşümler satırı süre, gün ve izin türüyle dolu).
        requestCount += AddDemoOvertimeRequest(
            seed.TenantId, employeeId, DemoOvertimeWorkDate(today, 12), new TimeOnly(18, 0), 120, OvertimePayRateKind.Rate25, "Ay sonu kapanışı",
            DemoOvertimeConversionMode.TimeOff, annualTypeId);
        requestCount += AddDemoOvertimeRequest(
            seed.TenantId, employeeId, DemoOvertimeWorkDate(today, 5), new TimeOnly(19, 0), 90, OvertimePayRateKind.Rate50, null,
            DemoOvertimeConversionMode.None, annualTypeId);
        // Manager: the explicit Kind=Pay conversion demo (kart senseik-030) - a pay row has no leave type/days by design.
        requestCount += AddDemoOvertimeRequest(
            seed.TenantId, managerId, DemoOvertimeWorkDate(today, 8), new TimeOnly(18, 30), 150, OvertimePayRateKind.Rate50, "Denetim hazırlığı",
            DemoOvertimeConversionMode.Pay, annualTypeId);

        await leaveCtx.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo overtime data seeded: {RequestCount} approved overtime requests (1 converted to time off, 1 to pay)", requestCount);
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

    internal enum DemoOvertimeConversionMode
    {
        None,
        Pay,
        TimeOff,
    }

    /// <summary>One seeded overtime request with its optional conversion (and the ExtraGrant ledger row a time-off conversion posts).</summary>
    internal sealed record DemoOvertime(OvertimeRequest Request, OvertimeConversion? Conversion, LeaveLedgerEntry? Grant);

    /// <summary>Adds one approved overtime request (placeholder approval id) with its optional conversion. Returns 1.</summary>
    private int AddDemoOvertimeRequest(
        Guid tenantId, Guid employeeId, DateOnly workDate, TimeOnly startTime, int durationMinutes, OvertimePayRateKind payRateKind, string? description,
        DemoOvertimeConversionMode conversionMode, Guid annualTypeId)
    {
        var seeded = BuildDemoOvertime(tenantId, employeeId, workDate, startTime, durationMinutes, payRateKind, description, conversionMode, annualTypeId);
        if (seeded.Conversion is not null)
        {
            leaveCtx.OvertimeConversions.Add(seeded.Conversion);
        }

        if (seeded.Grant is not null)
        {
            leaveCtx.LedgerEntries.Add(seeded.Grant);
        }

        leaveCtx.OvertimeRequests.Add(seeded.Request);
        return 1;
    }

    /// <summary>
    /// Builds the aggregates of one demo overtime request. A TimeOff conversion mirrors <c>ConvertOvertimeToTimeOffHandler</c>
    /// (rate = the request's own multiplier, <see cref="OvertimeMath.DefaultDailyWorkMinutes"/>, ExtraGrant row linked to the
    /// conversion) so the "İzne dönüşümler" row carries duration, days and leave type instead of a pay-style blank.
    /// </summary>
    internal static DemoOvertime BuildDemoOvertime(
        Guid tenantId, Guid employeeId, DateOnly workDate, TimeOnly startTime, int durationMinutes, OvertimePayRateKind payRateKind, string? description,
        DemoOvertimeConversionMode conversionMode, Guid annualTypeId)
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

        switch (conversionMode)
        {
            case DemoOvertimeConversionMode.Pay:
                var pay = OvertimeConversion.ToPay(request, OvertimeMinutesBasis.Payable, null, decidedAt, "Demo: nakde çevrildi");
                ThrowIfConversionFailed(pay);
                ThrowIfFailed(request.MarkConverted(pay.Value.Id));
                return new DemoOvertime(request, pay.Value, null);

            case DemoOvertimeConversionMode.TimeOff:
                var timeOff = OvertimeConversion.ToTimeOff(
                    request, OvertimeMinutesBasis.Payable, request.PayRateMultiplier, OvertimeMath.DefaultDailyWorkMinutes, annualTypeId, null, decidedAt,
                    "Demo: izne çevrildi", DateOnly.FromDateTime(decidedAt).AddMonths(ConvertOvertimeToTimeOffHandler.FallbackExpiryMonths));
                ThrowIfConversionFailed(timeOff);
                var grant = LeaveLedgerEntry.ExtraGrant(
                    tenantId, employeeId, annualTypeId, timeOff.Value.ResultDays, DateOnly.FromDateTime(decidedAt), LeaveLedgerSource.OvertimeConversion,
                    timeOff.Value.Id, "Demo: izne çevrildi");
                ThrowIfConversionFailed(grant);
                timeOff.Value.AttachLedgerEntry(grant.Value.Id);
                ThrowIfFailed(request.MarkConverted(timeOff.Value.Id));
                return new DemoOvertime(request, timeOff.Value, grant.Value);

            default:
                return new DemoOvertime(request, null, null);
        }
    }

    private static void ThrowIfConversionFailed<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed demo overtime conversion: {result.Error}");
        }
    }
}

using NSubstitute;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;

namespace Sense.Modules.Payroll.Tests.Payslips;

/// <summary>Shared fixtures for the E2 payslip acknowledgement/objection tests (paid periods, published payslips, fake repositories).</summary>
internal static class PayslipTestKit
{
    public static readonly Guid TenantId = Guid.NewGuid();
    public static readonly DateTimeOffset PaidAt = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Now = new(2026, 9, 10, 14, 30, 0, TimeSpan.Zero);

    public static PayrollPeriod PaidPeriod(int month = 8)
    {
        var period = PayrollPeriod.Create(TenantId, 2026, month).Value;
        period.Approve(null);
        period.MarkPaid();
        return period;
    }

    public static PayrollPeriod OpenPeriod(int month = 8) => PayrollPeriod.Create(TenantId, 2026, month).Value;

    public static Payslip PayslipOf(Guid employeeId, PayrollPeriod period, bool published = true)
    {
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 50_000m, 38_000m, 60_000m);
        if (published)
        {
            payslip.Publish(PaidAt);
        }

        return payslip;
    }

    public static IPayslipRepository PayslipRepository(params Payslip[] payslips)
    {
        var repository = Substitute.For<IPayslipRepository>();
        foreach (var payslip in payslips)
        {
            repository.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        }

        repository.ListByEmployeeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Payslip>>(payslips.Where(p => p.EmployeeId == call.Arg<Guid>()).ToList()));
        repository.ListPublishedUnacknowledgedAsync(Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Payslip>>(payslips
                .Where(p => p.PublishedAt != null && p.AcknowledgedAt == null)
                .Where(p => call.ArgAt<Guid?>(0) is not { } periodId || p.PayrollPeriodId == periodId)
                .Where(p => call.ArgAt<DateTimeOffset?>(1) is not { } since || p.PublishedAt >= since)
                .ToList()));
        return repository;
    }

    public static IPayrollPeriodRepository PeriodRepository(params PayrollPeriod[] periods)
    {
        var repository = Substitute.For<IPayrollPeriodRepository>();
        foreach (var period in periods)
        {
            repository.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        }

        repository.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>(periods.ToList()));
        return repository;
    }

    /// <summary>A saver whose early flush always wins the optimistic-concurrency race (the DB race is covered by the integration tests).</summary>
    public static Application.Payslips.IPayslipChangeSaver Saver(Sense.Shared.Kernel.Results.Result? outcome = null)
    {
        var saver = Substitute.For<Application.Payslips.IPayslipChangeSaver>();
        saver.SaveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(outcome ?? Sense.Shared.Kernel.Results.Result.Success()));
        return saver;
    }

    public static ICurrentUser CurrentEmployee(Guid? employeeId, string? ipAddress = "203.0.113.7")
    {
        var user = Substitute.For<ICurrentUser>();
        user.EmployeeId.Returns(employeeId);
        user.UserId.Returns(Guid.NewGuid());
        user.IpAddress.Returns(ipAddress);
        return user;
    }

    public static TimeProvider ClockAt(DateTimeOffset now)
    {
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(now);
        return clock;
    }
}

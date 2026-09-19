using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class MonthlyTimesheetApprovalDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTime ApprovedAt = new(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Approve_NormalizesPeriodMonthToFirstDay()
    {
        var approvedByUserId = Guid.NewGuid();

        var approval = MonthlyTimesheetApproval.Approve(TenantId, EmployeeId, new DateOnly(2026, 9, 17), ApprovedAt, approvedByUserId);

        approval.PeriodMonth.ShouldBe(new DateOnly(2026, 9, 1));
        approval.EmployeeId.ShouldBe(EmployeeId);
        approval.ApprovedAt.ShouldBe(ApprovedAt);
        approval.ApprovedByUserId.ShouldBe(approvedByUserId);
    }

    [Fact]
    public void Reapprove_BumpsTimestampAndActor_SameRow()
    {
        var approval = MonthlyTimesheetApproval.Approve(TenantId, EmployeeId, new DateOnly(2026, 9, 1), ApprovedAt, Guid.NewGuid());
        var originalId = approval.Id;
        var secondApprover = Guid.NewGuid();
        var secondApprovedAt = ApprovedAt.AddDays(1);

        approval.Reapprove(secondApprovedAt, secondApprover);

        approval.Id.ShouldBe(originalId);
        approval.ApprovedAt.ShouldBe(secondApprovedAt);
        approval.ApprovedByUserId.ShouldBe(secondApprover);
    }

    [Theory]
    [InlineData(2026, 9, 1)]
    [InlineData(2026, 9, 30)]
    [InlineData(2026, 9, 15)]
    public void NormalizeToMonthStart_AnyDayInMonth_ReturnsFirstDay(int year, int month, int day)
    {
        var normalized = MonthlyTimesheetApproval.NormalizeToMonthStart(new DateOnly(year, month, day));

        normalized.ShouldBe(new DateOnly(year, month, 1));
    }
}

using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.PunchPipeline.PunchTestData;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>Okutmalardan gün türetme (spec §5.1, Z0.1/Z0.4): ilk In / son Out, ara Out→In molası ve Incalculable koşulları.</summary>
public sealed class DayFromPunchesTests
{
    [Theory]
    [InlineData("In,Out,In")] // tek sayı
    [InlineData("Out,In")] // ilk kayıt Out
    [InlineData("In,In")] // yön bozuk
    public void Incalculable_when_direction_sequence_invalid(string seq)
    {
        var punches = Punches(seq, "09:00,12:00,13:00");

        var r = DayFromPunches.Build(punches, plan: Plan("09:00", "18:00", 60));

        r.Incalculable.ShouldBeTrue();
    }

    [Fact]
    public void Break_gap_between_out_in_adds_to_actual_break()
    {
        // In 09:00, Out 12:00, In 12:45, Out 18:00 → ActualBreakMinutes 45
        DayFromPunches.Build(Punches("In,Out,In,Out", "09:00,12:00,12:45,18:00"), Plan("09:00", "18:00", 60)).ActualBreakMinutes.ShouldBe(45);
    }

    [Fact]
    public void Paired_punches_give_first_in_and_last_out()
    {
        var r = DayFromPunches.Build(Punches("In,Out,In,Out", "08:55,12:00,12:45,18:05"), Plan("09:00", "18:00", 60));

        r.ShouldBe(new DayResult(new TimeOnly(8, 55), new TimeOnly(18, 5), 45, Incalculable: false, Overnight: false));
    }

    [Fact]
    public void Punch_order_is_chronological_not_insertion_order()
    {
        var punches = Punches("Out,In", "18:00,09:00");

        var r = DayFromPunches.Build(punches, Plan("09:00", "18:00", 60));

        r.Incalculable.ShouldBeFalse();
        r.CheckIn.ShouldBe(new TimeOnly(9, 0));
        r.CheckOut.ShouldBe(new TimeOnly(18, 0));
    }

    [Fact]
    public void Single_in_is_an_open_day_not_incalculable()
    {
        var r = DayFromPunches.Build(Punches("In", "09:00"), Plan("09:00", "18:00", 60));

        r.ShouldBe(new DayResult(new TimeOnly(9, 0), null, 0, Incalculable: false, Overnight: false));
    }

    [Fact]
    public void Single_out_keeps_missing_check_in_semantics()
    {
        var r = DayFromPunches.Build(Punches("Out", "18:00"), Plan("09:00", "18:00", 60));

        r.ShouldBe(new DayResult(null, new TimeOnly(18, 0), 0, Incalculable: false, Overnight: false));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("In,Unknown,Out")]
    public void Odd_count_with_unknown_direction_is_incalculable(string seq)
    {
        DayFromPunches.Build(Punches(seq, "09:00,12:00,18:00"), Plan("09:00", "18:00", 60)).Incalculable.ShouldBeTrue();
    }

    [Fact]
    public void Even_unknown_directions_are_paired_in_order()
    {
        var r = DayFromPunches.Build(Punches("Unknown,Unknown", "09:00,18:00"), Plan("09:00", "18:00", 60));

        r.ShouldBe(new DayResult(new TimeOnly(9, 0), new TimeOnly(18, 0), 0, Incalculable: false, Overnight: false));
    }

    [Fact]
    public void Punches_without_any_plan_are_incalculable()
    {
        DayFromPunches.Build(Punches("In,Out", "09:00,18:00"), plan: null).Incalculable.ShouldBeTrue();
    }

    [Fact]
    public void Rest_day_plan_is_not_incalculable()
    {
        var r = DayFromPunches.Build(Punches("In,Out", "10:00,14:00"), PlannedDay.Rest);

        r.ShouldBe(new DayResult(new TimeOnly(10, 0), new TimeOnly(14, 0), 0, Incalculable: false, Overnight: false));
    }

    [Fact]
    public void No_punches_give_an_empty_day()
    {
        DayFromPunches.Build([], plan: null).ShouldBe(new DayResult(null, null, 0, Incalculable: false, Overnight: false));
    }

    [Fact]
    public void Soft_deleted_punches_are_ignored()
    {
        var punches = Punches("In,In,Out", "08:00,09:00,18:00");
        punches[0].SoftDelete(Guid.NewGuid(), "hatalı okutma", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)).IsSuccess.ShouldBeTrue();

        var r = DayFromPunches.Build(punches, Plan("09:00", "18:00", 60));

        r.ShouldBe(new DayResult(new TimeOnly(9, 0), new TimeOnly(18, 0), 0, Incalculable: false, Overnight: false));
    }

    [Fact]
    public void Night_pair_across_midnight_is_overnight()
    {
        var punches = new[]
        {
            Create(PunchDirection.In, new DateTime(2026, 9, 1, 23, 30, 0), Day, PunchSource.Device),
            Create(PunchDirection.Out, new DateTime(2026, 9, 2, 5, 50, 0), Day, PunchSource.Device),
        };

        var r = DayFromPunches.Build(punches, Plan("22:00", "06:00", 30));

        r.ShouldBe(new DayResult(new TimeOnly(23, 30), new TimeOnly(5, 50), 0, Incalculable: false, Overnight: true));
    }

    [Fact]
    public void Times_are_converted_to_the_tenant_zone()
    {
        // 06:00Z / 15:00Z = 09:00 / 18:00 at UTC+3.
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");

        var r = DayFromPunches.Build(Punches("In,Out", "06:00,15:00"), Plan("09:00", "18:00", 60), zone);

        r.CheckIn.ShouldBe(new TimeOnly(9, 0));
        r.CheckOut.ShouldBe(new TimeOnly(18, 0));
    }

    [Fact]
    public void Incalculable_day_never_reports_check_out_before_check_in()
    {
        var r = DayFromPunches.Build(Punches("Out,In", "07:00,09:00"), Plan("09:00", "18:00", 60));

        r.Incalculable.ShouldBeTrue();
        r.CheckIn.ShouldBe(new TimeOnly(9, 0));
        r.CheckOut.ShouldBeNull();
    }
}

using Sense.Modules.Attendance.Application.Punches;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.PunchPipeline.PunchTestData;
using Dir = Sense.Modules.Attendance.Domain.SelfCheckIns.PunchDirection;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>Okutmanın puantaj günü (spec §5.1): plan penceresi start−4sa..end+4sa, gece vardiyası, çakışma → Incalculable.</summary>
public sealed class WorkDateResolverTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep2 = new(2026, 9, 2);

    [Fact]
    public void Night_shift_out_belongs_to_shift_start_day()
    {
        var plan = Plan("22:00", "06:00", 30, startsPreviousDay: false);
        var day1In = Punch(Dir.In, "2026-09-01T23:30");
        var day2Out = Punch(Dir.Out, "2026-09-02T05:50");

        WorkDateResolver.Resolve(day1In, new[] { (Sep1, (PlannedDay?)plan) }).ShouldBe(Sep1);
        // pencere: start-4sa..end+4sa; Eylül 2 22:00 penceresine 05:50 düşmez
        WorkDateResolver.Resolve(day2Out, new[] { (Sep1, (PlannedDay?)plan), (Sep2, (PlannedDay?)plan) }).ShouldBe(Sep1);
    }

    [Fact]
    public void Punch_outside_every_window_uses_the_local_calendar_day()
    {
        var plan = Plan("09:00", "18:00", 60);

        var at = new DateTime(2026, 9, 2, 23, 30, 0);

        WorkDateResolver.ResolveDetailed(at, [(Sep1, plan), (Sep2, plan)]).ShouldBe(new WorkDateResolution(Sep2, Ambiguous: false));
    }

    [Fact]
    public void Window_edges_are_inclusive()
    {
        var plan = Plan("09:00", "18:00", 60);

        WorkDateResolver.ResolveDetailed(new DateTime(2026, 9, 2, 5, 0, 0), [(Sep2, plan)]).Date.ShouldBe(Sep2);
        WorkDateResolver.ResolveDetailed(new DateTime(2026, 9, 2, 22, 0, 0), [(Sep2, plan)]).Date.ShouldBe(Sep2);
    }

    [Fact]
    public void Overlapping_windows_pick_the_closest_start()
    {
        // Eylül 1 gece 22:00-06:00 penceresi 18:00→10:00; Eylül 2 gündüz 08:00-17:00 penceresi 04:00→21:00.
        var night = Plan("22:00", "06:00", 30);
        var day = Plan("08:00", "17:00", 60);

        var result = WorkDateResolver.ResolveDetailed(new DateTime(2026, 9, 2, 7, 50, 0), [(Sep1, night), (Sep2, day)]);

        result.ShouldBe(new WorkDateResolution(Sep2, Ambiguous: false));
    }

    [Fact]
    public void Equal_distance_to_two_shift_starts_is_ambiguous()
    {
        // 02:00 is 4 h after Sep 1 22:00 and 4 h before Sep 2 06:00 - gece vardiyası çakışması.
        var night = Plan("22:00", "06:00", 30);
        var early = Plan("06:00", "14:00", 30);

        var result = WorkDateResolver.ResolveDetailed(new DateTime(2026, 9, 2, 2, 0, 0), [(Sep1, night), (Sep2, early)]);

        result.Ambiguous.ShouldBeTrue();
        result.Date.ShouldBe(Sep1);
    }

    [Fact]
    public void Starts_previous_day_shift_window_opens_the_evening_before()
    {
        // Eylül 2'nin vardiyası bir önceki gün 22:00'de başlar: 1 Eylül 21:50 okutması 2 Eylül'e aittir.
        var plan = Plan("22:00", "06:00", 30, startsPreviousDay: true);

        var result = WorkDateResolver.ResolveDetailed(new DateTime(2026, 9, 1, 21, 50, 0), [(Sep1, PlannedDay.Rest), (Sep2, plan)]);

        result.ShouldBe(new WorkDateResolution(Sep2, Ambiguous: false));
    }

    [Fact]
    public void Rest_and_missing_plans_have_no_window()
    {
        var result = WorkDateResolver.ResolveDetailed(new DateTime(2026, 9, 2, 1, 0, 0), [(Sep1, PlannedDay.Rest), (Sep2, null)]);

        result.ShouldBe(new WorkDateResolution(Sep2, Ambiguous: false));
    }

    [Fact]
    public void Punch_overload_converts_to_the_tenant_zone_first()
    {
        // 20:30Z = 23:30 at UTC+3 → Eylül 1 gece vardiyası penceresinde.
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");
        var punch = Punch(Dir.In, "2026-09-01T20:30");

        WorkDateResolver.Resolve(punch, [(Sep1, Plan("22:00", "06:00", 30))], zone).ShouldBe(Sep1);
    }
}

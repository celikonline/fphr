using System.Globalization;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>Okutma testleri için kısa kurucular (Z0.1): zamanlar UTC duvar saati olarak yorumlanır (dilim verilmezse yerel = UTC).</summary>
internal static class PunchTestData
{
    public static readonly Guid Tenant = Guid.NewGuid();
    public static readonly Guid Employee = Guid.NewGuid();
    public static readonly DateOnly Day = new(2026, 9, 1);

    /// <summary>Doğrulama için "şimdi" - tüm test okutmalarından sonra.</summary>
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<Punch> Punches(string directions, string times, DateOnly? day = null, PunchSource source = PunchSource.Device)
    {
        var date = day ?? Day;
        return directions.Split(',')
            .Zip(times.Split(','))
            .Select(pair => Create(Parse(pair.First), date.ToDateTime(TimeOnly.Parse(pair.Second, CultureInfo.InvariantCulture)), date, source))
            .ToList();
    }

    /// <summary><paramref name="isoLocal"/> ör. "2026-09-01T23:30"; iş günü takvim günüdür (çözümleyici testleri için).</summary>
    public static Punch Punch(PunchDirection direction, string isoLocal)
    {
        var at = DateTime.Parse(isoLocal, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return Create(direction, at, DateOnly.FromDateTime(at), PunchSource.Device);
    }

    public static Punch Create(PunchDirection direction, DateTime at, DateOnly workDate, PunchSource source) =>
        Sense.Modules.Attendance.Domain.Punches.Punch.Create(
            Tenant, Employee, DateTime.SpecifyKind(at, DateTimeKind.Utc), workDate, direction, source, null, null, null, null, null,
            source == PunchSource.Manual, null, null, Now).Value;

    public static PunchPlanWindow Plan(string start, string end, int breakMinutes, bool startsPreviousDay = false) =>
        new(TimeOnly.Parse(start, CultureInfo.InvariantCulture), TimeOnly.Parse(end, CultureInfo.InvariantCulture), breakMinutes, startsPreviousDay);

    private static PunchDirection Parse(string value) => Enum.Parse<PunchDirection>(value.Trim());
}

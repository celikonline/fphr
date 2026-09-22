using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sense.Modules.Organization.Domain.HolidayCalendars;

namespace Sense.Modules.Organization.Infrastructure.Persistence.Seed;

/// <summary>
/// Kiracının kendi "Türkiye Resmi Tatilleri" sistem takvimi kopyası (roadmap Faz 0 #5), gömülü
/// <c>tr-official-holidays.json</c>'dan (kaynak dosya: src/Sense.Api/SeedData/holidays) üretilir — sabit tarih sabitleri değil
/// veridir, böylece yıllık dini bayram güncellemesi yalnız JSON'a dokunur. DataSeeder (dev/demo) ve kiracı bootstrap'i
/// (<see cref="TenantOrganizationBootstrapper"/>) aynı yolu kullanır.
/// </summary>
public static class TrHolidayCalendarSeed
{
    private const string ResourceName = "Sense.Modules.Organization.Infrastructure.tr-official-holidays.json";

    /// <summary>Yıllık tekrarlayan kayıtlar için keyfi çapa yılı (bkz. HolidayCalendarEntry) — geri okunurken yalnız Month/Day kullanılır.</summary>
    private const int RecurringHolidayAnchorYear = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<HolidaySeedFile> Seed = new(Load);

    /// <summary>Takvimi ve tüm kayıtlarını <paramref name="db"/>'ye ekler; SaveChanges çağıranın sorumluluğundadır.</summary>
    public static HolidayCalendar AddTo(OrganizationDbContext db, Guid tenantId)
    {
        var seed = Seed.Value;

        var calendarResult = HolidayCalendar.Create(tenantId, seed.Name, seed.CountryCode, isSystemDefined: true);
        if (calendarResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed TR holiday calendar: {calendarResult.Error}");
        }

        var calendar = calendarResult.Value;
        db.HolidayCalendars.Add(calendar);

        foreach (var entry in seed.Entries)
        {
            var entryResult = HolidayCalendarEntry.Create(tenantId, calendar.Id, ResolveDate(entry), entry.Name, entry.RecurringAnnually, entry.HalfDay);
            if (entryResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to seed holiday entry '{entry.Name}': {entryResult.Error}");
            }

            db.HolidayCalendarEntries.Add(entryResult.Value);
        }

        return calendar;
    }

    private static DateOnly ResolveDate(HolidaySeedEntry entry)
    {
        if (entry.RecurringAnnually)
        {
            if (entry.Month is not { } month || entry.Day is not { } day)
            {
                throw new InvalidOperationException($"Recurring holiday seed entry '{entry.Name}' is missing Month/Day.");
            }

            return new DateOnly(RecurringHolidayAnchorYear, month, day);
        }

        if (string.IsNullOrWhiteSpace(entry.Date))
        {
            throw new InvalidOperationException($"Non-recurring holiday seed entry '{entry.Name}' is missing Date.");
        }

        return DateOnly.ParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static HolidaySeedFile Load()
    {
        using var stream = typeof(TrHolidayCalendarSeed).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded TR holiday seed '{ResourceName}' is missing from the assembly.");

        var file = JsonSerializer.Deserialize<HolidaySeedFile>(stream, JsonOptions);
        if (file is null || file.Entries.Count == 0)
        {
            throw new InvalidOperationException($"Embedded TR holiday seed '{ResourceName}' is empty.");
        }

        return file;
    }

    /// <summary>Deserialization shape for tr-official-holidays.json (Month/Day for recurring entries, Date for one-off ones).</summary>
    private sealed record HolidaySeedFile(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("countryCode")] string CountryCode,
        [property: JsonPropertyName("entries")] IReadOnlyList<HolidaySeedEntry> Entries);

    private sealed record HolidaySeedEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("month")] int? Month,
        [property: JsonPropertyName("day")] int? Day,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("recurringAnnually")] bool RecurringAnnually,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("halfDay")] bool HalfDay = false);
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Validates src/Sense.Api/SeedData/holidays/tr-official-holidays.json (the data file
/// Sense.Api.SeedData.DataSeeder.SeedTrHolidayCalendar reads at seed time - see its class doc comment)
/// against the exact shape that seeder expects, independently of DataSeeder itself (which lives in the
/// Sense.Api exe project and is not otherwise covered by a fast test). Catches the two failure modes a
/// data file - rather than code - is prone to: a malformed/renamed field, or an unparsable date.
/// </summary>
public sealed class TrHolidaySeedFileTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SeedFile_ParsesWithExpectedShape_AndEveryEntryHasAUsableDate()
    {
        var path = FindSeedFilePath();
        var json = File.ReadAllText(path);

        var file = JsonSerializer.Deserialize<SeedFile>(json, JsonOptions);

        file.ShouldNotBeNull();
        file.CountryCode.ShouldBe("TR");
        file.Name.ShouldNotBeNullOrWhiteSpace();
        file.Entries.ShouldNotBeEmpty();

        // The 7 fixed-date national holidays from the roadmap item's spec must be present and recurring.
        string[] expectedRecurringNames =
        [
            "Yılbaşı", "Ulusal Egemenlik ve Çocuk Bayramı", "Emek ve Dayanışma Günü",
            "Atatürk'ü Anma, Gençlik ve Spor Bayramı", "Demokrasi ve Millî Birlik Günü",
            "Zafer Bayramı", "Cumhuriyet Bayramı",
        ];
        var recurring = file.Entries.Where(e => e.RecurringAnnually).ToList();
        recurring.Select(e => e.Name).ShouldBe(expectedRecurringNames, ignoreOrder: true);

        foreach (var entry in file.Entries)
        {
            entry.Name.ShouldNotBeNullOrWhiteSpace();

            if (entry.RecurringAnnually)
            {
                entry.Month.ShouldNotBeNull($"Recurring entry '{entry.Name}' must have Month.");
                entry.Day.ShouldNotBeNull($"Recurring entry '{entry.Name}' must have Day.");
                entry.Date.ShouldBeNull($"Recurring entry '{entry.Name}' should not also carry a Date.");

                // Must construct a valid DateOnly on the anchor year DataSeeder uses (2000, a leap year).
                Should.NotThrow(() => new DateOnly(2000, entry.Month!.Value, entry.Day!.Value));
            }
            else
            {
                entry.Date.ShouldNotBeNullOrWhiteSpace($"Non-recurring entry '{entry.Name}' must have Date.");
                Should.NotThrow(() => DateOnly.ParseExact(entry.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture));

                // Roadmap spec: shifting religious holidays must be flagged as needing periodic refresh,
                // not silently treated as permanent data (see HolidayCalendarEntry's class doc comment).
                entry.Note.ShouldNotBeNullOrWhiteSpace($"Non-recurring entry '{entry.Name}' must document why it needs periodic refresh.");
            }
        }
    }

    /// <summary>
    /// senseik-037: the religious-holiday (non-recurring) entries must be seeded at least 5 years ahead of the
    /// current one, so IHolidayCalendarLookup.GetLastDefinedReligiousHolidayYearAsync's "calendar needs a refresh"
    /// warning (see LeaveRequestPreview.BuildHolidayCalendarCoverageWarningAsync) does not fire for ordinary
    /// near-term leave planning. A hardcoded lower bound (2031) is used instead of "today + 5" so this test fails
    /// loudly, rather than silently drifting green, once that year is actually reached without a refresh.
    /// </summary>
    [Fact]
    public void SeedFile_ReligiousHolidays_AreSeededAtLeastFiveYearsAhead()
    {
        var path = FindSeedFilePath();
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<SeedFile>(json, JsonOptions)!;

        var byYear = file.Entries
            .Where(e => !e.RecurringAnnually)
            .GroupBy(e => DateOnly.ParseExact(e.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture).Year)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Name).ToList());

        const int minimumLastCoveredYear = 2031;
        byYear.Keys.Max().ShouldBeGreaterThanOrEqualTo(minimumLastCoveredYear);

        // Every year 2027..2031 must have both bayrams in full (Ramazan: arife + 3 days, Kurban: arife + 4 days) -
        // not just a gap-tolerant max.
        for (var year = 2027; year <= minimumLastCoveredYear; year++)
        {
            byYear.ShouldContainKey(year, $"Year {year} is missing religious holiday entries.");
            var names = byYear[year];
            names.Count(n => n.StartsWith("Ramazan Bayramı", StringComparison.Ordinal)).ShouldBe(4, $"Year {year}: Ramazan Bayramı entries.");
            names.Count(n => n.StartsWith("Kurban Bayramı", StringComparison.Ordinal)).ShouldBe(5, $"Year {year}: Kurban Bayramı entries.");
            names.Distinct().Count().ShouldBe(names.Count, $"Year {year} has duplicate holiday entries.");
        }
    }

    private static string FindSeedFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Sense.Api", "SeedData", "holidays", "tr-official-holidays.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate src/Sense.Api/SeedData/holidays/tr-official-holidays.json from test output path.");
    }

    private sealed record SeedFile(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("countryCode")] string CountryCode,
        [property: JsonPropertyName("entries")] IReadOnlyList<SeedEntry> Entries);

    private sealed record SeedEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("month")] int? Month,
        [property: JsonPropertyName("day")] int? Day,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("recurringAnnually")] bool RecurringAnnually,
        [property: JsonPropertyName("note")] string? Note);
}

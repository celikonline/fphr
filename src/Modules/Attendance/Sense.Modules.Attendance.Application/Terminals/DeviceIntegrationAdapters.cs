using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Sense.Modules.Attendance.Contracts;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Normalized event emitted by a vendor protocol adapter. Device-local timestamps are converted using the tenant's time zone.</summary>
public sealed record DeviceIntegrationEvent(string ExternalUserId, DateTime OccurredAtUtc, PunchDirectionDto? Direction, string? EventId);

/// <summary>Translates a vendor payload into the attendance module's provider-neutral event shape.</summary>
public interface ITerminalEventAdapter
{
    string Provider { get; }

    IReadOnlyList<DeviceIntegrationEvent> Parse(string payload, TimeZoneInfo deviceTimeZone);
}

/// <summary>
/// ZKTeco Attendance PUSH/ADMS ATTLOG body adapter. It accepts tab-delimited ATTLOG rows forwarded by a local gateway;
/// models that use a different PUSH firmware layout should get a separate adapter rather than guessed parsing.
/// </summary>
public sealed class ZktecoAdmsEventAdapter : ITerminalEventAdapter
{
    public string Provider => "zkteco";

    public IReadOnlyList<DeviceIntegrationEvent> Parse(string payload, TimeZoneInfo deviceTimeZone)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var events = new List<DeviceIntegrationEvent>();
        foreach (var rawLine in payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = rawLine.Split('\t', StringSplitOptions.TrimEntries);
            if (fields.Length > 0 && fields[0].Equals("ATTLOG", StringComparison.OrdinalIgnoreCase))
            {
                fields = fields[1..];
            }

            if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]))
            {
                throw new FormatException("Every ZKTeco ATTLOG row must contain user id, timestamp, and status.");
            }

            var direction = fields[2] switch
            {
                "0" or "4" => PunchDirectionDto.In,
                "1" or "5" => PunchDirectionDto.Out,
                _ => (PunchDirectionDto?)null,
            };
            events.Add(new DeviceIntegrationEvent(fields[0], ParseDeviceTime(fields[1], deviceTimeZone), direction, null));
        }

        return events;
    }

    internal static DateTime ParseDeviceTime(string value, TimeZoneInfo deviceTimeZone)
    {
        if (HasExplicitOffset(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offsetTime))
        {
            return offsetTime.UtcDateTime;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var localTime))
        {
            throw new FormatException("The device event timestamp is invalid.");
        }

        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (deviceTimeZone.IsInvalidTime(localTime))
        {
            throw new FormatException("The device event timestamp falls in a daylight-saving time gap.");
        }

        // Ambiguous fall-back times map deterministically to the earlier UTC instant.
        var offset = deviceTimeZone.IsAmbiguousTime(localTime)
            ? deviceTimeZone.GetAmbiguousTimeOffsets(localTime).Min()
            : deviceTimeZone.GetUtcOffset(localTime);
        return new DateTimeOffset(localTime, offset).UtcDateTime;
    }

    private static bool HasExplicitOffset(string value) =>
        value.EndsWith('Z') || value.EndsWith('z')
        || (value.Length >= 6 && (value[^6] is '+' or '-') && char.IsDigit(value[^5]) && value[^3] == ':');
}

/// <summary>Hikvision ISAPI AccessControllerEvent search response adapter.</summary>
public sealed class HikvisionIsapiEventAdapter : ITerminalEventAdapter
{
    public string Provider => "hikvision";

    public IReadOnlyList<DeviceIntegrationEvent> Parse(string payload, TimeZoneInfo deviceTimeZone)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var root = XElement.Parse(payload, LoadOptions.None);
        var records = root.DescendantsAndSelf().Where(e => e.Name.LocalName.Equals("AcsEvent", StringComparison.OrdinalIgnoreCase));
        var events = new List<DeviceIntegrationEvent>();
        foreach (var record in records)
        {
            var userId = Value(record, "employeeNoString", "employeeNo");
            var timestamp = Value(record, "time", "dateTime");
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(timestamp))
            {
                continue;
            }

            var status = Value(record, "attendanceStatus");
            var direction = status?.Trim().ToLowerInvariant() switch
            {
                "checkin" or "check_in" or "in" => PunchDirectionDto.In,
                "checkout" or "check_out" or "out" => PunchDirectionDto.Out,
                _ => (PunchDirectionDto?)null,
            };
            events.Add(new DeviceIntegrationEvent(
                userId, ZktecoAdmsEventAdapter.ParseDeviceTime(timestamp, deviceTimeZone), direction,
                Value(record, "serialNo", "eventId", "eventNo")));
        }

        if (events.Count == 0)
        {
            throw new FormatException("The Hikvision ISAPI response contains no recognizable access events.");
        }

        return events;
    }

    private static string? Value(XElement element, params string[] names) =>
        element.DescendantsAndSelf()
            .FirstOrDefault(e => names.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))?
            .Value.Trim();
}

/// <summary>Suprema BioStar 2 event or T&amp;A JSON response adapter.</summary>
public sealed class SupremaBioStarEventAdapter : ITerminalEventAdapter
{
    public string Provider => "suprema";

    public IReadOnlyList<DeviceIntegrationEvent> Parse(string payload, TimeZoneInfo deviceTimeZone)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var rows = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : FindRows(root).ToList();
        var events = new List<DeviceIntegrationEvent>();
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var userId = GetString(row, "user_id", "userId", "user_code", "userCode");
            var timestamp = GetString(row, "datetime", "event_time", "eventTime", "occurred_at", "occurredAt");
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(timestamp))
            {
                continue;
            }

            var status = GetString(row, "tna_key", "tnaKey", "attendance_status", "attendanceStatus", "direction");
            var direction = status?.Trim().ToLowerInvariant() switch
            {
                "in" or "checkin" or "check_in" => PunchDirectionDto.In,
                "out" or "checkout" or "check_out" => PunchDirectionDto.Out,
                _ => (PunchDirectionDto?)null,
            };
            events.Add(new DeviceIntegrationEvent(
                userId, ZktecoAdmsEventAdapter.ParseDeviceTime(timestamp, deviceTimeZone), direction,
                GetString(row, "id", "event_id", "eventId", "log_id", "logId")));
        }

        if (events.Count == 0)
        {
            throw new FormatException("The BioStar response contains no recognizable attendance events.");
        }

        return events;
    }

    private static IEnumerable<JsonElement> FindRows(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("events", StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals("records", StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals("logs", StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.EnumerateArray().ToArray();
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var nested = FindRows(property.Value).ToArray();
                    if (nested.Length > 0)
                    {
                        return nested;
                    }
                }
            }
        }

        return root.ValueKind == JsonValueKind.Object ? [root] : [];
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null,
                };
            }
        }

        return null;
    }
}

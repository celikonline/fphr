using System.Text.Json;

namespace Sense.Modules.Notifications.Application;

/// <summary>Serializes template arguments to/from the ArgsJson column.</summary>
public static class NotificationArgs
{
    public static string Serialize(IReadOnlyDictionary<string, string> args) => JsonSerializer.Serialize(args);

    public static IReadOnlyDictionary<string, string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}

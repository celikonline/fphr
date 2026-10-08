using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sense.Modules.Identity.Domain.Tenants;

/// <summary>Sales-managed entitlement. Persisted in the existing settings JSON; legacy tenants are unchanged.</summary>
public sealed record CommercialLicense(string Status, string Plan, IReadOnlyList<string> Modules, int EmployeeLimit, DateTime ExpiresAt, string Reference)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public bool IsAccessible(DateTime nowUtc) => Status is "trial" or "active" && ExpiresAt > nowUtc;

    public static CommercialLicense? Read(string settingsJson)
    {
        var root = JsonNode.Parse(settingsJson);
        return root?["commercialLicense"]?.Deserialize<CommercialLicense>(JsonOptions);
    }

    public string WriteTo(string settingsJson)
    {
        var root = JsonNode.Parse(settingsJson) as JsonObject ?? new JsonObject();
        root["commercialLicense"] = JsonSerializer.SerializeToNode(this, JsonOptions);
        return root.ToJsonString();
    }
}

namespace Sense.Api;

/// <summary>Host sabitleri (yollar, log alanları).</summary>
public static class HostConstants
{
    public const string ApplicationProperty = "Application";
    public const string ApplicationNameKey = "Application:Name";
    public const string DefaultApplicationName = "Sense.Api";
    public const string HealthPath = "/health";
    public const string HealthLivePath = "/health/live";
    public const string HealthReadyPath = "/health/ready";
    public const string HealthReadyTag = "ready";
    public const string OpenApiRoutePattern = "/openapi/{documentName}.json";
    public const string ScalarPath = "/scalar";
    public const string ApiTitle = "SenseİK API";
    public const string StartupFailedMessage = "Sense.Api failed to start";
}

using System.Globalization;
using System.Resources;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Contracts;

namespace Sense.Modules.Notifications.Infrastructure.Messaging;

/// <summary>
/// Renders templates from Resources/NotificationTemplates{,.en,.de}.resx (neutral = Turkish). Uses ResourceManager
/// directly instead of IStringLocalizer so the Worker host (no AddLocalization) renders the same way as the API.
/// </summary>
public sealed class ResourceNotificationTemplateRenderer : INotificationTemplateRenderer
{
    public const string TitleSuffix = ".title";
    public const string BodySuffix = ".body";
    public const string EmailGreetingKey = "email.greeting";
    public const string EmailOpenLinkKey = "email.open_link";
    public const string UrlArg = "Url";
    private const string RequestTypePrefix = "request_type.";
    private const string BaseName = "Sense.Modules.Notifications.Infrastructure.Resources.NotificationTemplates";

    private static readonly ResourceManager Resources = new(BaseName, typeof(ResourceNotificationTemplateRenderer).Assembly);

    public RenderedNotification Render(string templateKey, IReadOnlyDictionary<string, string> args, CultureInfo culture)
    {
        var title = GetString(templateKey + TitleSuffix, culture) ?? templateKey;
        var body = GetString(templateKey + BodySuffix, culture) ?? string.Empty;
        return new RenderedNotification(Apply(title, args, culture), Apply(body, args, culture));
    }

    public string RenderFragment(string key, IReadOnlyDictionary<string, string> args, CultureInfo culture) =>
        Apply(GetString(key, culture) ?? string.Empty, args, culture);

    private static string? GetString(string key, CultureInfo culture)
    {
        try
        {
            return Resources.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    private static string Apply(string template, IReadOnlyDictionary<string, string> args, CultureInfo culture)
    {
        var result = template;
        foreach (var (key, value) in args)
        {
            var display = key == NotificationTemplateArgs.RequestType
                ? GetString(RequestTypePrefix + value, culture) ?? value
                : value;
            result = result.Replace("{" + key + "}", display, StringComparison.Ordinal);
        }

        return result;
    }
}

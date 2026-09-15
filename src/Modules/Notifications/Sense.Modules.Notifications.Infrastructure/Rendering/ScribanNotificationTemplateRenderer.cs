using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Scriban;
using Scriban.Runtime;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;

namespace Sense.Modules.Notifications.Infrastructure.Rendering;

/// <summary>Frontend base URL (shared "Frontend" section) - turns app-relative action paths into e-mail links.</summary>
public sealed class NotificationsFrontendOptions
{
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Renders embedded Scriban templates "Templates/{key}.{channel}.{lang}.sbn". A template assigns its subject/title to the
/// <c>subject</c> variable and outputs the body: HTML for e-mail (wrapped in "_layout.email.{lang}.sbn"; the plain-text
/// part is derived from it), plain text for in-app. Globals: the model, <c>request_type_name</c> (from
/// "labels.{lang}.json"), <c>action_url</c>/<c>settings_url</c> (absolute), and <c>button label url</c>.
/// Templates must html.escape user-provided values in e-mail bodies.
/// </summary>
public sealed partial class ScribanNotificationTemplateRenderer : INotificationTemplateRenderer
{
    public const string SubjectVariable = "subject";
    public const string ContentVariable = "content";
    public const string RequestTypeNameVariable = "request_type_name";
    public const string ActionUrlVariable = "action_url";
    public const string SettingsUrlVariable = "settings_url";
    public const string ButtonFunction = "button";

    /// <summary>Self-service notification settings route (web/src/App.tsx).</summary>
    public const string SettingsPath = "/app/notifications/settings";

    public static readonly IReadOnlyList<string> SupportedLanguages = ["tr", "en", "de"];

    private const string ResourcePrefix = "Templates/";
    private const string LayoutKey = "_layout";
    private const string LabelsFile = "labels";
    private const string RequestTypesLabel = "request_types";

    private static readonly Assembly ResourceAssembly = typeof(ScribanNotificationTemplateRenderer).Assembly;

    private readonly ConcurrentDictionary<string, Template?> _templates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _requestTypeLabels = new(StringComparer.Ordinal);
    private readonly string _defaultLanguage;
    private readonly string _baseUrl;

    public ScribanNotificationTemplateRenderer(IOptions<NotificationsOptions> options, IOptions<NotificationsFrontendOptions> frontend)
    {
        _defaultLanguage = SupportedLanguages.Contains(options.Value.DefaultLanguage, StringComparer.Ordinal) ? options.Value.DefaultLanguage : SupportedLanguages[0];
        _baseUrl = frontend.Value.BaseUrl.TrimEnd('/');
    }

    public RenderedNotification Render(string templateKey, string channel, string language, IReadOnlyDictionary<string, object?> model)
    {
        var lang = ResolveLanguage(language);
        var template = LoadTemplate($"{templateKey}.{channel}", lang)
            ?? throw new InvalidOperationException($"Notification template '{templateKey}.{channel}' has no '{lang}' or '{_defaultLanguage}' version.");

        var globals = BuildGlobals(model, lang);
        var body = RenderWith(template, globals, lang).Trim();
        var subject = (globals.TryGetValue(SubjectVariable, out var s) ? s?.ToString() : null)?.Trim() ?? string.Empty;

        if (!string.Equals(channel, NotificationChannels.Email, StringComparison.Ordinal))
        {
            return new RenderedNotification(subject, body, null);
        }

        var layout = LoadTemplate($"{LayoutKey}.{NotificationChannels.Email}", lang);
        var html = body;
        if (layout is not null)
        {
            globals[ContentVariable] = body;
            html = RenderWith(layout, globals, lang);
        }

        return new RenderedNotification(subject, HtmlToText(body), html);
    }

    private string ResolveLanguage(string language)
    {
        var normalized = NotificationRecipientResolver.NormalizeLanguage(language);
        return normalized is not null && SupportedLanguages.Contains(normalized, StringComparer.Ordinal) ? normalized : _defaultLanguage;
    }

    private ScriptObject BuildGlobals(IReadOnlyDictionary<string, object?> model, string lang)
    {
        var globals = new ScriptObject(StringComparer.Ordinal);
        foreach (var (key, value) in model)
        {
            globals[key] = value;
        }

        if (model.GetValueOrDefault(WorkflowTemplateModel.RequestType) is string requestType)
        {
            globals[RequestTypeNameVariable] = RequestTypeLabels(lang).GetValueOrDefault(requestType)
                ?? RequestTypeLabels(_defaultLanguage).GetValueOrDefault(requestType)
                ?? requestType;
        }

        if (model.GetValueOrDefault(NotificationPublisher.ActionPathKey) is string { Length: > 0 } actionPath)
        {
            globals[ActionUrlVariable] = _baseUrl + actionPath;
        }

        globals[SettingsUrlVariable] = _baseUrl + SettingsPath;
        globals.Import(ButtonFunction, new Func<string, string, string>(Button));
        return globals;
    }

    private static string RenderWith(Template template, ScriptObject globals, string lang)
    {
        var context = new TemplateContext { StrictVariables = false, EnableRelaxedMemberAccess = true };
        context.PushCulture(CultureInfo.GetCultureInfo(lang));
        context.PushGlobal(globals);
        return template.Render(context);
    }

    private Template? LoadTemplate(string name, string lang) =>
        _templates.GetOrAdd($"{name}.{lang}", _ => Parse($"{name}.{lang}.sbn"))
        ?? (lang == _defaultLanguage ? null : _templates.GetOrAdd($"{name}.{_defaultLanguage}", _ => Parse($"{name}.{_defaultLanguage}.sbn")));

    private static Template? Parse(string fileName)
    {
        var text = ReadResource(fileName);
        if (text is null)
        {
            return null;
        }

        var template = Template.Parse(text, fileName);
        if (template.HasErrors)
        {
            throw new InvalidOperationException($"Notification template '{fileName}' is invalid: {string.Join("; ", template.Messages)}");
        }

        return template;
    }

    private IReadOnlyDictionary<string, string> RequestTypeLabels(string lang) =>
        _requestTypeLabels.GetOrAdd(lang, l =>
        {
            var json = ReadResource($"{LabelsFile}.{l}.json");
            if (json is null)
            {
                return new Dictionary<string, string>();
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(RequestTypesLabel, out var types)
                ? types.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name, StringComparer.Ordinal)
                : new Dictionary<string, string>();
        });

    private static string? ReadResource(string fileName)
    {
        using var stream = ResourceAssembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Button(string label, string url) =>
        $"<p><a href=\"{WebUtility.HtmlEncode(url)}\" style=\"display:inline-block;padding:10px 18px;background:#4f46e5;color:#ffffff;text-decoration:none;border-radius:6px\">{WebUtility.HtmlEncode(label)}</a></p>";

    /// <summary>Plain-text alternative: links become "label: url", block ends become line breaks, tags dropped, entities decoded.</summary>
    public static string HtmlToText(string html)
    {
        var text = AnchorRegex().Replace(html, m => $"{StripTags(m.Groups["label"].Value)}: {WebUtility.HtmlDecode(m.Groups["href"].Value)}");
        text = BreakRegex().Replace(text, "\n");
        text = BlockEndRegex().Replace(text, "\n\n");
        text = WebUtility.HtmlDecode(StripTags(text));
        text = SpacesRegex().Replace(text, " ");
        text = BlankLinesRegex().Replace(text, "\n\n");
        return string.Join('\n', text.Split('\n').Select(l => l.Trim())).Trim();
    }

    private static string StripTags(string value) => TagRegex().Replace(value, string.Empty);

    [GeneratedRegex("<a\\s[^>]*href=\"(?<href>[^\"]*)\"[^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex("</(p|div|h[1-6]|li|tr)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("[ \\t\\r]+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex BlankLinesRegex();
}

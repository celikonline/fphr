using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace Sense.Modules.Notifications.Application.HrMessages;

/// <summary>Sanitizes rich HR message HTML before it is persisted or sent.</summary>
public static class HrMessageContentSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = Build();

    public static string Sanitize(string html) => Sanitizer.Sanitize(html);

    /// <summary>
    /// Length of the text a reader sees once the body is sanitized: tags and attributes (href/src/alt) are dropped,
    /// entities are decoded (&amp;amp; = 1, &amp;nbsp; = 1), anchor text counts, images count 0, whitespace counts.
    /// </summary>
    public static int VisibleTextLength(string html)
    {
        var document = new HtmlParser().ParseDocument(Sanitize(html));
        return document.Body?.TextContent.Length ?? 0;
    }

    private static HtmlSanitizer Build()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "strike", "blockquote", "h1", "h2", "h3", "ul", "ol", "li", "a", "img" })
            sanitizer.AllowedTags.Add(tag);

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[] { "href", "src", "alt", "title", "target", "rel" })
            sanitizer.AllowedAttributes.Add(attribute);

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("data");
        sanitizer.AllowDataAttributes = false;
        return sanitizer;
    }
}

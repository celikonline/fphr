using Ganss.Xss;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>
/// Allowlist-sanitizes a duyuru's rich-text (HTML) content before it is ever persisted (React security rule: sanitize
/// user-derived HTML at the write boundary, since this command is the only path content ever enters through - the
/// feed, the manage panel and any future consumer all read back already-sanitized content). Strips <c>&lt;script&gt;</c>,
/// event handlers (<c>onclick</c> etc.), <c>javascript:</c>/<c>data:</c> URLs and any tag/attribute outside the
/// allowlist below, while keeping the formatting a rich-text editor (Tiptap/Word paste) actually produces.
/// </summary>
public static class AnnouncementContentSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = Build();

    public static string Sanitize(string html) => Sanitizer.Sanitize(html);

    private static HtmlSanitizer Build()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "p", "br", "strong", "b", "em", "i", "u", "s", "strike", "blockquote", "h1", "h2", "h3", "h4",
                     "ul", "ol", "li", "a", "img", "table", "thead", "tbody", "tr", "td", "th", "span", "code", "pre",
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[] { "href", "src", "alt", "title", "data-attachment-id", "target", "rel" })
        {
            sanitizer.AllowedAttributes.Add(attribute);
        }

        // Word/paste cleanup leaves inline "text-align"/"font-weight" styles Tiptap's TextAlign extension relies on;
        // anything more (position, expression(), behavior) is exactly what HtmlSanitizer's CSS parser already refuses.
        sanitizer.AllowedCssProperties.Add("text-align");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");

        // Inline images are never embedded as data: URIs (they are separate AnnouncementAttachment rows, referenced
        // by data-attachment-id and resolved to a fresh, permission-checked URL at render time - see AnnouncementsLimits.ContentMaxLength's doc).
        sanitizer.AllowDataAttributes = true;
        return sanitizer;
    }
}

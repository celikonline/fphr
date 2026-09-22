using System.Net;
using System.Text.RegularExpressions;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Plain-text preview of an announcement's (already sanitized) HTML for e-mails and notifications.</summary>
public static partial class AnnouncementExcerpt
{
    public const int MaxLength = 400;

    public static string FromHtml(string html)
    {
        var withBreaks = BlockEndRegex().Replace(html, " ");
        var text = WebUtility.HtmlDecode(TagRegex().Replace(withBreaks, string.Empty));
        var collapsed = WhitespaceRegex().Replace(text, " ").Trim();
        return collapsed.Length <= MaxLength ? collapsed : string.Concat(collapsed.AsSpan(0, MaxLength).TrimEnd(), "…");
    }

    [GeneratedRegex(@"</(p|li|h[1-4]|tr|blockquote)>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

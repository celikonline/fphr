using System.Text;
using Sense.Modules.Announcements.Domain.Attachments;

namespace Sense.Modules.Announcements.Application.Attachments;

/// <summary>
/// Upload safety policy for duyuru attachments (own small copy of Documents' <c>UploadFilePolicy</c> - modules only
/// share code through <c>*.Contracts</c>, and this one is scoped to what an announcement actually needs: images for
/// inline editor pictures, common office/PDF formats for downloadable attachments). Extension allowlist (the LAST
/// extension decides), a matching content-type check, and file-name sanitisation.
/// </summary>
public static class AnnouncementUploadPolicy
{
    private const string OctetStream = "application/octet-stream";

    private static readonly Dictionary<string, string[]> ImageTypesByExtension = new(StringComparer.Ordinal)
    {
        ["jpg"] = ["image/jpeg", "image/pjpeg"],
        ["jpeg"] = ["image/jpeg", "image/pjpeg"],
        ["png"] = ["image/png"],
        ["gif"] = ["image/gif"],
        ["webp"] = ["image/webp"],
    };

    private static readonly Dictionary<string, string[]> FileTypesByExtension = new(StringComparer.Ordinal)
    {
        ["pdf"] = ["application/pdf"],
        ["doc"] = ["application/msword"],
        ["docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/msword"],
        ["xls"] = ["application/vnd.ms-excel"],
        ["xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-excel"],
        ["ppt"] = ["application/vnd.ms-powerpoint"],
        ["pptx"] = ["application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/vnd.ms-powerpoint"],
        ["txt"] = ["text/plain"],
    };

    /// <summary>Returns the file name with directory parts, <c>..</c>, control characters and illegal filename characters removed; empty when nothing usable is left.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var lastSeparator = fileName.LastIndexOfAny(['/', '\\']);
        var name = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;

        var cleaned = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (!char.IsControl(ch) && "<>:\"|?*".IndexOf(ch) < 0)
            {
                cleaned.Append(ch);
            }
        }

        var result = cleaned.ToString();
        while (result.Contains("..", StringComparison.Ordinal))
        {
            result = result.Replace("..", ".", StringComparison.Ordinal);
        }

        return result.Trim().Trim('.').Trim();
    }

    private static string? ExtensionOf(string? fileName)
    {
        var name = SanitizeFileName(fileName);
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..].ToLowerInvariant() : null;
    }

    /// <summary>True when the extension/content-type pair is allowed for <paramref name="kind"/> (images only for Inline; images + office/PDF for File).</summary>
    public static bool IsAllowed(string? fileName, string? contentType, AnnouncementAttachmentKind kind)
    {
        if (ExtensionOf(fileName) is not { } extension)
        {
            return false;
        }

        var allowedTypes = kind == AnnouncementAttachmentKind.Inline
            ? ImageTypesByExtension.GetValueOrDefault(extension)
            : ImageTypesByExtension.TryGetValue(extension, out var image) ? image : FileTypesByExtension.GetValueOrDefault(extension);
        if (allowedTypes is null)
        {
            return false;
        }

        var mediaType = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return mediaType == OctetStream || allowedTypes.Contains(mediaType);
    }
}

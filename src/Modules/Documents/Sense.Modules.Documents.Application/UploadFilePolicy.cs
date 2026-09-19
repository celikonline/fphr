using System.Text;
using FluentValidation;

namespace Sense.Modules.Documents.Application;

/// <summary>
/// Upload safety policy shared by every presigned-upload entry point (documents, self-service documents, shared
/// company files): an extension allowlist (the LAST extension decides, so <c>a.pdf.exe</c> is an .exe), a matching
/// content-type check (so <c>text/html</c> cannot ride on a .pdf name) and file-name sanitisation (no path
/// separators, <c>..</c> or control characters ever reach the storage key or the stored title).
/// </summary>
public static class UploadFilePolicy
{
    private const string OctetStream = "application/octet-stream";

    /// <summary>Allowed extension (lower-case, no dot) to the content types a browser may legitimately send for it.</summary>
    private static readonly Dictionary<string, string[]> AllowedTypesByExtension = new(StringComparer.Ordinal)
    {
        ["pdf"] = ["application/pdf"],
        ["jpg"] = ["image/jpeg", "image/pjpeg"],
        ["jpeg"] = ["image/jpeg", "image/pjpeg"],
        ["png"] = ["image/png"],
        ["gif"] = ["image/gif"],
        ["webp"] = ["image/webp"],
        ["heic"] = ["image/heic", "image/heif"],
        ["doc"] = ["application/msword"],
        ["docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/msword"],
        ["xls"] = ["application/vnd.ms-excel"],
        ["xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-excel"],
        ["ppt"] = ["application/vnd.ms-powerpoint"],
        ["pptx"] = ["application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/vnd.ms-powerpoint"],
        ["txt"] = ["text/plain"],
        // Windows browsers report .csv as application/vnd.ms-excel when Excel is installed.
        ["csv"] = ["text/csv", "application/csv", "text/plain", "application/vnd.ms-excel"],
        ["odt"] = ["application/vnd.oasis.opendocument.text"],
        ["ods"] = ["application/vnd.oasis.opendocument.spreadsheet"],
    };

    /// <summary>
    /// Returns the file name with directory parts, <c>..</c>, control characters and characters that are illegal in
    /// file names removed; empty when nothing usable is left.
    /// </summary>
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

    /// <summary>The lower-case extension of the sanitised name without the dot; null when there is none.</summary>
    public static string? ExtensionOf(string? fileName)
    {
        var name = SanitizeFileName(fileName);
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..].ToLowerInvariant() : null;
    }

    /// <summary>True when the extension is allowed and the declared content type is one of its expected types (or a generic octet-stream).</summary>
    public static bool IsAllowed(string? fileName, string? contentType)
    {
        if (ExtensionOf(fileName) is not { } extension || !AllowedTypesByExtension.TryGetValue(extension, out var allowedTypes))
        {
            return false;
        }

        var mediaType = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return mediaType == OctetStream || allowedTypes.Contains(mediaType);
    }

    /// <summary>Adds the file-name and file-type rules to an upload-initiation validator.</summary>
    public static void ApplyRules<T>(AbstractValidator<T> validator, Func<T, string?> fileName, Func<T, string?> contentType)
    {
        validator.RuleFor(x => fileName(x))
            .Must(name => SanitizeFileName(name).Length > 0)
            .WithMessage(DocumentsErrors.FileNameInvalid)
            .OverridePropertyName("FileName");
        validator.RuleFor(x => x)
            .Must(x => SanitizeFileName(fileName(x)).Length == 0 || IsAllowed(fileName(x), contentType(x)))
            .WithMessage(DocumentsErrors.FileTypeNotAllowed)
            .OverridePropertyName("FileName");
    }
}

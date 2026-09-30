using System.Text;
using Sense.Modules.Employee.Domain.Employees;

namespace Sense.Modules.Employee.Application.Notes;

/// <summary>Allowed note attachment types: extension and declared content type must agree (same idea as Announcements' upload policy).</summary>
public static class EmployeeNoteUploadPolicy
{
    private static readonly Dictionary<string, string[]> TypesByExtension = new(StringComparer.Ordinal)
    {
        ["pdf"] = ["application/pdf"],
        ["png"] = ["image/png"],
        ["jpg"] = ["image/jpeg"],
        ["jpeg"] = ["image/jpeg"],
        ["txt"] = ["text/plain"],
        ["docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        ["xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
    };

    /// <summary>Base name only: directory parts, control and illegal filename characters and ".." removed; empty when nothing usable is left.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var separator = fileName.LastIndexOfAny(['/', '\\']);
        var name = separator >= 0 ? fileName[(separator + 1)..] : fileName;
        var cleaned = new StringBuilder(name.Length);
        foreach (var ch in name.Where(ch => !char.IsControl(ch) && "<>:\"|?*".IndexOf(ch) < 0))
        {
            cleaned.Append(ch);
        }

        var result = cleaned.ToString();
        while (result.Contains("..", StringComparison.Ordinal))
        {
            result = result.Replace("..", ".", StringComparison.Ordinal);
        }

        result = result.Trim().Trim('.').Trim();
        return result.Length > EmployeeNoteLimits.FileNameMaxLength ? result[..EmployeeNoteLimits.FileNameMaxLength] : result;
    }

    public static bool IsAllowed(string? fileName, string? contentType)
    {
        var name = SanitizeFileName(fileName);
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1 || string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var declared = contentType.Split(';')[0].Trim();
        return TypesByExtension.TryGetValue(name[(dot + 1)..].ToLowerInvariant(), out var types)
            && types.Contains(declared, StringComparer.OrdinalIgnoreCase);
    }
}

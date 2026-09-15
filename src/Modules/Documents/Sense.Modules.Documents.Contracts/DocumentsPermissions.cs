using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Documents.Contracts;

/// <summary>Documents module permission definitions.</summary>
public static class DocumentsPermissions
{
    private const string Module = "documents";

    /// <summary>Create/upload a file document.</summary>
    public static readonly Permission FilesCreate = new(
        "documents.files.create",
        Module,
        "Create Document",
        "Documents",
        "Upload a new file document");

    /// <summary>Read/download a file document.</summary>
    public static readonly Permission FilesRead = new(
        "documents.files.read",
        Module,
        "Read Document",
        "Documents",
        "View and download file documents");

    /// <summary>Delete a file document.</summary>
    public static readonly Permission FilesDelete = new(
        "documents.files.delete",
        Module,
        "Delete Document",
        "Documents",
        "Delete file documents");

    /// <summary>All Documents module permissions.</summary>
    public static IEnumerable<Permission> All =>
    [
        FilesCreate,
        FilesRead,
        FilesDelete,
    ];
}

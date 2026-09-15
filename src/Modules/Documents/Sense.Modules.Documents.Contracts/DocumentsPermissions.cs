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

    /// <summary>View the tenant's personnel document type catalog.</summary>
    public static readonly Permission DocumentTypesView = new(
        "documents.document_types.view",
        Module,
        "View Document Types",
        "Documents",
        "View the personnel document type catalog");

    /// <summary>Create/edit/activate/deactivate personnel document types.</summary>
    public static readonly Permission DocumentTypesManage = new(
        "documents.document_types.manage",
        Module,
        "Manage Document Types",
        "Documents",
        "Create, edit and (de)activate personnel document types");

    /// <summary>View employees' document requirement status.</summary>
    public static readonly Permission RequirementsView = new(
        "documents.requirements.view",
        Module,
        "View Document Requirements",
        "Documents",
        "View employees' personnel document requirement status");

    /// <summary>Waive/clear-waive/link/reevaluate employees' document requirements.</summary>
    public static readonly Permission RequirementsManage = new(
        "documents.requirements.manage",
        Module,
        "Manage Document Requirements",
        "Documents",
        "Waive, clear, link and reevaluate employees' personnel document requirements");

    /// <summary>Self-service: view the caller's own document requirement status.</summary>
    public static readonly Permission MyRequirements = new(
        "documents.my.requirements",
        Module,
        "My Document Requirements",
        "Documents",
        "View the caller's own personnel document requirement status");

    /// <summary>All Documents module permissions.</summary>
    public static IEnumerable<Permission> All =>
    [
        FilesCreate,
        FilesRead,
        FilesDelete,
        DocumentTypesView,
        DocumentTypesManage,
        RequirementsView,
        RequirementsManage,
        MyRequirements,
    ];
}

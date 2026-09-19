namespace Sense.Modules.Documents.Application;

/// <summary>Application layer error codes (localized in Web layer).</summary>
public static class DocumentsErrors
{
    public const string DocumentNotFound = "document.not_found";
    public const string UploadInitiationFailed = "documents.upload_initiation_failed";
    public const string ConfirmationFailed = "documents.confirmation_failed";
    public const string DeletionFailed = "documents.deletion_failed";
    public const string TenantNotAuthorized = "document.tenant_not_authorized";

    /// <summary>Uniqueness check requires a DB round-trip, so it lives here rather than in the Domain (mirrors Employee's EmployeeErrors.CustomFieldKeyTaken).</summary>
    public const string DocumentTypeCodeTaken = "document_type.code_taken";

    public const string NoEmployeeForUser = "documents.employee.not_linked";

    /// <summary>Çağıranın "documents.requirements.view" veri kapsamı bu çalışanı kapsamıyor.</summary>
    public const string EmployeeNotInScope = "documents.employee.not_in_scope";

    /// <summary>Self-service write attempted against a requirement that belongs to a different employee.</summary>
    public const string NotOwner = "employee_document_requirement.not_owner";
}

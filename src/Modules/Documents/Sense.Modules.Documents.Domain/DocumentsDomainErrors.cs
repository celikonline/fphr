namespace Sense.Modules.Documents.Domain;

/// <summary>Domain error codes for Documents module.</summary>
public static class DocumentsDomainErrors
{
    public const string DocumentNotFound = "document.not_found";
    public const string DocumentTitleRequired = "document.title_required";
    public const string DocumentFileKeyRequired = "document.filekey_required";
    public const string DocumentInvalidSize = "document.invalid_size";
    public const string DocumentUploaderRequired = "document.uploader_required";
    public const string DocumentInvalidStatus = "document.invalid_status";
    public const string DocumentAlreadyDeleted = "document.already_deleted";
    public const string TenantNotAuthorized = "document.tenant_not_authorized";

    // Document type catalog (11.7: "zorunlu ve koşullu özlük belgesi kuralları")
    public const string DocumentTypeNotFound = "document_type.not_found";
    public const string DocumentTypeCodeRequired = "document_type.code_required";
    public const string DocumentTypeCodeTooLong = "document_type.code_too_long";
    public const string DocumentTypeNameRequired = "document_type.name_required";
    public const string DocumentTypeConditionRequiredForConditional = "document_type.condition_required_for_conditional";
    public const string DocumentTypeConditionNotAllowed = "document_type.condition_not_allowed";
    public const string DocumentTypeInvalidQuantity = "document_type.invalid_quantity";
    public const string DocumentTypeInvalidRenewalPeriod = "document_type.invalid_renewal_period";
    public const string DocumentTypeInvalidRetention = "document_type.invalid_retention";

    /// <summary>Reserved for a future write path that could attempt to change an immutable field (Code/Stage/Condition/IsSystem) of a system-seeded row; Update() does not expose those fields today, so nothing currently raises it.</summary>
    public const string DocumentTypeSystemImmutable = "document_type.system_immutable";

    public const string EmployeeDocumentRequirementNotFound = "employee_document_requirement.not_found";
    public const string EmployeeDocumentRequirementWaiveReasonRequired = "employee_document_requirement.waive_reason_required";
}

/// <summary>Domain constants and limits.</summary>
public static class DocumentsLimits
{
    public const int TitleMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const long MaxFileSize = 100 * 1024 * 1024; // 100 MB

    public const int DocumentTypeCodeMaxLength = 100;
    public const int DocumentTypeNameMaxLength = 255;
    public const int WaivedReasonMaxLength = 500;

    /// <summary>Default "expiring soon" window (days) for <c>GET /api/v1/documents/expiring</c> when the caller
    /// omits or sends a non-positive <c>withinDays</c>.</summary>
    public const int DefaultExpiryReportWindowDays = 30;
}

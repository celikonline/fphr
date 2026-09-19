namespace Sense.Modules.Documents.Domain.VisaDocuments;

/// <summary>Domain error codes for Visa Documents feature.</summary>
public static class VisaDomainErrors
{
    public const string VisaDocumentProcessNotFound = "visa_document_process.not_found";
    public const string VisaDocumentProcessNameRequired = "visa_document_process.name_required";
    public const string VisaDocumentProcessNameTooLong = "visa_document_process.name_too_long";
    public const string VisaDocumentProcessAlreadyExists = "visa_document_process.already_exists";

    public const string VisaDocumentRequestNotFound = "visa_document_request.not_found";
    public const string VisaDocumentRequestInvalidTravelDates = "visa_document_request.invalid_travel_dates";
    public const string VisaDocumentRequestCountryCodeRequired = "visa_document_request.country_code_required";
    public const string VisaDocumentRequestVisaTypeRequired = "visa_document_request.visa_type_required";
    public const string VisaDocumentRequestAlreadySubmitted = "visa_document_request.already_submitted";
    public const string VisaDocumentRequestApproverNotConfigured = "visa_document_request.approver_not_configured";
    public const string VisaDocumentRequestInvalidStatusTransition = "visa_document_request.invalid_status_transition";
    public const string VisaDocumentRequestInvalidVisaType = "visa_document_request.invalid_visa_type";
    public const string VisaDocumentRequestInvalidCountryCode = "visa_document_request.invalid_country_code";
    public const string VisaDocumentRequestProcessUnavailable = "visa_document_request.process_unavailable";
    public const string VisaDocumentRequestNoEmployeeForUser = "visa_document_request.no_employee_for_user";

    /// <summary>Copying a configured template document's bytes into the employee's folder failed
    /// (<c>IVisaDocumentDeliveryService</c>/<c>IFileStorage.CopyObjectAsync</c>).</summary>
    public const string VisaDocumentDeliveryFailed = "visa_document_request.delivery_failed";
}

/// <summary>Seed-defined visa type lookup (handoff GATE 1 assumption 1). Stored on the request as the lowercase code;
/// the web maps each code to a translated label.</summary>
public static class VisaTypes
{
    public const string Tourist = "tourist";
    public const string Business = "business";
    public const string Work = "work";
    public const string Student = "student";
    public const string Transit = "transit";

    public static readonly IReadOnlyList<string> All = [Tourist, Business, Work, Student, Transit];

    public static bool IsKnown(string? visaType) => visaType is not null && All.Contains(visaType, StringComparer.Ordinal);
}

/// <summary>Domain constants and limits for Visa Documents.</summary>
public static class VisaLimits
{
    public const int ProcessNameMaxLength = 255;
    public const int CountryCodeMinLength = 2;
    public const int CountryCodeMaxLength = 3; // ISO 3166-1 alpha-2/alpha-3
    public const int VisaTypeMaxLength = 50;
    public const int RejectionReasonMaxLength = 500;
    public const int DescriptionMaxLength = 2000;
}

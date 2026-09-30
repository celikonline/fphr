namespace Sense.Modules.Employee.Application;

/// <summary>Application layer error codes (localized in the Web layer via SharedResource).</summary>
public static class EmployeeErrors
{
    public const string EmployeeNotFound = "employee.not_found";
    public const string OrgUnitNotFound = "employee.org_unit_not_found";
    public const string ManagerNotFound = "employee.manager_not_found";
    public const string MentorNotFound = "employee.mentor_not_found";
    public const string NationalIdTaken = "employee.national_id_taken";
    public const string SicilNumarasiTaken = "employee.sicil_numarasi_taken";

    /// <summary>Another non-terminated employee of the tenant already uses this work e-mail (case-insensitive).</summary>
    public const string WorkEmailTaken = "employee.work_email_taken";
    public const string StatusInvalid = "employee.status_invalid";
    public const string DaysOutOfRange = "employee.days_out_of_range";

    /// <summary>Target org unit is outside the units the caller may manage (e.g. a unit manager adding to another unit).</summary>
    public const string OrgUnitOutOfScope = "employee.org_unit_out_of_scope";

    public const string CustomFieldDefinitionNotFound = "employee.custom_field.not_found";
    public const string CustomFieldKeyTaken = "employee.custom_field.key_taken";

    /// <summary>A self-service request tried to touch a definition that is not IsEmployeeEditable.</summary>
    public const string CustomFieldNotEmployeeEditable = "employee.custom_field.not_employee_editable";

    /// <summary>Self-servis profil güncellemesi tam-değiştirme semantiğindedir; hiçbir alan gönderilmemiş (boş gövde) istek mevcut iletişim verisini silmesin diye reddedilir.</summary>
    public const string ProfileUpdateEmpty = "employee.profile.update_empty";

    public const string CardPdfSectionsRequired = "employee.card_pdf.sections_required";
    public const string CardPdfUnknownSection = "employee.card_pdf.unknown_section";
    public const string CardPdfPeriodInvalid = "employee.card_pdf.period_invalid";
    public const string CardPdfNoSections = "employee.card_pdf.no_sections";
}

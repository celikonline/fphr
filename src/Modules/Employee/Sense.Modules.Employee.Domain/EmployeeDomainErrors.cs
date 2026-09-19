namespace Sense.Modules.Employee.Domain;

/// <summary>Domain error codes for Employee module.</summary>
public static class EmployeeDomainErrors
{
    public const string EmployeeNotFound = "employee.not_found";
    public const string FirstNameRequired = "employee.first_name_required";
    public const string LastNameRequired = "employee.last_name_required";
    public const string NameTooLong = "employee.name_too_long";
    public const string NationalIdInvalid = "employee.national_id_invalid";
    public const string WorkEmailInvalid = "employee.work_email_invalid";
    public const string PersonalEmailInvalid = "employee.personal_email_invalid";
    public const string WorkPhoneTooLong = "employee.work_phone_too_long";
    public const string ManagerSelf = "employee.manager_self";
    public const string ManagerCycle = "employee.manager_cycle";
    public const string ChildCountInvalid = "employee.child_count_invalid";
    public const string MobilePhoneTooLong = "employee.mobile_phone_too_long";
    public const string HomePhoneTooLong = "employee.home_phone_too_long";
    public const string AddressLineTooLong = "employee.address_line_too_long";
    public const string CityTooLong = "employee.city_too_long";
    public const string PostalCodeTooLong = "employee.postal_code_too_long";
    public const string SuspendInvalidStatus = "employee.suspend_invalid_status";
    public const string ReactivateNotSuspended = "employee.reactivate_not_suspended";

    /// <summary>Terminate is only allowed from Active/Suspended (not Draft, not already Terminated).</summary>
    public const string TerminateInvalidStatus = "employee.terminate_invalid_status";

    public const string TerminationDateBeforeHireDate = "employee.termination_date_before_hire_date";

    /// <summary>Rehire is only allowed on a Terminated employee.</summary>
    public const string RehireInvalidStatus = "employee.rehire_invalid_status";

    /// <summary>The new hire date must be after the previous period's exit date.</summary>
    public const string RehireDateBeforePreviousExit = "employee.rehire_date_before_previous_exit";

    public const string SubcontractorNotFound = "employee.subcontractor_not_found";
    public const string SubcontractorNameRequired = "employee.subcontractor_name_required";
    public const string SubcontractorNameTooLong = "employee.subcontractor_name_too_long";
    public const string SubcontractorTaxNumberTooLong = "employee.subcontractor_tax_number_too_long";
    public const string SubcontractorContactEmailInvalid = "employee.subcontractor_contact_email_invalid";
    public const string SubcontractorContractEndBeforeStart = "employee.subcontractor_contract_end_before_start";

    /// <summary>An employee can only be linked to an active subcontractor.</summary>
    public const string SubcontractorInactive = "employee.subcontractor_inactive";

    public const string UnionMembershipNotFound = "employee.union_membership_not_found";
    public const string UnionNameRequired = "employee.union_name_required";
    public const string UnionBusinessLineCodeTooLong = "employee.union_business_line_code_too_long";
    public const string UnionEndDateBeforeMembership = "employee.union_end_date_before_membership";
    public const string UnionOfficerSinceBeforeMembership = "employee.union_officer_since_before_membership";

    /// <summary>An employee belongs to one union at a time: end the open membership first.</summary>
    public const string UnionMembershipAlreadyOpen = "employee.union_membership_already_open";
    public const string TerminationReasonNotFound = "employee.termination_reason_not_found";
    public const string TerminationReasonCodeRequired = "employee.termination_reason_code_required";
    public const string TerminationReasonCodeTooLong = "employee.termination_reason_code_too_long";

    /// <summary>Tenant-unique per <see cref="Terminations.TerminationReason"/>'s Code uniqueness rule (checked by a future CRUD command; not used by this pass's system seed).</summary>
    public const string TerminationReasonCodeTaken = "employee.termination_reason_code_taken";

    public const string TerminationReasonNameRequired = "employee.termination_reason_name_required";
    public const string TerminationReasonNameTooLong = "employee.termination_reason_name_too_long";

    /// <summary>The (TerminationType, SgkExitCode) pair does not match SGK's family for that type - see <see cref="Terminations.SgkExitCodeRules"/>.</summary>
    public const string TerminationSgkExitCodeInconsistent = "employee.termination_sgk_exit_code_inconsistent";

    public const string CareerRecordNotFound = "employee.career_record_not_found";

    /// <summary>A new/edited record must start after the previous record's ValidFrom.</summary>
    public const string CareerValidFromInvalid = "employee.career_valid_from_invalid";

    /// <summary>PositionStartDate &gt;= HireDate (11.3.03).</summary>
    public const string CareerBeforeHireDate = "employee.career_before_hire_date";

    /// <summary>Two assignments for the same hierarchy level in one record.</summary>
    public const string CareerDuplicateLevel = "employee.career_duplicate_level";

    public const string CareerAssignmentInvalid = "employee.career_assignment_invalid";

    /// <summary>Only the latest record may move its start date or be deleted.</summary>
    public const string CareerNotLatest = "employee.career_not_latest";

    /// <summary>The only career record cannot be deleted (edit it instead).</summary>
    public const string CareerLastRecord = "employee.career_last_record";

    public const string CareerNoteTooLong = "employee.career_note_too_long";

    public const string SalaryRecordNotFound = "employee.salary_record_not_found";

    /// <summary>One salary record per effective date per employee.</summary>
    public const string SalaryEffectiveDateDuplicate = "employee.salary_effective_date_duplicate";

    /// <summary>"Maaş bloğu bütünlüğü": nothing of the block is saved without an effective date.</summary>
    public const string SalaryEffectiveDateRequired = "employee.salary_effective_date_required";

    /// <summary>"Maaş bloğu bütünlüğü": nothing of the block is saved without an amount.</summary>
    public const string SalaryAmountRequired = "employee.salary_amount_required";

    /// <summary>Amount must be &gt; 0, at most 2 decimals and below <see cref="EmployeeLimits.MoneyMax"/>.</summary>
    public const string SalaryAmountInvalid = "employee.salary_amount_invalid";

    /// <summary>Allowances must be &gt;= 0, at most 2 decimals and below <see cref="EmployeeLimits.MoneyMax"/>.</summary>
    public const string SalaryAllowanceInvalid = "employee.salary_allowance_invalid";

    public const string SalaryTypeRequired = "employee.salary_type_required";
    public const string SalaryPayFrequencyRequired = "employee.salary_pay_frequency_required";
    public const string SalaryNoteTooLong = "employee.salary_note_too_long";

    /// <summary>Currency must be an active ISO 4217 code (3 uppercase letters).</summary>
    public const string CurrencyInvalid = "employee.currency_invalid";

    public const string CurrencyRequired = "employee.currency_required";

    public const string BankAccountNotFound = "employee.bank_account_not_found";
    public const string IbanRequired = "employee.iban_required";

    /// <summary>Not a well-formed IBAN (length/characters/mod-97 checksum).</summary>
    public const string IbanInvalid = "employee.iban_invalid";

    /// <summary>A valid IBAN of a country bank accounts do not accept yet (TR only for now).</summary>
    public const string IbanCountryUnsupported = "employee.iban_country_unsupported";

    /// <summary>The employee already has an account with this IBAN.</summary>
    public const string IbanDuplicate = "employee.iban_duplicate";

    public const string BankAccountTypeRequired = "employee.bank_account_type_required";
    public const string BankNameTooLong = "employee.bank_name_too_long";
    public const string BankAccountNoteTooLong = "employee.bank_account_note_too_long";

    public const string SicilNumarasiTooLong = "employee.sicil_numarasi_too_long";

    /// <summary>Türk uyruklu bir çalışana 99 ile başlayan (yabancı formatlı) kimlik numarası girilemez.</summary>
    public const string NationalIdForeignPrefixForTurkish = "employee.national_id_foreign_prefix_for_turkish";

    /// <summary>Yabancı uyruklu bir çalışana NationalId verilirse, 99 ile başlayan yabancı kimlik formatında olmalı.</summary>
    public const string NationalIdNotForeignForForeignNational = "employee.national_id_not_foreign_for_foreign_national";

    /// <summary>Yabancı uyruklu bir çalışan için Yabancı Kimlik No veya Pasaport No alanlarından en az biri zorunludur.</summary>
    public const string ForeignIdentificationRequired = "employee.foreign_identification_required";

    public const string ForeignIdNumberTooLong = "employee.foreign_id_number_too_long";
    public const string PassportNumberTooLong = "employee.passport_number_too_long";
    public const string SgkOccupationCodeTooLong = "employee.sgk_occupation_code_too_long";
}

/// <summary>Domain constants and length limits.</summary>
public static class EmployeeLimits
{
    public const int NameMaxLength = 100;

    /// <summary>"First Last": both names at max length plus the separating space.</summary>
    public const int DisplayNameMaxLength = (NameMaxLength * 2) + 1;

    public const int NationalIdLength = 11;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int AddressLineMaxLength = 250;
    public const int CityMaxLength = 100;
    public const int PostalCodeMaxLength = 20;
    public const int CareerNoteMaxLength = 500;
    public const int SalaryNoteMaxLength = 500;
    public const int BankNameMaxLength = 100;
    public const int BankAccountNoteMaxLength = 500;

    /// <summary>Money columns are numeric(18,2): kuruş precision, amounts strictly below this bound.</summary>
    public const decimal MoneyMax = 1_000_000_000_000m;

    public const int MoneyPrecision = 18;
    public const int MoneyScale = 2;

    /// <summary>AES-GCM ciphertext of a normalized IBAN (base64 of nonce + tag + at most 34 bytes).</summary>
    public const int ProtectedIbanMaxLength = 128;

    /// <summary>"TR** **** **** **** **** **12 34": at most 34 characters plus group separators.</summary>
    public const int MaskedIbanMaxLength = 42;

    /// <summary>Tenant-unique employee identifier (senseik-009/F-02), replacing NationalId as the external match key over time (see IEmployeeLookup remarks).</summary>
    public const int SicilNumarasiMaxLength = 30;

    /// <summary>Same shape as NationalId (11-digit, "99" prefix for foreign) - see TcKimlik.</summary>
    public const int ForeignIdNumberLength = 11;

    public const int PassportNumberMaxLength = 20;

    /// <summary>SGK "meslek kodu" (ISCO-08 based, senseik-010/F-03).</summary>
    public const int SgkOccupationCodeMaxLength = 20;
}

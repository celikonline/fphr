namespace Sense.Modules.Documents.Domain.DocumentTypes;

/// <summary>One system-seed row, pre-tenant/id (<see cref="DocumentTypeDefinition.Create"/> assigns those).</summary>
public sealed record DocumentTypeSeedRow(
    string Code,
    string Name,
    DocumentTypeStage Stage,
    bool IsRequired,
    DocumentRequirementCondition? Condition,
    int? RenewalPeriodDays,
    int Quantity,
    bool IsSensitive,
    int? DefaultRetentionDays,
    int SortOrder);

/// <summary>System seed list for the personnel document type catalog (11.7 roadmap: "zorunlu ve koşullu özlük belgesi kuralları").</summary>
public static class DocumentTypeSeed
{
    /// <summary>
    /// The written employment contract (4857 md.8). Its requirement gets the statutory deadline instead of the generic
    /// grace period: when no written contract exists the employee must receive a working-conditions document within
    /// Contract.WorkingConditionsDocDeadlineDays of being hired.
    /// </summary>
    public const string EmploymentContractCode = "EmploymentContract";

    public static IReadOnlyList<DocumentTypeSeedRow> SystemDefinitions() =>
    [
        // Onboarding: required, no condition.
        Row("NationalIdCopy", "Nüfus cüzdanı fotokopisi", DocumentTypeStage.Onboarding, true, null, 10),
        Row("PopulationRegistryExtract", "Nüfus kayıt örneği", DocumentTypeStage.Onboarding, true, null, 20),
        Row("ResidenceCertificate", "İkametgah belgesi", DocumentTypeStage.Onboarding, true, null, 30),
        Row("HealthReport", "Sağlık raporu", DocumentTypeStage.Onboarding, true, null, 40, isSensitive: true),
        Row("DiplomaCopy", "Diploma fotokopisi", DocumentTypeStage.Onboarding, true, null, 50),
        Row("CriminalRecord", "Adli sicil kaydı", DocumentTypeStage.Onboarding, true, null, 60, isSensitive: true),
        Row("PassportPhoto", "Vesikalık fotoğraf", DocumentTypeStage.Onboarding, true, null, 70, quantity: 2),
        Row("FamilyStatusDocument", "Aile durumu belgesi", DocumentTypeStage.Onboarding, true, null, 80),
        Row("MilitaryStatusDocument", "Askerlik durum belgesi", DocumentTypeStage.Onboarding, true, null, 90),
        Row(EmploymentContractCode, "İş sözleşmesi", DocumentTypeStage.Onboarding, true, null, 100),
        Row("SgkHireDeclaration", "SGK işe giriş bildirgesi", DocumentTypeStage.Onboarding, true, null, 110),

        // Conditional: required, each with its condition.
        Row("ParentalConsent", "Ebeveyn izni", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.AgeUnder18, 10),
        Row("WorkPermit", "Çalışma izni belgesi", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.NonTurkishNational, 20),
        Row("IncentiveDocument", "Teşvik belgesi", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.HasIncentive, 30),
        Row("DisabilityReport", "Engelli raporu", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.HasDisability, 40, isSensitive: true),
        Row("DisabilityTaxReductionLetter", "Engelli vergi indirimi yazısı", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.UsesDisabilityTaxReduction, 50, isSensitive: true),
        Row("IskurRegistration", "İŞKUR kayıt belgesi", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.ExConvictOrTerrorVictim, 60),
        Row("PeriodicHealthCheck", "Periyodik sağlık kontrolü raporu", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.RequiresPeriodicHealthCheck, 70, renewalPeriodDays: 365, isSensitive: true),
        Row("DriverLicenseCopy", "Ehliyet fotokopisi", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.RequiresDriving, 80),
        Row("AssetHandoverForm", "Zimmet formu", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.HasOpenAssets, 90),
        Row("PersonnelInfoForm", "Personel bilgi formu", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.VeryHazardousJob, 100),
        Row("OvertimeConsent", "Fazla çalışma onay belgesi", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.HasOvertimeConsent, 110),
        Row("WorkScheduleChart", "Çalışma çizelgesi", DocumentTypeStage.Conditional, true, DocumentRequirementCondition.IsShiftWorker, 120),

        // DuringEmployment: informational/event-driven, not blocking.
        Row("AnnualLeaveForm", "Yıllık izin formu", DocumentTypeStage.DuringEmployment, false, null, 10),
        Row("OtherLeaveForm", "Diğer izin formu", DocumentTypeStage.DuringEmployment, false, null, 20),
        Row("MaternityLeavePetition", "Doğum izni dilekçesi", DocumentTypeStage.DuringEmployment, false, null, 30),
        Row("CallUpPaper", "Celp/sevk belgesi", DocumentTypeStage.DuringEmployment, false, null, 40),
        Row("FosterCarePlacement", "Koruyucu aile teslim belgesi", DocumentTypeStage.DuringEmployment, false, null, 50),
        Row("OvertimeConsentRevocationForm", "Fazla çalışma onayı geri alma formu", DocumentTypeStage.DuringEmployment, false, null, 60),
        Row("IncapacityReport", "İş göremezlik raporu", DocumentTypeStage.DuringEmployment, false, null, 70, isSensitive: true),
        Row("WorkAccidentReport", "İş kazası tutanağı", DocumentTypeStage.DuringEmployment, false, null, 80),
        Row("WarningLetter", "Uyarı yazısı", DocumentTypeStage.DuringEmployment, false, null, 90),
        Row("SignedPayslip", "İmzalı ücret pusulası", DocumentTypeStage.DuringEmployment, false, null, 100),
        Row("OhsTrainingRecord", "İSG eğitimi kaydı", DocumentTypeStage.DuringEmployment, false, null, 110),

        // Offboarding: required.
        Row("ResignationLetter", "İstifa dilekçesi", DocumentTypeStage.Offboarding, true, null, 10),
        Row("TerminationNotice", "Fesih bildirimi", DocumentTypeStage.Offboarding, true, null, 20),
        Row("ReleaseForm", "İbraname", DocumentTypeStage.Offboarding, true, null, 30),
        Row("SgkExitDeclaration", "SGK işten ayrılış bildirgesi", DocumentTypeStage.Offboarding, true, null, 40),
        Row("ServiceCertificate", "Hizmet belgesi", DocumentTypeStage.Offboarding, true, null, 50),

        // Recommended: not required.
        Row("JobApplicationForm", "İş başvuru formu", DocumentTypeStage.Recommended, false, null, 10),
        Row("Cv", "Özgeçmiş", DocumentTypeStage.Recommended, false, null, 20),
        Row("ReferenceLetter", "Referans mektubu", DocumentTypeStage.Recommended, false, null, 30),
        Row("PreviousEmployerCertificate", "Bonservis", DocumentTypeStage.Recommended, false, null, 40),
    ];

    private static DocumentTypeSeedRow Row(
        string code,
        string name,
        DocumentTypeStage stage,
        bool isRequired,
        DocumentRequirementCondition? condition,
        int sortOrder,
        int? renewalPeriodDays = null,
        int quantity = 1,
        bool isSensitive = false,
        int? defaultRetentionDays = null) =>
        new(code, name, stage, isRequired, condition, renewalPeriodDays, quantity, isSensitive, defaultRetentionDays, sortOrder);
}

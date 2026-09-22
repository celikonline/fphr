using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;

namespace Sense.Modules.OccupationalSafety.Domain.Services;

/// <summary>Who provides an OHS service at a workplace (İş Sağlığı ve Güvenliği Hizmetleri Yönetmeliği).</summary>
public enum SafetyServiceRole
{
    /// <summary>İş güvenliği uzmanı.</summary>
    SafetyExpert = 0,

    /// <summary>İşyeri hekimi.</summary>
    WorkplacePhysician = 1,

    /// <summary>Diğer sağlık personeli (DSP) - only mandated for very hazardous workplaces.</summary>
    OtherHealthPersonnel = 2,
}

/// <summary>İş güvenliği uzmanlık belgesi sınıfı.</summary>
public enum SafetyExpertCertificateClass
{
    A = 0,
    B = 1,
    C = 2,
}

/// <summary>İSG kurulu üyesi rolü (İş Sağlığı ve Güvenliği Kurulları Hakkında Yönetmelik md.6).</summary>
public enum CommitteeRole
{
    /// <summary>İşveren veya işveren vekili (başkan).</summary>
    Chair = 0,

    /// <summary>İş güvenliği uzmanı (sekreter).</summary>
    Secretary = 1,

    /// <summary>İşyeri hekimi.</summary>
    Physician = 2,

    /// <summary>İnsan kaynakları / personel / sosyal işler / idari-mali işler sorumlusu.</summary>
    HrRepresentative = 3,

    /// <summary>Sivil savunma uzmanı (varsa).</summary>
    CivilDefenseExpert = 4,

    /// <summary>Formen, ustabaşı veya usta.</summary>
    Foreman = 5,

    /// <summary>Çalışan temsilcisi (birden fazlaysa baş temsilci).</summary>
    EmployeeRepresentative = 6,
}

/// <summary>
/// Statutory norms keyed by hazard class. Kept as static tables (like RiskAssessmentRenewalPeriods) and verified
/// against the regulation texts: İSG Hizmetleri Yönetmeliği md.12 (minutes per employee per month), md.8/md.10
/// (certificate class), 6331 md.20 (employee representatives), İSG Kurulları Yönetmeliği md.5/md.7 (committee threshold,
/// meeting frequency).
/// </summary>
public static class OhsServiceNorms
{
    /// <summary>6331 md.22: a committee is mandatory with 50 or more employees (and work lasting over six months).</summary>
    public const int CommitteeEmployeeThreshold = 50;

    /// <summary>Minimum service minutes per employee per month; 0 when the role is not mandated for the class.</summary>
    public static int MinutesPerEmployeePerMonth(SafetyServiceRole role, HazardClass hazardClass) => (role, hazardClass) switch
    {
        (SafetyServiceRole.SafetyExpert, HazardClass.Low) => 10,
        (SafetyServiceRole.SafetyExpert, HazardClass.Medium) => 20,
        (SafetyServiceRole.SafetyExpert, HazardClass.High) => 40,
        (SafetyServiceRole.WorkplacePhysician, HazardClass.Low) => 5,
        (SafetyServiceRole.WorkplacePhysician, HazardClass.Medium) => 10,
        (SafetyServiceRole.WorkplacePhysician, HazardClass.High) => 15,
        (SafetyServiceRole.OtherHealthPersonnel, HazardClass.High) => 10,
        _ => 0,
    };

    /// <summary>A: any class; B: dangerous and less hazardous; C: less hazardous only.</summary>
    public static bool CertificateAllowed(SafetyExpertCertificateClass certificate, HazardClass hazardClass) => certificate switch
    {
        SafetyExpertCertificateClass.A => true,
        SafetyExpertCertificateClass.B => hazardClass != HazardClass.High,
        _ => hazardClass == HazardClass.Low,
    };

    /// <summary>İSG Kurulları Yönetmeliği md.7: çok tehlikeli ayda bir, tehlikeli iki ayda bir, az tehlikeli üç ayda bir.</summary>
    public static int CommitteeMeetingIntervalMonths(HazardClass hazardClass) => hazardClass switch
    {
        HazardClass.High => 1,
        HazardClass.Medium => 2,
        _ => 3,
    };

    /// <summary>6331 md.20: number of employee representatives by headcount.</summary>
    public static int EmployeeRepresentativeCount(int employeeCount) => employeeCount switch
    {
        < 2 => 0,
        <= 50 => 1,
        <= 100 => 2,
        <= 500 => 3,
        <= 1000 => 4,
        <= 2000 => 5,
        _ => 6,
    };
}

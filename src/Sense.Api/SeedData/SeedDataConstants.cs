namespace Sense.Api.SeedData;

/// <summary>Hard-coded constants for seed data generation.</summary>
public static class SeedDataConstants
{
    // Minimal Profile
    public static readonly Guid MinimalTenantId = new("a1b2c3d4-e5f6-4a5b-9c8d-7e6f5a4b3c2d");
    public const string MinimalTenantSlug = "pilot-company";
    public const string MinimalTenantName = "Pilot Company";
    public const string MinimalAdminEmail = "admin@senseik.dev";

    // Demo Profile
    public static readonly Guid DemoTenantId = new("b2c3d4e5-f6a7-5b6c-ad9e-8f7a6b5c4d3e");
    public const string DemoTenantSlug = "demo-hr-company";
    public const string DemoTenantName = "Demo HR Company";

    /// <summary>Login password for every seeded user (minimal admin + demo accounts). Documented in README.md.</summary>
    public const string SeedUserPassword = "Demo123!";

    // Turkish Names for Demo Data (realistic mix)
    public static readonly string[] TurkishFirstNames =
    [
        "Ahmet", "Mehmet", "Ali", "Fatih", "Mustafa", "Ibrahim", "Yusuf", "Hamza",
        "Kerem", "Deniz", "Cem", "Arda", "Serkan", "Emre", "Kaan", "Bahadir",
        "Ayşe", "Fatma", "Zeynep", "Emine", "Elif", "Sevgi", "Gülşen", "Türkan",
        "Selin", "Betül", "Pınar", "Didem", "Hande", "Ceren", "Burçin", "Nilüfer",
        "Şirvan", "Kemal", "Ercan", "Bülent", "Hakan", "Nihat", "Salih", "Tunç",
        "İbrahim", "Fikret", "Galip", "Hayri", "Kadir", "Levent", "Murat", "Nezih",
        "Osman", "Paşa", "Raşid", "Saime", "Talat", "Ufuk", "Vecdi", "Wilmar"
    ];

    public static readonly string[] TurkishLastNames =
    [
        "Yilmaz", "Kaya", "Demir", "Celik", "Acar", "Ozdemir", "Sahin", "Aydin",
        "Karaca", "Polat", "Gunduz", "Tunc", "Altun", "Aslan", "Balci", "Besli",
        "Cakmak", "Caliskan", "Cilek", "Cinar", "Demirtas", "Demirtus", "Deniz",
        "Dogan", "Donmez", "Duran", "Durmus", "Erol", "Esen", "Eskin", "Ezer",
        "Fidan", "Firat", "Gedikli", "Genis", "Gercek", "Giray", "Godek", "Goken",
        "Goral", "Gozkaya", "Guclu", "Gumus", "Gunay", "Gundogdu", "Gungor", "Gur",
        "Guray", "Gurbuz", "Gurhan", "Gurtekin", "Gurucu", "Guzel", "Guzelbey", "Guzey"
    ];

    // System roles that should be seeded (already defined in SystemRoleCodes)
    // - Owner, Admin, HR, HRSpecialist, PayrollSpecialist, Finance, Manager, Timekeeper,
    //   Recruiter, Interviewer, Auditor, ExternalAccountant, ITAdmin, Employee, Intern, Contractor, ApiIntegration
}

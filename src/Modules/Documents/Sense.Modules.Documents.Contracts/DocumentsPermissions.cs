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
        "Belge Oluşturma",
        "Belgeler",
        "Yeni bir dosya belgesi yükleme");

    /// <summary>Read/download a file document.</summary>
    public static readonly Permission FilesRead = new(
        "documents.files.read",
        Module,
        "Belge Görüntüleme",
        "Belgeler",
        "Dosya belgelerini görüntüleme ve indirme");

    /// <summary>Delete a file document.</summary>
    public static readonly Permission FilesDelete = new(
        "documents.files.delete",
        Module,
        "Belge Silme",
        "Belgeler",
        "Dosya belgelerini silme");

    /// <summary>View the tenant's personnel document type catalog.</summary>
    public static readonly Permission DocumentTypesView = new(
        "documents.document_types.view",
        Module,
        "Belge Türlerini Görüntüleme",
        "Belgeler",
        "Özlük belge türü kataloğunu görüntüleme");

    /// <summary>Create/edit/activate/deactivate personnel document types.</summary>
    public static readonly Permission DocumentTypesManage = new(
        "documents.document_types.manage",
        Module,
        "Belge Türü Yönetimi",
        "Belgeler",
        "Özlük belge türleri oluşturma, düzenleme ve etkinleştirme/pasifleştirme");

    /// <summary>View employees' document requirement status.</summary>
    public static readonly Permission RequirementsView = new(
        "documents.requirements.view",
        Module,
        "Belge Yükümlülüklerini Görüntüleme",
        "Belgeler",
        "Çalışanların özlük belge yükümlülük durumunu görüntüleme");

    /// <summary>Waive/clear-waive/link/reevaluate employees' document requirements.</summary>
    public static readonly Permission RequirementsManage = new(
        "documents.requirements.manage",
        Module,
        "Belge Yükümlülüğü Yönetimi",
        "Belgeler",
        "Çalışanların özlük belge yükümlülüklerini muaf tutma, muafiyeti kaldırma, ilişkilendirme ve yeniden değerlendirme");

    /// <summary>Self-service: view the caller's own document requirement status, and satisfy one by uploading/linking a document.</summary>
    public static readonly Permission MyRequirements = new(
        "documents.my.requirements",
        Module,
        "Kendi Belge Yükümlülüklerim",
        "Belgeler",
        "Kendi özlük belge yükümlülük durumunuzu görüntüleme ve yüklenmiş bir belgeyi ilişkilendirerek karşılama");

    /// <summary>Self-service: upload a file document for the caller's own use (e.g. to satisfy one's own requirement).</summary>
    public static readonly Permission MyFilesCreate = new(
        "documents.my.files",
        Module,
        "Kendi Belgemi Yükleme",
        "Belgeler",
        "Kendi kullanımınız için bir dosya belgesi yükleme");

    /// <summary>View/download tenant-wide shared company files (senseik-028). Key was already reserved in
    /// <c>SystemRoleDefinitions</c>'s Employee/Intern role includes before this feature existed; reused rather than
    /// introducing a parallel "documents.sharedfile.view" key.</summary>
    public static readonly Permission CompanyFilesView = new(
        "documents.company.view",
        Module,
        "Ortak Dosyaları Görüntüleme",
        "Belgeler",
        "Kiracı geneli ortak şirket dosyalarını görüntüleme ve indirme");

    /// <summary>Upload/delete shared company files (senseik-028). Granted to HR/Admin/Owner via those roles'
    /// existing "documents.*" wildcard include - no SystemRoleDefinitions change needed.</summary>
    public static readonly Permission CompanyFilesManage = new(
        "documents.company.manage",
        Module,
        "Ortak Dosyaları Yönetme",
        "Belgeler",
        "Kiracı geneli ortak şirket dosyalarını yükleme ve silme");

    /// <summary>Self-service: submit a visa document processing request and view the caller's own requests
    /// (senseik-001-vize-belgesi).</summary>
    public static readonly Permission VisaRequest = new(
        "documents.visa.request",
        Module,
        "Request Visa Documents",
        "Documents",
        "Submit a visa document processing request and view the caller's own requests");

    /// <summary>Approve or reject a submitted visa document request (the tenant's configured approver).</summary>
    public static readonly Permission VisaApprove = new(
        "documents.visa.approve",
        Module,
        "Approve Visa Requests",
        "Documents",
        "Approve or reject submitted visa document requests");

    /// <summary>Configure the tenant's visa approver and delivered template documents (Settings page).</summary>
    public static readonly Permission VisaManage = new(
        "documents.visa.manage",
        Module,
        "Manage Visa Settings",
        "Documents",
        "Configure the tenant's visa document approver and template documents");

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
        MyFilesCreate,
        CompanyFilesView,
        CompanyFilesManage,
        VisaRequest,
        VisaApprove,
        VisaManage,
    ];
}

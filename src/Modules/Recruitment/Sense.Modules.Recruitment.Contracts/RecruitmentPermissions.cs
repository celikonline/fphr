using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Recruitment.Contracts;

/// <summary>
/// Recruitment module permission definitions (R0: postings/candidates/applications only). SystemRoleDefinitions.cs
/// already grants "recruitment.*" to HR/HRSpecialist/Recruiter, so no SystemRoleDefinitions.cs change is needed for
/// the keys below; Interviewer/Manager's narrower "recruitment.interviews.*"/"recruitment.scorecards.*" keys belong
/// to later Recruitment packages (interviews/scorecards are not modeled yet).
/// </summary>
public static class RecruitmentPermissions
{
    private const string Module = "recruitment";
    private const string Group = "İşe Alım";

    public static readonly Permission PostingsRead = new(
        "recruitment.postings.read", Module, "İlanları Görüntüleme", Group, "İş ilanlarını görüntüleme");

    public static readonly Permission PostingsCreate = new(
        "recruitment.postings.create", Module, "İlan Oluşturma", Group, "İş ilanı oluşturma");

    public static readonly Permission PostingsUpdate = new(
        "recruitment.postings.update", Module, "İlan Güncelleme", Group, "Bir iş ilanının alanlarını düzenleme, yayınlama/kapatma/yeniden açma veya erişim listesini değiştirme");

    public static readonly Permission PostingsArchive = new(
        "recruitment.postings.archive", Module, "İlan Arşivleme", Group, "Bir iş ilanını arşivleme (kesin)");

    public static readonly Permission CandidatesRead = new(
        "recruitment.candidates.read", Module, "Adayları Görüntüleme", Group, "Adayları görüntüleme");

    public static readonly Permission CandidatesManage = new(
        "recruitment.candidates.manage", Module, "Aday Yönetimi", Group, "Bir adayın iletişim bilgilerini ekleme veya düzenleme");

    public static readonly Permission ApplicationsRead = new(
        "recruitment.applications.read", Module, "Başvuruları Görüntüleme", Group, "Bir ilanın veya adayın başvurularını görüntüleme");

    public static readonly Permission ApplicationsManage = new(
        "recruitment.applications.manage", Module, "Başvuru Yönetimi", Group, "Başvuru ekleme veya süreç aşamaları arasında taşıma");

    public static readonly Permission RequisitionsRead = new(
        "recruitment.requisitions.read", Module, "Kadro Taleplerini Görüntüleme", Group, "Kadro taleplerini görüntüleme");

    public static readonly Permission RequisitionsManage = new(
        "recruitment.requisitions.manage", Module, "Kadro Talebi Yönetimi", Group, "Kadro talebi oluşturma, onaya gönderme veya iptal etme");

    public static readonly Permission TagsRead = new(
        "recruitment.tags.read", Module, "Etiketleri Görüntüleme", Group, "Aday etiket kataloğunu görüntüleme");

    public static readonly Permission TagsManage = new(
        "recruitment.tags.manage", Module, "Etiket Yönetimi", Group, "Aday etiketi oluşturma veya pasifleştirme");

    public static readonly Permission FormTemplatesRead = new(
        "recruitment.form_templates.read", Module, "Form Şablonlarını Görüntüleme", Group, "Geri bildirim/değerlendirme/sonuç form şablonlarını görüntüleme");

    public static readonly Permission FormTemplatesManage = new(
        "recruitment.form_templates.manage", Module, "Form Şablonu Yönetimi", Group, "Form şablonu oluşturma, alanlarını değiştirme veya pasifleştirme");

    public static readonly Permission FormResponsesRead = new(
        "recruitment.form_responses.read", Module, "Form Yanıtlarını Görüntüleme", Group, "Bir başvuruya ait gönderilmiş form yanıtlarını görüntüleme");

    /// <summary>Also granted to the Interviewer system role (see SystemRoleDefinitions.cs) - replaces that role's earlier speculative "recruitment.scorecards.submit" key.</summary>
    public static readonly Permission FormResponsesSubmit = new(
        "recruitment.form_responses.submit", Module, "Form Yanıtı Gönderme", Group, "Bir başvuru için geri bildirim, değerlendirme veya sonuç formu gönderme");

    public static readonly Permission AutomationRulesRead = new(
        "recruitment.automation_rules.read", Module, "Otomasyon Kurallarını Görüntüleme", Group, "Süreç otomasyon kurallarını görüntüleme");

    public static readonly Permission AutomationRulesManage = new(
        "recruitment.automation_rules.manage", Module, "Otomasyon Kuralı Yönetimi", Group, "Süreç otomasyon kuralı oluşturma veya pasifleştirme");

    public static IEnumerable<Permission> All =>
        [
            PostingsRead, PostingsCreate, PostingsUpdate, PostingsArchive, CandidatesRead, CandidatesManage,
            ApplicationsRead, ApplicationsManage, RequisitionsRead, RequisitionsManage, TagsRead, TagsManage,
            FormTemplatesRead, FormTemplatesManage, FormResponsesRead, FormResponsesSubmit, AutomationRulesRead, AutomationRulesManage,
        ];
}

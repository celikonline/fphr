using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Workflow.Contracts;

/// <summary>
/// Workflow module permission definitions. SystemRoleDefinitions.cs already anticipates these prefixes -
/// HR/HRSpecialist carry "workflow.*", Manager carries "workflow.inbox.*", and every base role carries
/// PermissionPatterns.SelfService ("*.my.*") - so no SystemRoleDefinitions.cs change is needed for the keys below.
/// </summary>
public static class WorkflowPermissions
{
    private const string Module = "workflow";
    private const string Group = "Onay Süreçleri";

    /// <summary>Read approval policies (name, request type, steps).</summary>
    public static readonly Permission PoliciesRead = new(
        "workflow.policies.read", Module, "Onay Politikalarını Görüntüleme", Group, "Onay politikalarını ve adımlarını görüntüleme");

    /// <summary>Create a named approval policy.</summary>
    public static readonly Permission PoliciesCreate = new(
        "workflow.policies.create", Module, "Onay Politikası Oluşturma", Group, "Adlandırılmış bir onay politikası oluşturma");

    /// <summary>Rename/replace steps/toggle visibility on an approval policy.</summary>
    public static readonly Permission PoliciesUpdate = new(
        "workflow.policies.update", Module, "Onay Politikası Güncelleme", Group, "Bir onay politikasını yeniden adlandırma, adım listesini değiştirme veya görünürlüğünü değiştirme");

    /// <summary>Deactivate (soft delete) an approval policy.</summary>
    public static readonly Permission PoliciesDelete = new(
        "workflow.policies.delete", Module, "Onay Politikası Pasifleştirme", Group, "Bir onay politikasını pasifleştirme");

    /// <summary>View pending approval tasks assigned to the caller (directly, or via a fallback role queue).</summary>
    public static readonly Permission InboxView = new(
        "workflow.inbox.view", Module, "Onay Kutusunu Görüntüleme", Group, "Kendinize atanmış bekleyen onay görevlerini görüntüleme");

    /// <summary>Decide (approve/reject) a pending task the caller is the assignee of, or is authorized for via a fallback role queue.</summary>
    public static readonly Permission InboxDecide = new(
        "workflow.inbox.decide", Module, "Onay Görevine Karar Verme", Group, "Kendinize atanmış bekleyen bir onay görevini onaylama veya reddetme");

    /// <summary>Decide a pending task on behalf of someone else, or cancel any approval request (Admin override).</summary>
    public static readonly Permission RequestOverride = new(
        "workflow.request.override", Module, "Onay Kararını Geçersiz Kılma", Group, "Başka biri adına bekleyen bir onay görevine karar verme veya herhangi bir onay talebini iptal etme");

    /// <summary>View the status/history of one's own submitted approval requests.</summary>
    public static readonly Permission MyRequests = new(
        "workflow.my.requests", Module, "Kendi Onay Taleplerimi Görüntüleme", Group, "Kendi gönderdiğiniz onay taleplerinin durumunu ve geçmişini görüntüleme");

    public static readonly Permission DelegationManage = new(
        "workflow.delegation.manage", Module, "Onay Vekaletlerini Yönetme", Group, "Herhangi bir çalışan adına onay vekaleti oluşturma ve iptal etme");

    public static readonly Permission MyDelegation = new(
        "workflow.my.delegation", Module, "Kendi Onay Vekaletimi Yönetme", Group, "Kendi onay yetkinizi belirli bir tarih aralığında başka bir çalışana devretme");

    public static IEnumerable<Permission> All =>
        [PoliciesRead, PoliciesCreate, PoliciesUpdate, PoliciesDelete, InboxView, InboxDecide, RequestOverride, MyRequests, DelegationManage, MyDelegation];
}

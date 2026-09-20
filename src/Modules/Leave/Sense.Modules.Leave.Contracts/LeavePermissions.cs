using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// Leave module permissions (11.3.5 "Roller ve yetkiler"). SystemRoleDefinitions already grants "leave.*" to HR and
/// HRSpecialist and "*.my.*" (self-service) to every base role.
/// </summary>
public static class LeavePermissions
{
    private const string Module = "leave";
    private const string Group = "İzin";

    public static readonly Permission RuleSetsRead = new(
        "leave.rule_sets.read", Module, "İzin Kural Setlerini Görüntüleme", Group, "İzin kural setlerini ve içerdikleri izin türlerini görüntüleme");

    public static readonly Permission RuleSetsManage = new(
        "leave.rule_sets.manage", Module, "İzin Kural Seti Yönetimi", Group, "İzin kural seti oluşturma, çoğaltma, yeniden adlandırma ve varsayılan olarak ayarlama");

    public static readonly Permission RuleSetsAssign = new(
        "leave.rule_sets.assign", Module, "İzin Kural Seti Atama", Group, "Çalışanlara izin kural seti atama");

    public static readonly Permission TypesManage = new(
        "leave.types.manage", Module, "İzin Türü Yönetimi", Group, "Bir kural seti içinde izin türü ekleme, düzenleme ve pasifleştirme");

    public static readonly Permission BalancesRead = new(
        "leave.balances.read", Module, "İzin Bakiyelerini Görüntüleme", Group, "Çalışanların izin bakiyelerini ve hareket dökümünü görüntüleme");

    public static readonly Permission BalancesAdjust = new(
        "leave.balances.adjust", Module, "İzin Bakiyesi Düzeltme", Group, "Manuel bakiye düzeltmesi işleme");

    public static readonly Permission BalancesRecalculate = new(
        "leave.balances.recalculate", Module, "İzin Bakiyelerini Yeniden Hesaplama", Group, "Çalışanlar için tahakkuk ve devir bakiyelerini yeniden üretme");

    public static readonly Permission ExtraGrantsManage = new(
        "leave.extra_grants.manage", Module, "Ek İzin Yönetimi", Group, "Ek izin günü tanımlama");

    public static readonly Permission MyBalances = new(
        "leave.my.balances", Module, "Kendi İzin Bakiyemi Görüntüleme", Group, "Kendi izin bakiyelerinizi görüntüleme");

    public static readonly Permission RequestsRead = new(
        "leave.requests.read", Module, "İzin Taleplerini Görüntüleme", Group, "Çalışanların izin taleplerini görüntüleme");

    /// <summary>Self-service create/edit/withdraw of own requests; "*.my.*" so every base role gets it (11.3.5 names it leave.requests.create).</summary>
    public static readonly Permission MyRequests = new(
        "leave.my.requests", Module, "Kendi İzin Taleplerimi Yönetme", Group, "Kendi izin talebinizi oluşturma, düzenleme, geri çekme ve görüntüleme");

    public static readonly Permission OvertimesRead = new(
        "leave.overtimes.read", Module, "Fazla Mesaiyi Görüntüleme", Group, "Çalışanların fazla mesai taleplerini ve dönüşümlerini görüntüleme");

    /// <summary>Self-service submit/withdraw/list of own overtime (11.3.5 names it leave.overtimes.create); "*.my.*" reaches every base role.</summary>
    public static readonly Permission MyOvertimes = new(
        "leave.my.overtimes", Module, "Kendi Fazla Mesaimi Yönetme", Group, "Kendi fazla mesai talebinizi oluşturma, geri çekme ve görüntüleme");

    public static readonly Permission OvertimesCreateOnBehalf = new(
        "leave.overtimes.create_on_behalf", Module, "Çalışan Adına Fazla Mesai Ekleme", Group, "Çalışanlar adına fazla mesai talebi oluşturma");

    public static readonly Permission OvertimesConvert = new(
        "leave.overtimes.convert", Module, "Fazla Mesai Dönüştürme", Group, "Onaylanmış fazla mesaiyi ek izne dönüştürme");

    public static readonly Permission OvertimesOverridePayable = new(
        "leave.overtimes.override_payable", Module, "Ödenecek Fazla Mesaiyi Geçersiz Kılma", Group, "Ödenecek dakikalar yerine fazla mesainin tamamını dönüştürme");

    public static readonly Permission OvertimeConsentsManage = new(
        "leave.overtime_consents.manage", Module, "Fazla Mesai Muvafakati Yönetimi", Group, "Çalışanların yazılı fazla mesai muvafakatini kaydetme, görüntüleme ve iptal etme");

    public static readonly Permission RequestsCreateOnBehalf = new(
        "leave.requests.create_on_behalf", Module, "Çalışan Adına İzin Ekleme", Group, "Çalışanlar adına izin talebi oluşturma (İşlemler → İzin Ekle)");

    public static readonly Permission RequestsBypassApproval = new(
        "leave.requests.bypass_approval", Module, "Onay Süreci Olmadan İzin Onaylama", Group, "Çalışanlar için eklenen izni onay sürecine gerek kalmadan onaylanmış olarak kaydetme");

    /// <summary>Decision #16: approved leave is cancelled (never edited) by an authorized user; also cancels pending requests.</summary>
    public static readonly Permission RequestsCancelApproved = new(
        "leave.requests.cancel_approved", Module, "İzin Talebi İptali", Group, "Çalışanların onaylanmış veya bekleyen izin taleplerini iptal etme");

    /// <summary>Y-4 (senseik-045): Pending requests without workflow approval can be approved/rejected by HR directly.</summary>
    public static readonly Permission RequestsApproveWithoutWorkflow = new(
        "leave.requests.approve_without_workflow", Module, "Onay Süreci Olmadan İzin Onaylama", Group, "Onay sürecine dahil edilmeden bekleyen izin taleplerini onaylama/reddetme");

    /// <summary>İzin takvimi menu; "*.my.*" reaches every base role (visibility further limited by Calendar access settings).</summary>
    public static readonly Permission MyCalendar = new(
        "leave.my.calendar", Module, "İzin Takvimini Görüntüleme", Group, "İzin takvimini açma (kendi izniniz veya kiracının izin verdiği durumda herkesinki)");

    public static readonly Permission CalendarRead = new(
        "leave.calendar.read", Module, "Ekip İzin Takvimini Görüntüleme", Group, "Takvimde, veri kapsamınızdaki çalışanların izinlerini görme");

    /// <summary>Employee card → Devamsızlık: unexcused/excused/late records İK enters by hand.</summary>
    public static readonly Permission AbsencesView = new(
        "leave.absences.view", Module, "Devamsızlık Kayıtlarını Görüntüleme", Group, "Çalışanların mazeretsiz/mazeretli/geç kalma devamsızlık kayıtlarını görüntüleme");

    public static readonly Permission AbsencesManage = new(
        "leave.absences.manage", Module, "Devamsızlık Kaydı Yönetimi", Group, "Çalışanların devamsızlık kayıtlarını işleme ve geri alma");

    /// <summary>
    /// senseik-048: şirket geneli izin/fazla mesai defteri ("bu ay kimler izinde", bakiye sıralaması, 270 saat sınır
    /// aşımı). HR/HRSpecialist zaten "leave.*" joker deseniyle, Owner/Admin "*" ile bunu otomatik alır -
    /// SystemRoleDefinitions'a ayrıca eklenmedi. Not: mevcut "leave.*.read" / "*.read" joker desenleri (PayrollSpecialist,
    /// Timekeeper, Auditor, ApiIntegration) de bu anahtarı eşler - veri yine ReportEmployeeSet üzerinden rolün veri kapsamıyla
    /// (ör. Timekeeper = Departman) sınırlanır. Manager açık listeyle tanımlı, almaz.
    /// </summary>
    public static readonly Permission CompanyLedgerRead = new(
        "leave.company_ledger.read", Module, "Şirket İzin/Fazla Mesai Defterini Görüntüleme", Group,
        "Şirket genelinde kimin izinde olduğunu, yıllık izin bakiyelerini ve yıllık fazla mesai sınırı aşımlarını görüntüleme");

    public static IEnumerable<Permission> All =>
        [RuleSetsRead, RuleSetsManage, RuleSetsAssign, TypesManage, BalancesRead, BalancesAdjust, BalancesRecalculate, ExtraGrantsManage, MyBalances,
         RequestsRead, MyRequests, OvertimesRead, MyOvertimes, OvertimesCreateOnBehalf, OvertimesConvert, OvertimesOverridePayable, OvertimeConsentsManage,
         RequestsCreateOnBehalf, RequestsBypassApproval, RequestsCancelApproved, RequestsApproveWithoutWorkflow, MyCalendar, CalendarRead, AbsencesView, AbsencesManage, CompanyLedgerRead];
}

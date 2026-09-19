using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Attendance.Contracts;

/// <summary>
/// Attendance module permissions (11.3.6 "Roller ve yetkiler"). SystemRoleDefinitions already grants "attendance.*"
/// to Owner/Admin/HR/HRSpecialist/Timekeeper and "*.my.*" (self-service) to every base role, so new attendance.*
/// keys reach the right roles without touching that file.
/// </summary>
public static class AttendancePermissions
{
    private const string Module = "attendance";
    private const string Group = "Puantaj";

    public static readonly Permission WorkScheduleView = new(
        "attendance.workschedule.view", Module, "Çalışma Tablolarını Görüntüleme", Group, "Çalışma tablolarını ve gün planlarını görüntüleme");

    public static readonly Permission WorkScheduleManage = new(
        "attendance.workschedule.manage", Module, "Çalışma Tablosu Yönetimi", Group, "Çalışma tablosu oluşturma, düzenleme, etkinleştirme/pasifleştirme ve varsayılan olarak ayarlama");

    public static readonly Permission WorkScheduleAssign = new(
        "attendance.workschedule.assign", Module, "Çalışma Tablosu Atama", Group, "Çalışanlara çalışma tablosu atama");

    /// <summary>"Çalışma tablosu" field on the employee card and Puantajım summary; "*.my.*" reaches every base role.</summary>
    public static readonly Permission MyWorkSchedule = new(
        "attendance.my.workschedule", Module, "Kendi Çalışma Tablomu Görüntüleme", Group, "Kendi çalışma tablonuzu görüntüleme");

    public static readonly Permission TimesheetView = new(
        "attendance.timesheet.view", Module, "Puantaj Kayıtlarını Görüntüleme", Group, "Çalışanların işlenmiş puantaj günlerini görüntüleme");

    /// <summary>"Çalışılan saat" manual record/correction (11.3.6); PDKS import (package A2b) reuses the same write path.</summary>
    public static readonly Permission TimesheetEdit = new(
        "attendance.timesheet.edit", Module, "Puantaj Kaydı Düzenleme", Group, "Çalışanların giriş/çıkış saatlerini kaydetme ve düzeltme");

    /// <summary>"Puantajım"; "*.my.*" reaches every base role.</summary>
    public static readonly Permission MyTimesheet = new(
        "attendance.my.timesheet", Module, "Kendi Puantajımı Görüntüleme", Group, "Kendi işlenmiş puantaj günlerinizi görüntüleme");

    public const string TimesheetImportKey = "attendance.timesheet.import";

    /// <summary>PDKS Excel import (package A2b); checked on top of datatransfer.import.run by the DataTransfer module's ImportTemplateCatalog.</summary>
    public static readonly Permission TimesheetImport = new(
        TimesheetImportKey, Module, "Puantaj İçe Aktarma (PDKS)", Group, "PDKS Excel dosyasından giriş/çıkış saatlerini içe aktarma");

    /// <summary>
    /// "Öneri" resolution (package A3, 11.3.6 "Roller ve yetkiler" - spec text names it attendance.timesheet.resolve):
    /// apply Fazla mesai ekle / Çalışılan saat / İzin talebi / Ignore to a flagged day. Reaching Leave for the
    /// overtime/leave-request actions additionally needs the caller's own Leave permissions (leave.requests.create_on_behalf,
    /// leave.requests.bypass_approval, leave.overtimes.create_on_behalf) - this key alone only unlocks Çalışılan saat/Ignore.
    /// SystemRoleDefinitions.cs's Manager entry already anticipated an "attendance.corrections.approve" key for this same
    /// purpose that was never wired up; it is left untouched and this key is appended to Manager's include list instead,
    /// to match the literal permission name the 11.3.06 spec uses for every other attendance.timesheet.* key.
    /// </summary>
    public const string TimesheetResolveKey = "attendance.timesheet.resolve";

    public static readonly Permission TimesheetResolve = new(
        TimesheetResolveKey, Module, "Puantaj Önerisini Çözümleme", Group, "İşaretli bir puantaj gününe önerilen çözümü (fazla mesai, çalışılan saat, izin talebi, yok say) uygulama");

    /// <summary>
    /// Vardiya izinleri (package A4, 11.3.6 "Roller ve yetkiler"). Timekeeper's "attendance.*" wildcard already
    /// grants view/plan/publish; its exclude list is appended with lock/unlock/settings (11.3.06 role mapping:
    /// "Timekeeper → ... plan/publish ... approve/lock yok") since the placeholder excludes someone pre-wrote there
    /// (attendance.timesheets.lock/.unlock, attendance.settings.*) don't match this module's actual key shape.
    /// Manager's include list is appended with shift.plan/.publish ("Manager → view/resolve/plan/publish @Team").
    /// </summary>
    public static readonly Permission ShiftView = new(
        "attendance.shift.view", Module, "Vardiya Planını Görüntüleme", Group, "Vardiya planını görüntüleme (haftalık/aylık görünümler, raporlar)");

    public static readonly Permission ShiftPlan = new(
        "attendance.shift.plan", Module, "Vardiya Planlama", Group, "Taslak vardiya atamaları oluşturma, düzenleme ve tekrarlama");

    public static readonly Permission ShiftPublish = new(
        "attendance.shift.publish", Module, "Vardiya Yayınlama", Group, "Taslak vardiya atamalarını çalışanlara yayınlama");

    public static readonly Permission ShiftSettingsManage = new(
        "attendance.shift.settings", Module, "Vardiya Ayarları Yönetimi", Group, "Günlük/haftalık limitleri, gece çalışmasını, molayı ve otomatik kilit ayarlarını yapılandırma");

    public static readonly Permission ShiftLock = new(
        "attendance.shift.lock", Module, "Vardiya Planını Kilitleme", Group, "Bir ayın vardiya planını değişikliğe karşı kilitleme");

    public static readonly Permission ShiftUnlock = new(
        "attendance.shift.unlock", Module, "Vardiya Planı Kilidini Açma", Group, "Kilitli bir ayın vardiya planını yeniden açma (denetimli istisna)");

    /// <summary>"Vardiyam"; "*.self.*" reaches every base role.</summary>
    public static readonly Permission ShiftSelf = new(
        "attendance.shift.self", Module, "Kendi Vardiyalarımı Görüntüleme", Group, "Kendi vardiya atamalarınızı görüntüleme");

    /// <summary>Package A5. "*.self.*" reaches every base role for self-service submit/cancel.</summary>
    public static readonly Permission AvailabilitySelf = new(
        "attendance.availability.self", Module, "Müsaitlik Durumumu Bildirme", Group, "Kendi \"müsait değilim\" kayıtlarınızı oluşturma ve iptal etme");

    public static readonly Permission AvailabilityView = new(
        "attendance.availability.view", Module, "Çalışan Müsaitlik Durumunu Görüntüleme", Group, "Vardiya planlarken çalışanların müsaitlik kayıtlarını görüntüleme");

    public const string CompensatoryViewKey = "attendance.compensatory.view";

    public static readonly Permission CompensatoryView = new(
        CompensatoryViewKey, Module, "Telafi Çalışması Defterini Görüntüleme", Group, "Çalışanların telafi çalışması borç/telafi defterini görüntüleme");

    public const string CompensatoryManageKey = "attendance.compensatory.manage";

    public static readonly Permission CompensatoryManage = new(
        CompensatoryManageKey, Module, "Telafi Çalışması Kaydetme", Group, "Açık bir telafi borcuna karşı çalışılan telafi saatlerini kaydetme");

    /// <summary>
    /// Ay sonu kapatma (senseik-029, 11.3.6 "Aylık Puantaj" S-11): bir çalışanın bir ayını MonthlyTimesheetApproval
    /// ile onaylar/yeniden onaylar ve mali müşavir raporunu (attendance.monthly.closing) üretilebilir hale getirir.
    /// Anahtar senseik-056 (b/attendance-monthly-timesheet-approval) ile ortak tutuldu - aynı eylem için tek izin. Yeni bir "attendance.*" anahtarı olduğundan SystemRoleDefinitions'taki mevcut "attendance.*" joker izinleri
    /// (Owner/Admin/HR/HRSpecialist/Timekeeper) ayrı bir dosya değişikliği gerekmeden bunu da kapsar.
    /// </summary>
    public const string TimesheetApproveKey = "attendance.timesheet.approve";

    public static readonly Permission TimesheetApprove = new(
        TimesheetApproveKey, Module, "Aylık Puantaj Onaylama", Group, "Bir çalışanın aylık puantajını dönem sonunda onaylama/kapatma (yeniden onaylama dahil)");

    /// <summary>Self-servis QR/GPS check-in/out (S-31); "*.my.*" reaches every base role, same as MyTimesheet/ShiftSelf/AvailabilitySelf above.</summary>
    public const string MyCheckInKey = "attendance.my.checkin";

    public static readonly Permission MyCheckIn = new(
        MyCheckInKey, Module, "Kendi Giriş-Çıkışımı Kaydetme", Group, "QR kod veya GPS ile kendi giriş-çıkışınızı kaydetme");

    /// <summary>Canlı içeride/dışarıda panosu (S-31): "attendance.*" already reaches Owner/Admin/HR/HRSpecialist/Timekeeper, "attendance.*.view" reaches Manager/PayrollSpecialist.</summary>
    public const string BoardViewKey = "attendance.board.view";

    public static readonly Permission BoardView = new(
        BoardViewKey, Module, "Canlı İçeride/Dışarıda Panosunu Görüntüleme", Group, "Şu an kimin içeride kimin dışarıda olduğunu canlı görüntüleme");

    public static IEnumerable<Permission> All =>
        [
            WorkScheduleView, WorkScheduleManage, WorkScheduleAssign, MyWorkSchedule, TimesheetView, TimesheetEdit, MyTimesheet, TimesheetImport, TimesheetResolve,
            TimesheetApprove, ShiftView, ShiftPlan, ShiftPublish, ShiftSettingsManage, ShiftLock, ShiftUnlock, ShiftSelf,
            AvailabilitySelf, AvailabilityView, CompensatoryView, CompensatoryManage, MyCheckIn, BoardView,
        ];
}

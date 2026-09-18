using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Training.Contracts;

public static class TrainingPermissions
{
    private const string Module = "training";
    private const string Group = "Eğitim";

    public static readonly Permission ViewPrograms = new(
        "training.programs.view", Module, "Eğitim Programlarını Görüntüleme", Group, "Eğitim programlarını ve katılımcılarını görüntüleme");

    public static readonly Permission ManagePrograms = new(
        "training.programs.manage", Module, "Eğitim Programı Yönetimi", Group, "Eğitim programı oluşturma, düzenleme ve kapatma");

    public static readonly Permission ViewEnrollments = new(
        "training.enrollments.view", Module, "Katılım Kayıtlarını Görüntüleme", Group, "Eğitim programı katılım kayıtlarını görüntüleme");

    public static readonly Permission ManageEnrollments = new(
        "training.enrollments.manage", Module, "Katılım Yönetimi", Group, "Katılım kaydı oluşturma, güncelleme ve tamamlama işaretleme");

    public static IEnumerable<Permission> All =>
        [ViewPrograms, ManagePrograms, ViewEnrollments, ManageEnrollments];
}

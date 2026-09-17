using Sense.Shared.Kernel;

namespace Sense.Modules.Training.Domain;

public static class TrainingDomainErrors
{
    public static readonly Error TitleRequired = Error.Validation(
        "training.program.title_required",
        "Program başlığı gereklidir ve 200 karakteri aşamaz.");

    public static readonly Error CannotActivateProgram = Error.Conflict(
        "training.program.cannot_activate",
        "Yalnızca taslak programlar aktivize edilebilir.");

    public static readonly Error CannotModifyArchived = Error.Conflict(
        "training.program.cannot_modify_archived",
        "Arşivlenmiş programlar değiştirilemez.");

    public static readonly Error AlreadyCompleted = Error.Conflict(
        "training.enrollment.already_completed",
        "Katılımcı zaten tamamlandı olarak işaretlenmiş.");

    public static readonly Error ProgramNotFound = Error.NotFound(
        "training.program.not_found",
        "Eğitim programı bulunamadı.");

    public static readonly Error EnrollmentNotFound = Error.NotFound(
        "training.enrollment.not_found",
        "Katılımcı kaydı bulunamadı.");
}

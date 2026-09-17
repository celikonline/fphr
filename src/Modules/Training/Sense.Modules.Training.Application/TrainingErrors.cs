using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application;

/// <summary>Training modülü application katmanı hataları.</summary>
public static class TrainingErrors
{
    public static readonly Error TitleRequired = Error.Validation(
        "training.program.title_required",
        "Program başlığı gereklidir.");

    public static readonly Error InvalidDateRange = Error.Validation(
        "training.program.invalid_date_range",
        "Başlangıç tarihi bitiş tarihinden önce olmalıdır.");

    public static readonly Error ProgramNotFound = Error.NotFound(
        "training.program.not_found",
        "Eğitim programı bulunamadı.");

    public static readonly Error EnrollmentNotFound = Error.NotFound(
        "training.enrollment.not_found",
        "Katılımcı kaydı bulunamadı.");

    public static readonly Error CannotModifyClosedProgram = Error.Conflict(
        "training.program.cannot_modify_closed",
        "Kapalı programlar değiştirilemez.");

    public static readonly Error DuplicateEnrollment = Error.Conflict(
        "training.enrollment.duplicate",
        "Katılımcı zaten bu programa kaydedilmiş.");
}

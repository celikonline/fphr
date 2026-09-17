using Sense.Shared.Contracts.Results;

namespace Sense.Modules.Training.Application;

public static class TrainingErrors
{
    public static readonly Error TrainingProgramNotFound = new("training.program.not_found", "Training program not found.");
    public static readonly Error EnrollmentNotFound = new("training.enrollment.not_found", "Enrollment not found.");
    public static readonly Error TitleRequired = new("training.program.title_required", "Title is required.");
    public static readonly Error InvalidDateRange = new("training.program.invalid_date_range", "Start date must be before end date.");
}

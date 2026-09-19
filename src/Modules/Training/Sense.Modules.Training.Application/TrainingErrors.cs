using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application;

/// <summary>Training modülü application katmanı hataları (Web katmanında SharedResource ile lokalize edilir, bkz. CLAUDE.md).</summary>
public static class TrainingErrors
{
    public static readonly Error TitleRequired = Error.Validation("training.program.title_required");

    public static readonly Error InvalidDateRange = Error.Validation("training.program.invalid_date_range");

    public static readonly Error ProgramNotFound = Error.NotFound("training.program.not_found");

    public static readonly Error EnrollmentNotFound = Error.NotFound("training.enrollment.not_found");

    public static readonly Error CannotModifyClosedProgram = Error.Conflict("training.program.cannot_modify_closed");

    public static readonly Error DuplicateEnrollment = Error.Conflict("training.enrollment.duplicate");

    /// <summary>A training program cannot be its own prerequisite.</summary>
    public static readonly Error RequirementSelfReference = Error.Validation("training.requirement.self_reference");

    public static readonly Error RequirementNotFound = Error.NotFound("training.requirement.not_found");

    /// <summary>Enrollment blocked: the program has a <c>BlocksAssignmentUntilCompleted</c> prerequisite the employee has not completed yet.</summary>
    public static readonly Error PrerequisiteNotCompleted = Error.Rule("training.enrollment.prerequisite_not_completed");
}

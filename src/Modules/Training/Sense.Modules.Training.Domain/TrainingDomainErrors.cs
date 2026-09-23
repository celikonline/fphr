using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Domain;

/// <summary>Domain error codes for the Training module (localized in the Web layer via SharedResource, per CLAUDE.md).</summary>
public static class TrainingDomainErrors
{
    public static readonly Error TitleRequired = Error.Validation("training.program.title_required");

    public static readonly Error CannotActivateProgram = Error.Conflict("training.program.cannot_activate");

    public static readonly Error CannotModifyArchived = Error.Conflict("training.program.cannot_modify_archived");

    public static readonly Error AlreadyCompleted = Error.Conflict("training.enrollment.already_completed");

    public static readonly Error AlreadyCancelled = Error.Conflict("training.enrollment.already_cancelled");

    public static readonly Error ProgramNotFound = Error.NotFound("training.program.not_found");

    public static readonly Error EnrollmentNotFound = Error.NotFound("training.enrollment.not_found");

    /// <summary>A training program cannot be its own prerequisite.</summary>
    public static readonly Error RequirementSelfReference = Error.Validation("training.requirement.self_reference");
}

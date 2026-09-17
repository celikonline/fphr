using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Training.Contracts;

public static class TrainingPermissions
{
    public const string ViewPrograms = "training.programs.view";
    public const string ManagePrograms = "training.programs.manage";
    public const string ViewEnrollments = "training.enrollments.view";
    public const string ManageEnrollments = "training.enrollments.manage";

    public static readonly IReadOnlyList<Permission> All =
    [
        new Permission(ViewPrograms, "View training programs"),
        new Permission(ManagePrograms, "Create and edit training programs"),
        new Permission(ViewEnrollments, "View enrollments"),
        new Permission(ManageEnrollments, "Manage enrollments"),
    ];
}

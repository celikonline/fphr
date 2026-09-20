using FluentValidation;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Enrollments;

/// <summary>Eğitim programı katılımcısı kaydı oluştur.</summary>
[RequiresPermission("training.enrollments.manage")]
public sealed record CreateEnrollmentCommand(
    Guid TrainingProgramId,
    Guid EmployeeId
) : ICommand<Guid>;

public sealed class CreateEnrollmentValidator : AbstractValidator<CreateEnrollmentCommand>
{
    public CreateEnrollmentValidator()
    {
        RuleFor(x => x.TrainingProgramId).NotEqual(Guid.Empty);
        RuleFor(x => x.EmployeeId).NotEqual(Guid.Empty);
    }
}

/// <summary>
/// Katılımcı kaydı oluştur — İK/HR tarafından (training.enrollments.manage izni gerekir). Roadmap #72 ("ön koşul
/// eğitim"): programın <see cref="TrainingRequirement.BlocksAssignmentUntilCompleted"/> işaretli bir ön koşulu varsa,
/// çalışan o ön koşulu (Completed) tamamlamadan bu kayıt reddedilir.
/// </summary>
public sealed class CreateEnrollmentHandler(
    IEnrollmentRepository enrollments,
    ITrainingProgramRepository programs,
    ITrainingRequirementRepository requirements,
    ITenantContext tenant) : ICommandHandler<CreateEnrollmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateEnrollmentCommand command, CancellationToken cancellationToken)
    {
        var program = await programs.GetByIdAsync(tenant.TenantId, command.TrainingProgramId, cancellationToken);
        if (program is null)
        {
            return TrainingErrors.ProgramNotFound;
        }

        var existing = await enrollments.GetByEmployeeIdAsync(tenant.TenantId, command.EmployeeId, cancellationToken);
        if (existing.Any(e => e.TrainingProgramId == command.TrainingProgramId))
        {
            return TrainingErrors.DuplicateEnrollment;
        }

        var gate = await CheckPrerequisitesAsync(command.TrainingProgramId, command.EmployeeId, cancellationToken);
        if (gate.IsFailure)
        {
            return gate.Error;
        }

        var created = Enrollment.Create(
            command.TrainingProgramId,
            tenant.TenantId,
            command.EmployeeId);

        if (created.IsFailure)
        {
            return created.Error;
        }

        await enrollments.AddAsync(created.Value, cancellationToken);
        return created.Value.Id;
    }

    private async Task<Result> CheckPrerequisitesAsync(Guid trainingProgramId, Guid employeeId, CancellationToken cancellationToken)
    {
        var blocking = (await requirements.GetByTrainingProgramIdAsync(tenant.TenantId, trainingProgramId, cancellationToken))
            .Where(r => r.BlocksAssignmentUntilCompleted)
            .ToList();

        if (blocking.Count == 0)
        {
            return Result.Success();
        }

        var employeeEnrollments = await enrollments.GetByEmployeeIdAsync(tenant.TenantId, employeeId, cancellationToken);
        var completedProgramIds = employeeEnrollments
            .Where(e => e.Status == EnrollmentStatus.Completed)
            .Select(e => e.TrainingProgramId)
            .ToHashSet();

        return blocking.Any(r => !completedProgramIds.Contains(r.PrerequisiteTrainingProgramId))
            ? TrainingErrors.PrerequisiteNotCompleted
            : Result.Success();
    }
}

/// <summary>Katılımcı kaydını tamamlandı olarak işaretle.</summary>
[RequiresPermission("training.enrollments.manage")]
public sealed record CompleteEnrollmentCommand(
    Guid EnrollmentId,
    string? CertificateNumber = null
) : ICommand;

/// <summary>Katılımcı kaydını tamamlandı olarak işaretle — İK/HR tarafından.</summary>
public sealed class CompleteEnrollmentHandler(IEnrollmentRepository enrollments, ITenantContext tenant)
    : ICommandHandler<CompleteEnrollmentCommand>
{
    public async Task<Result> Handle(CompleteEnrollmentCommand command, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetByIdAsync(tenant.TenantId, command.EnrollmentId, cancellationToken);
        if (enrollment is null)
        {
            return TrainingErrors.EnrollmentNotFound;
        }

        var result = enrollment.Complete(command.CertificateNumber);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await enrollments.UpdateAsync(enrollment, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Katılımcı kaydını iptal et.</summary>
[RequiresPermission("training.enrollments.manage")]
public sealed record CancelEnrollmentCommand(
    Guid EnrollmentId
) : ICommand;

/// <summary>Katılımcı kaydını iptal et — İK/HR tarafından.</summary>
public sealed class CancelEnrollmentHandler(IEnrollmentRepository enrollments, ITenantContext tenant)
    : ICommandHandler<CancelEnrollmentCommand>
{
    public async Task<Result> Handle(CancelEnrollmentCommand command, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetByIdAsync(tenant.TenantId, command.EnrollmentId, cancellationToken);
        if (enrollment is null)
        {
            return TrainingErrors.EnrollmentNotFound;
        }

        await enrollments.DeleteAsync(enrollment, cancellationToken);
        return Result.Success();
    }
}

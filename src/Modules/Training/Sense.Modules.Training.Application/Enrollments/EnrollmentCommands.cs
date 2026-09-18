using FluentValidation;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Commands;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Enrollments;

/// <summary>Eğitim programı katılımcısı kaydı oluştur.</summary>
[RequiresPermission("training.enrollments.manage")]
public sealed record CreateEnrollmentCommand(
    Guid TrainingProgramId,
    Guid EmployeeId,
    Guid TenantId
) : ICommand<Guid>;

public sealed class CreateEnrollmentValidator : AbstractValidator<CreateEnrollmentCommand>
{
    public CreateEnrollmentValidator()
    {
        RuleFor(x => x.TrainingProgramId)
            .NotEqual(Guid.Empty).WithErrorCode(TrainingErrors.ProgramNotFound.Code);

        RuleFor(x => x.EmployeeId)
            .NotEqual(Guid.Empty).WithMessage("Çalışan gereklidir.");

        RuleFor(x => x.TenantId)
            .NotEqual(Guid.Empty).WithMessage("Kiracı gereklidir.");
    }
}

/// <summary>Katılımcı kaydı oluştur — İK/HR tarafından (training.enrollments.manage izni gerekir).</summary>
public sealed class CreateEnrollmentHandler(IEnrollmentRepository enrollments, ITrainingProgramRepository programs)
    : ICommandHandler<CreateEnrollmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateEnrollmentCommand command, CancellationToken cancellationToken)
    {
        var program = await programs.GetByIdAsync(command.TrainingProgramId, cancellationToken);
        if (program is null)
        {
            return TrainingErrors.ProgramNotFound;
        }

        var created = Enrollment.Create(
            command.TrainingProgramId,
            command.TenantId,
            command.EmployeeId);

        if (created.IsFailure)
        {
            return created.Error;
        }

        await enrollments.AddAsync(created.Value, cancellationToken);
        return created.Value.Id;
    }
}

/// <summary>Katılımcı kaydını tamamlandı olarak işaretle.</summary>
[RequiresPermission("training.enrollments.manage")]
public sealed record CompleteEnrollmentCommand(
    Guid EnrollmentId,
    string? CertificateNumber = null
) : ICommand;

/// <summary>Katılımcı kaydını tamamlandı olarak işaretle — İK/HR tarafından.</summary>
public sealed class CompleteEnrollmentHandler(IEnrollmentRepository enrollments)
    : ICommandHandler<CompleteEnrollmentCommand>
{
    public async Task<Result> Handle(CompleteEnrollmentCommand command, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetByIdAsync(command.EnrollmentId, cancellationToken);
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
public sealed class CancelEnrollmentHandler(IEnrollmentRepository enrollments)
    : ICommandHandler<CancelEnrollmentCommand>
{
    public async Task<Result> Handle(CancelEnrollmentCommand command, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetByIdAsync(command.EnrollmentId, cancellationToken);
        if (enrollment is null)
        {
            return TrainingErrors.EnrollmentNotFound;
        }

        await enrollments.DeleteAsync(enrollment, cancellationToken);
        return Result.Success();
    }
}

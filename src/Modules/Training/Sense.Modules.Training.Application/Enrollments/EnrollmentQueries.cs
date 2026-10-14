using FluentValidation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Context;
using Sense.Modules.Training.Contracts;
using Sense.Modules.Training.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Enrollments;

/// <summary>Eğitim programı katılımcılarını listele.</summary>
[RequiresPermission("training.enrollments.view")]
public sealed record ListEnrollmentsQuery(Guid? TrainingProgramId = null, Guid? EmployeeId = null) : IQuery<List<EnrollmentDto>>;

/// <summary>Katılımcı kayıtlarını listele — repository'den veri al.</summary>
public sealed class ListEnrollmentsHandler(IEnrollmentRepository enrollments, ITenantContext tenant)
    : IQueryHandler<ListEnrollmentsQuery, List<EnrollmentDto>>
{
    public async Task<Result<List<EnrollmentDto>>> Handle(ListEnrollmentsQuery request, CancellationToken cancellationToken)
    {
        var enrollmentRecords = request.TrainingProgramId.HasValue
            ? await enrollments.GetByTrainingProgramIdAsync(tenant.TenantId, request.TrainingProgramId.Value, cancellationToken)
            : request.EmployeeId.HasValue
                ? await enrollments.GetByEmployeeIdAsync(tenant.TenantId, request.EmployeeId.Value, cancellationToken)
                : new List<Enrollment>();

        var dtos = enrollmentRecords
            .Select(e => new EnrollmentDto(
                e.Id,
                e.TrainingProgramId,
                e.EmployeeId,
                e.Status.ToString(),
                e.EnrolledAt,
                e.CompletedAt,
                e.CertificateNumber
            ))
            .ToList();

        return dtos;
    }
}

/// <summary>Katılımcı kaydını ID'ye göre getir.</summary>
[RequiresPermission("training.enrollments.view")]
public sealed record GetEnrollmentQuery(Guid EnrollmentId) : IQuery<EnrollmentDto>;

public sealed class GetEnrollmentValidator : AbstractValidator<GetEnrollmentQuery>
{
    public GetEnrollmentValidator()
    {
        RuleFor(x => x.EnrollmentId).NotEqual(Guid.Empty).WithMessage("Katılımcı kaydı gereklidir.");
    }
}

/// <summary>Katılımcı kaydını getir — repository'den veri al.</summary>
public sealed class GetEnrollmentHandler(IEnrollmentRepository enrollments, ITenantContext tenant)
    : IQueryHandler<GetEnrollmentQuery, EnrollmentDto>
{
    public async Task<Result<EnrollmentDto>> Handle(GetEnrollmentQuery request, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetByIdAsync(tenant.TenantId, request.EnrollmentId, cancellationToken);
        if (enrollment is null)
            return TrainingErrors.EnrollmentNotFound;

        return new EnrollmentDto(
            enrollment.Id,
            enrollment.TrainingProgramId,
            enrollment.EmployeeId,
            enrollment.Status.ToString(),
            enrollment.EnrolledAt,
            enrollment.CompletedAt,
            enrollment.CertificateNumber
        );
    }
}

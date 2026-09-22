using Sense.Modules.Training.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Enrollments;

/// <summary>"Eğitimlerim": çağıranın kendi katılım kayıtları, program bilgisiyle; yakın tarihli program önce.</summary>
[RequiresPermission("training.my.enrollments")]
public sealed record ListMyTrainingsQuery : IQuery<IReadOnlyList<MyTrainingDto>>;

public sealed class ListMyTrainingsHandler(
    IEnrollmentRepository enrollments,
    ITrainingProgramRepository programs,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : IQueryHandler<ListMyTrainingsQuery, IReadOnlyList<MyTrainingDto>>
{
    public async Task<Result<IReadOnlyList<MyTrainingDto>>> Handle(ListMyTrainingsQuery query, CancellationToken cancellationToken)
    {
        // Çalışan kaydı olmayan kullanıcının (ör. yalnız yönetici hesabı) katılımı olamaz.
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Result.Success<IReadOnlyList<MyTrainingDto>>([]);
        }

        var mine = await enrollments.GetByEmployeeIdAsync(tenant.TenantId, employeeId, cancellationToken);
        if (mine.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyTrainingDto>>([]);
        }

        var programsById = (await programs.GetAllAsync(tenant.TenantId, cancellationToken)).ToDictionary(p => p.Id);
        IReadOnlyList<MyTrainingDto> items = mine
            .Where(e => programsById.ContainsKey(e.TrainingProgramId))
            .Select(e =>
            {
                var program = programsById[e.TrainingProgramId];
                return new MyTrainingDto(
                    e.Id, program.Id, program.Title, program.Description, program.Category.ToString(),
                    program.StartDate, program.EndDate, program.Status.ToString(),
                    e.Status.ToString(), e.EnrolledAt, e.CompletedAt, e.CertificateNumber);
            })
            .OrderByDescending(t => t.StartDate)
            .ToList();
        return Result.Success(items);
    }
}

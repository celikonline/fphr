using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Domain;

/// <summary>
/// Training program agregat kökü.
/// Eğitim programı, sınıf, tarihler ve kapasite yönetimi.
/// </summary>
public sealed class TrainingProgram : TenantAggregateRoot<Guid>, IAuditLogged
{
    // ReSharper disable UnusedMember.Local
    private TrainingProgram() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public TrainingCategory Category { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    public int? MaxCapacity { get; private set; }
    public TrainingStatus Status { get; private set; }

    /// <summary>
    /// Eğitim programı oluştur.
    /// </summary>
    public static Result<TrainingProgram> Create(
        Guid tenantId,
        string title,
        string? description,
        TrainingCategory category,
        DateTime startDate,
        DateTime? endDate,
        int? maxCapacity)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return TrainingDomainErrors.TitleRequired;

        var program = new TrainingProgram
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = description,
            Category = category,
            StartDate = startDate,
            EndDate = endDate,
            MaxCapacity = maxCapacity,
            Status = TrainingStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        return program;
    }

    /// <summary>
    /// Programı açık duruma getir.
    /// </summary>
    public Result Activate()
    {
        if (Status != TrainingStatus.Draft)
            return TrainingDomainErrors.CannotActivateProgram;

        Status = TrainingStatus.Active;
        return Result.Success();
    }

    /// <summary>
    /// Programı kapat.
    /// </summary>
    public Result Close()
    {
        if (Status == TrainingStatus.Archived)
            return TrainingDomainErrors.CannotModifyArchived;

        Status = TrainingStatus.Closed;
        return Result.Success();
    }
}

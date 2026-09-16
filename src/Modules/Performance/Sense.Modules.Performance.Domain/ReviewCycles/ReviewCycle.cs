using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>Performans değerlendirme dönemi durumu.</summary>
public enum ReviewCycleStatus
{
    Draft = 0,
    Active = 1,
    Closed = 2,
}

/// <summary>
/// A performance review period ("değerlendirme dönemi", e.g. "2026 Q1"). Owns nothing but its own header data -
/// ReviewForm/Goal reference it by id. Closing is terminal: 4857 md.18 fesih delili gerekçesiyle kapanmış bir dönemin
/// içeriği (ileride eklenecek bireysel değerlendirme kayıtları dahil) değiştirilemez olmalı - bu kural burada Status'ün
/// bir daha Active'e dönmemesiyle uygulanıyor; ReviewForm.SetQuestions/Publish de yalnızca cycle Closed değilken izin verir
/// (bkz. ReviewFormCommands.cs).
/// </summary>
public sealed class ReviewCycle : TenantAggregateRoot<Guid>, IAuditLogged
{
    private ReviewCycle()
    {
    }

    private ReviewCycle(Guid id, Guid tenantId, string name, DateOnly startDate, DateOnly endDate)
        : base(id, tenantId)
    {
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
        Status = ReviewCycleStatus.Draft;
    }

    public string Name { get; private set; } = string.Empty;

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public ReviewCycleStatus Status { get; private set; }

    public static Result<ReviewCycle> Create(Guid tenantId, string name, DateOnly startDate, DateOnly endDate, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(PerformanceDomainErrors.CycleNameRequired);
        }

        if (endDate <= startDate)
        {
            return Error.Validation(PerformanceDomainErrors.CycleInvalidDateRange);
        }

        return new ReviewCycle(id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(name.Trim(), PerformanceLimits.NameMaxLength), startDate, endDate);
    }

    public Result Activate()
    {
        if (Status != ReviewCycleStatus.Draft)
        {
            return Error.Rule(PerformanceDomainErrors.CycleNotActivatable);
        }

        Status = ReviewCycleStatus.Active;
        return Result.Success();
    }

    /// <summary>Terminal: no reopen. See class remarks for the immutability rationale.</summary>
    public Result Close()
    {
        if (Status == ReviewCycleStatus.Closed)
        {
            return Error.Rule(PerformanceDomainErrors.CycleAlreadyClosed);
        }

        if (Status != ReviewCycleStatus.Active)
        {
            return Error.Rule(PerformanceDomainErrors.CycleNotClosable);
        }

        Status = ReviewCycleStatus.Closed;
        return Result.Success();
    }
}

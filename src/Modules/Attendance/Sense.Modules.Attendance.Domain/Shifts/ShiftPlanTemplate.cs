using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Shifts;

/// <summary>Şablonun bir hücresi (EF-owned, JSON): çalışan × dönem başından gün ofseti → vardiya tanımı ve gün türü.</summary>
public sealed class ShiftPlanTemplateEntry
{
    public ShiftPlanTemplateEntry()
    {
    }

    private ShiftPlanTemplateEntry(Guid employeeId, int dayOffset, Guid? shiftDefinitionId, ShiftDayType dayType)
    {
        EmployeeId = employeeId;
        DayOffset = dayOffset;
        ShiftDefinitionId = shiftDefinitionId;
        DayType = dayType;
    }

    public Guid EmployeeId { get; private set; }

    public int DayOffset { get; private set; }

    public Guid? ShiftDefinitionId { get; private set; }

    public ShiftDayType DayType { get; private set; }

    public static ShiftPlanTemplateEntry Of(Guid employeeId, int dayOffset, Guid? shiftDefinitionId, ShiftDayType dayType) =>
        new(employeeId, dayOffset, shiftDefinitionId, dayType);
}

/// <summary>
/// Kaydedilmiş vardiya planı şablonu (zaman-devam Z3.2, spec §4): bir dönemlik planın (ör. 7 ya da 14 gün) çalışan
/// bazında kopyası; başka bir döneme uygulanınca <see cref="ShiftAssignment"/> satırları üretilir (uygulama katmanı).
/// Bir çalışanın aynı gün ofsetinde tek hücresi olabilir.
/// </summary>
public sealed class ShiftPlanTemplate : TenantAggregateRoot<Guid>, IAuditLogged
{
    /// <summary>En uzun şablon dönemi: iki aylık plan (62 gün).</summary>
    public const int MaxPeriodLengthDays = 62;

    private readonly List<ShiftPlanTemplateEntry> _entries = [];

    private ShiftPlanTemplate()
    {
    }

    private ShiftPlanTemplate(Guid id, Guid tenantId, Guid createdByUserId)
        : base(id, tenantId) => CreatedByUserId = createdByUserId;

    public string Name { get; private set; } = string.Empty;

    public int PeriodLengthDays { get; private set; }

    public IReadOnlyList<ShiftPlanTemplateEntry> Entries => _entries;

    public Guid CreatedByUserId { get; private set; }

    public static Result<ShiftPlanTemplate> Create(
        Guid tenantId, string name, int periodLengthDays, IReadOnlyList<ShiftPlanTemplateEntry> entries, Guid createdByUserId)
    {
        var template = new ShiftPlanTemplate(Guid.CreateVersion7(), tenantId, createdByUserId);
        var applied = template.Update(name, periodLengthDays, entries);
        return applied.IsFailure ? applied.Error : template;
    }

    public Result Update(string name, int periodLengthDays, IReadOnlyList<ShiftPlanTemplateEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > AttendanceLimits.NameMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftTemplateNameRequired);
        }

        if (periodLengthDays is < 1 or > MaxPeriodLengthDays)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftTemplatePeriodInvalid);
        }

        if (entries is null || !AreValid(entries, periodLengthDays))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftTemplateEntryInvalid);
        }

        Name = name.Trim();
        PeriodLengthDays = periodLengthDays;
        _entries.Clear();
        _entries.AddRange(entries);
        return Result.Success();
    }

    private static bool AreValid(IReadOnlyList<ShiftPlanTemplateEntry> entries, int periodLengthDays) =>
        entries.All(e => e is not null && e.EmployeeId != Guid.Empty && e.DayOffset >= 0 && e.DayOffset < periodLengthDays && Enum.IsDefined(e.DayType))
        && entries.Select(e => (e.EmployeeId, e.DayOffset)).Distinct().Count() == entries.Count;
}

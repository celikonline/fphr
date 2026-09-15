using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.RuleSets;

/// <summary>Kıdem kademesi: completed service years compared with <see cref="AfterYears"/> add <see cref="ExtraDays"/> (cumulative).</summary>
public sealed class LeaveSeniorityTier
{
    public LeaveSeniorityTier()
    {
    }

    public LeaveSeniorityTier(int afterYears, SeniorityComparison comparison, decimal extraDays)
    {
        AfterYears = afterYears;
        Comparison = comparison;
        ExtraDays = extraDays;
    }

    public int AfterYears { get; init; }

    public SeniorityComparison Comparison { get; init; }

    public decimal ExtraDays { get; init; }

    public bool AppliesTo(int completedYears) =>
        Comparison == SeniorityComparison.GreaterThan ? completedYears > AfterYears : completedYears >= AfterYears;
}

/// <summary>The "Yeni Tür ve Kural Ekle" wizard field set (11.3.5 Alanlar), validated as one unit on save.</summary>
public sealed record LeaveTypeSettings(
    LeaveTypeCode Code,
    string Name,
    string? Description,
    bool IsPaid,
    LeaveLimitType LimitType,
    decimal? PerRequestMaxDays = null,
    decimal? AnnualDays = null,
    bool AgeExceptionEnabled = false,
    decimal? AgeExceptionMinDays = null,
    NegativeBalanceMode NegativeBalance = NegativeBalanceMode.None,
    decimal? NegativeBalanceLimitDays = null,
    AccrualFrequency AccrualFrequency = AccrualFrequency.Yearly,
    AccrualTiming AccrualTiming = AccrualTiming.EndOfPeriod,
    AccrualBasis AccrualBasis = AccrualBasis.HireDate,
    CarryOverMode CarryOverMode = CarryOverMode.Reset,
    decimal? CarryOverMaxDays = null,
    IReadOnlyList<LeaveSeniorityTier>? SeniorityTiers = null,
    decimal? MinConsecutiveDays = null,
    decimal? MaxConsecutiveDays = null,
    LeaveUnit Unit = LeaveUnit.FullDay,
    bool RequiresDescription = false,
    bool RequiresSubstitute = false,
    bool RequiresDocument = false,
    int WaitingPeriodMonths = 0,
    bool CountsNonWorkingDays = false,
    bool CountsTowardAccrualService = true,
    bool ShowTypeNameOnCalendar = true,
    bool IsSensitive = false,
    bool AllowSelfServiceRequest = true,
    bool IsDisabled = false,
    bool IsStatutory = false)
{
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > LeaveLimits.NameMaxLength)
        {
            return Error.Validation(LeaveDomainErrors.TypeNameRequired);
        }

        if (LimitType == LeaveLimitType.PerRequest && PerRequestMaxDays is not (>= 0.5m and <= LeaveLimits.MaxDays))
        {
            return Error.Validation(LeaveDomainErrors.TypePerRequestMaxDaysInvalid);
        }

        if (LimitType == LeaveLimitType.PerYear)
        {
            if (AnnualDays is not (> 0m and <= LeaveLimits.MaxDays))
            {
                return Error.Validation(LeaveDomainErrors.TypeAnnualDaysInvalid);
            }

            if (NegativeBalance == NegativeBalanceMode.Limited && (NegativeBalanceLimitDays is not >= 0.5m || NegativeBalanceLimitDays > AnnualDays))
            {
                return Error.Validation(LeaveDomainErrors.TypeNegativeBalanceLimitInvalid);
            }

            if (CarryOverMode == CarryOverMode.Limited && CarryOverMaxDays is not > 0m)
            {
                return Error.Validation(LeaveDomainErrors.TypeCarryOverLimitInvalid);
            }
        }

        if (MinConsecutiveDays is { } min && MaxConsecutiveDays is { } max && min > max)
        {
            return Error.Validation(LeaveDomainErrors.TypeConsecutiveDaysInvalid);
        }

        if (WaitingPeriodMonths is < 0 or > LeaveLimits.MaxWaitingPeriodMonths)
        {
            return Error.Validation(LeaveDomainErrors.TypeWaitingPeriodInvalid);
        }

        if (SeniorityTiers is { Count: > 0 } && Code != LeaveTypeCode.Annual)
        {
            return Error.Validation(LeaveDomainErrors.TypeSeniorityTiersOnlyAnnual);
        }

        return Result.Success();
    }
}

/// <summary>A leave type inside a <see cref="LeaveRuleSet"/> (child entity; changed only through the rule set aggregate).</summary>
public sealed class LeaveType : TenantEntity<Guid>
{
    private List<LeaveSeniorityTier> _seniorityTiers = [];

    private LeaveType()
    {
    }

    internal LeaveType(Guid id, Guid tenantId, Guid ruleSetId, int sortOrder, LeaveTypeSettings settings)
        : base(id, tenantId)
    {
        RuleSetId = ruleSetId;
        SortOrder = sortOrder;
        Apply(settings);
    }

    public Guid RuleSetId { get; private set; }

    public int SortOrder { get; private set; }

    public LeaveTypeCode Code { get; private set; }

    /// <summary>Resource key ("leave.type.annual") for seeded types, free text for custom ones.</summary>
    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsPaid { get; private set; }

    public LeaveLimitType LimitType { get; private set; }

    public decimal? PerRequestMaxDays { get; private set; }

    public decimal? AnnualDays { get; private set; }

    public bool AgeExceptionEnabled { get; private set; }

    public decimal? AgeExceptionMinDays { get; private set; }

    public NegativeBalanceMode NegativeBalance { get; private set; }

    public decimal? NegativeBalanceLimitDays { get; private set; }

    public AccrualFrequency AccrualFrequency { get; private set; }

    public AccrualTiming AccrualTiming { get; private set; }

    public AccrualBasis AccrualBasis { get; private set; }

    public CarryOverMode CarryOverMode { get; private set; }

    public decimal? CarryOverMaxDays { get; private set; }

    public IReadOnlyList<LeaveSeniorityTier> SeniorityTiers => _seniorityTiers.AsReadOnly();

    public decimal? MinConsecutiveDays { get; private set; }

    public decimal? MaxConsecutiveDays { get; private set; }

    public LeaveUnit Unit { get; private set; }

    public bool RequiresDescription { get; private set; }

    public bool RequiresSubstitute { get; private set; }

    public bool RequiresDocument { get; private set; }

    public int WaitingPeriodMonths { get; private set; }

    /// <summary>"Çalışma takvimini geçersiz kıl": count calendar days instead of working days.</summary>
    public bool CountsNonWorkingDays { get; private set; }

    public bool CountsTowardAccrualService { get; private set; }

    /// <summary>Always false when <see cref="IsSensitive"/> (KVKK: health data types show only as "İzinli").</summary>
    public bool ShowTypeNameOnCalendar { get; private set; }

    public bool IsSensitive { get; private set; }

    public bool AllowSelfServiceRequest { get; private set; }

    public bool IsDisabled { get; private set; }

    /// <summary>Seeded statutory type: cannot be deleted, only disabled.</summary>
    public bool IsStatutory { get; private set; }

    public bool HasBalance => LimitType == LeaveLimitType.PerYear;

    public LeaveTypeSettings ToSettings() => new(
        Code, Name, Description, IsPaid, LimitType, PerRequestMaxDays, AnnualDays, AgeExceptionEnabled, AgeExceptionMinDays,
        NegativeBalance, NegativeBalanceLimitDays, AccrualFrequency, AccrualTiming, AccrualBasis, CarryOverMode, CarryOverMaxDays,
        _seniorityTiers.Select(t => new LeaveSeniorityTier(t.AfterYears, t.Comparison, t.ExtraDays)).ToList(),
        MinConsecutiveDays, MaxConsecutiveDays, Unit, RequiresDescription, RequiresSubstitute, RequiresDocument, WaitingPeriodMonths,
        CountsNonWorkingDays, CountsTowardAccrualService, ShowTypeNameOnCalendar, IsSensitive, AllowSelfServiceRequest, IsDisabled, IsStatutory);

    internal void Update(LeaveTypeSettings settings) => Apply(settings with { IsStatutory = IsStatutory });

    internal void SetDisabled(bool disabled) => IsDisabled = disabled;

    private void Apply(LeaveTypeSettings s)
    {
        Code = s.Code;
        Name = s.Name.Trim();
        Description = string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim();
        IsPaid = s.IsPaid;
        LimitType = s.LimitType;
        PerRequestMaxDays = s.LimitType == LeaveLimitType.PerRequest ? s.PerRequestMaxDays : null;

        var perYear = s.LimitType == LeaveLimitType.PerYear;
        AnnualDays = perYear ? s.AnnualDays : null;
        AgeExceptionEnabled = perYear && s.AgeExceptionEnabled;
        AgeExceptionMinDays = AgeExceptionEnabled ? s.AgeExceptionMinDays : null;
        NegativeBalance = perYear ? s.NegativeBalance : NegativeBalanceMode.None;
        NegativeBalanceLimitDays = NegativeBalance == NegativeBalanceMode.Limited ? s.NegativeBalanceLimitDays : null;
        AccrualFrequency = s.AccrualFrequency;
        AccrualTiming = s.AccrualTiming;
        AccrualBasis = s.AccrualBasis;
        CarryOverMode = perYear ? s.CarryOverMode : CarryOverMode.Reset;
        CarryOverMaxDays = CarryOverMode == CarryOverMode.Limited ? s.CarryOverMaxDays : null;
        _seniorityTiers = perYear && s.Code == LeaveTypeCode.Annual && s.SeniorityTiers is not null
            ? s.SeniorityTiers.OrderBy(t => t.AfterYears).Select(t => new LeaveSeniorityTier(t.AfterYears, t.Comparison, t.ExtraDays)).ToList()
            : [];

        MinConsecutiveDays = s.MinConsecutiveDays;
        MaxConsecutiveDays = s.MaxConsecutiveDays;
        Unit = s.Unit;
        RequiresDescription = s.RequiresDescription;
        RequiresSubstitute = s.RequiresSubstitute;
        RequiresDocument = s.RequiresDocument;
        WaitingPeriodMonths = s.WaitingPeriodMonths;
        CountsNonWorkingDays = s.CountsNonWorkingDays;
        CountsTowardAccrualService = s.CountsTowardAccrualService;
        IsSensitive = s.IsSensitive;
        ShowTypeNameOnCalendar = s.ShowTypeNameOnCalendar && !s.IsSensitive;
        AllowSelfServiceRequest = s.AllowSelfServiceRequest;
        IsDisabled = s.IsDisabled;
        IsStatutory = s.IsStatutory;
    }
}

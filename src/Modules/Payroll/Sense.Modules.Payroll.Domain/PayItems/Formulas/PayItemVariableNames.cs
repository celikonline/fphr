namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

/// <summary>The only identifiers a pay-item formula may reference (plan: "İzinli değişkenler"). A formula cannot reference another pay item.</summary>
public static class PayItemVariableNames
{
    /// <summary>Base gross of the period: proration applied, overtime and other pay items excluded.</summary>
    public const string Gross = "brut";

    public const string MonthlyWage = "aylik_ucret";

    /// <summary>Agreed net for net-wage employees; 0 for gross-wage employees.</summary>
    public const string NetTarget = "net_hedef";

    public const string DailyWage = "gunluk_ucret";
    public const string HourlyWage = "saatlik_ucret";
    public const string WorkedDays = "calisilan_gun";
    public const string PaidDays = "ucretli_gun";
    public const string CalendarDays = "takvim_gunu";
    public const string MissingDays = "eksik_gun";
    public const string SeniorityYears = "kidem_yili";
    public const string MinimumWage = "asgari_ucret";
    public const string OvertimeHours = "fazla_mesai_saati";
    public const string Month = "ay";

    /// <summary>The single parameter given when a pay item is assigned to an employee.</summary>
    public const string Value = "deger";

    public static readonly IReadOnlyList<string> All =
    [
        Gross, MonthlyWage, NetTarget, DailyWage, HourlyWage, WorkedDays, PaidDays,
        CalendarDays, MissingDays, SeniorityYears, MinimumWage, OvertimeHours, Month, Value,
    ];

    private static readonly HashSet<string> AllSet = new(All, StringComparer.Ordinal);

    public static bool IsAllowed(string name) => AllSet.Contains(name);
}

namespace Sense.Modules.Performance.Application;

/// <summary>Application layer error codes (localized in the Web layer).</summary>
public static class PerformanceErrors
{
    public const string EmployeeInvalid = "performance.goal.employee_invalid";
    public const string NoEmployeeForUser = "performance.no_employee_for_user";

    /// <summary>Çağıranın performans izni bu çalışanı kapsamıyor (veri kapsamı: Self/Team/Department/... dışında).</summary>
    public const string EmployeeNotInScope = "performance.employee.not_in_scope";
    public const string FormCycleClosed = "performance.form.cycle_closed";

    /// <summary>4857 md.18 fesih delili gerekçesi: kapatılmış bir dönemin (ReviewCycle.Close, terminal) formu bir daha
    /// yayınlanamaz - PublishReviewFormHandler bu kontrolü form kendi Draft/Published durumundan bağımsız, dönem
    /// seviyesinde de uygular (form hâlâ Draft'ta kalıp dönem kapansa bile). G-8-19: aynı kilit, dönemi kapalıyken
    /// hedef ilerleme güncellemesi/iptali için de GoalCommands.cs'te uygulanır.</summary>
    public const string ReviewImmutable = "performance.review.immutable";

    /// <summary>G-8-20: bir hedef bir değerlendirme dönemine bağlıysa (ReviewCycleId dolu), TargetDate dönemin
    /// [StartDate, EndDate] aralığı dışında olamaz.</summary>
    public const string GoalTargetDateOutsideCycle = "performance.goal.target_date_outside_cycle";
}

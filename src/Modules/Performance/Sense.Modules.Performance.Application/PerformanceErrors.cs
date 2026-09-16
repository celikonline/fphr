namespace Sense.Modules.Performance.Application;

/// <summary>Application layer error codes (localized in the Web layer).</summary>
public static class PerformanceErrors
{
    public const string EmployeeInvalid = "performance.goal.employee_invalid";
    public const string NoEmployeeForUser = "performance.no_employee_for_user";
    public const string FormCycleClosed = "performance.form.cycle_closed";

    /// <summary>4857 md.18 fesih delili gerekçesi: kapatılmış bir dönemin (ReviewCycle.Close, terminal) formu bir daha
    /// yayınlanamaz - PublishReviewFormHandler bu kontrolü form kendi Draft/Published durumundan bağımsız, dönem
    /// seviyesinde de uygular (form hâlâ Draft'ta kalıp dönem kapansa bile).</summary>
    public const string ReviewImmutable = "performance.review.immutable";
}

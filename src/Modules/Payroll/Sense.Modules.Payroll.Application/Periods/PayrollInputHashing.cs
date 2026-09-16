using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Profiles;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>
/// The live inputs one payslip calculation used, serialized for <see cref="Domain.Payslips.Payslip.InputSnapshot"/> and
/// hashed for <see cref="Domain.Payslips.Payslip.InputHash"/> (11.3.07-bordro.md "Değişti" mechanism / ChangeDetectionRun,
/// P3). Deliberately narrow: only the fields <see cref="Sense.Modules.Payroll.Application.Calculation.PayrollLegislationSnapshotResolver"/>-independent
/// live data the calculation reads today (salary + payroll profile content) - per the P3 task scope ("just salary +
/// profile version is enough for this slice"), not every conceivable input (Attendance/variable pay items are still
/// placeholders/out of scope, see PayrollCalculationInput.WorkedDays).
/// </summary>
public sealed record PayrollInputSnapshotDto(
    Guid ProfileId,
    WageBasis WageBasis,
    SgkStatus SgkStatus,
    MealBenefitVariant MealBenefitVariant,
    decimal MealDailyAmount,
    TransportVariant TransportVariant,
    PayrollPaymentMethod PaymentMethod,
    Guid? BankAccountId,
    bool SickDaysReducePay,
    decimal SalaryAmount,
    string SalaryCurrency,
    EmployeeSalaryType SalaryType,
    DateOnly SalaryEffectiveFrom);

/// <summary>Builds and hashes the <see cref="PayrollInputSnapshotDto"/> - both the initial calculation (P3 §1/§4) and
/// the periodic ChangeDetectionRun (P3 §3) call this so the two hashes are always computed the same way.</summary>
public static class PayrollInputHashing
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PayrollInputSnapshotDto BuildSnapshot(EmployeePayrollProfile profile, EmployeeCurrentSalary salary) => new(
        profile.Id, profile.WageBasis, profile.SgkStatus, profile.MealBenefitVariant, profile.MealDailyAmount,
        profile.TransportVariant, profile.PaymentMethod, profile.BankAccountId, profile.SickDaysReducePay,
        salary.Amount, salary.Currency, salary.SalaryType, salary.EffectiveFrom);

    public static string Serialize(PayrollInputSnapshotDto snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

    public static string Hash(PayrollInputSnapshotDto snapshot)
    {
        var bytes = Encoding.UTF8.GetBytes(Serialize(snapshot));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

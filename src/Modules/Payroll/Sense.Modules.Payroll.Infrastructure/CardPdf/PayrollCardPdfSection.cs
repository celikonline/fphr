using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Payroll.Application.Garnishments;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Payroll.Infrastructure.CardPdf;

/// <summary>Card PDF "Bordro": current payroll profile, payslips within the period, garnishment orders.</summary>
public sealed class PayrollCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    private const string PayslipsHeading = "Bordro pusulaları";

    public string Key => EmployeeCardPdfSectionKeys.Payroll;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Payroll;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        if (request.IsOwnCard)
        {
            // Self-service has payslips only (no profile/garnishment query for the employee).
            var mine = EmployeeCardPdfSectionResult.Part(
                await dispatcher.Query(new GetMyPayslipsQuery(), cancellationToken),
                rows => Table(
                    PayslipsHeading,
                    ["Dönem", "Brüt", "Net"],
                    rows.Where(r => InMonth(r.Year, r.Month, request))
                        .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                        .Select(r => (IReadOnlyList<string>)[Month(r.Year, r.Month), Amount(r.GrossAmount), Amount(r.NetAmount)]),
                    [1, 2]));
            return EmployeeCardPdfSectionResult.Combine(mine);
        }

        var profileResult = await dispatcher.Query(new GetCurrentEmployeePayrollProfileQuery(request.EmployeeId), cancellationToken);
        var profile = EmployeeCardPdfSectionResult.Part(profileResult, p =>
        [
            KeyValues(
                "Bordro profili",
                ("Geçerlilik", $"{Date(p.ValidFrom)} – {Date(p.ValidTo)}"),
                ("Ücret esası", localizer.EnumText<WageBasis>(p.WageBasis) ?? Empty),
                ("SGK durumu", localizer.EnumText<SgkStatus>(p.SgkStatus) ?? Empty),
                ("Teşvik kanunu", Text(p.IncentiveLawCode)),
                ("Yemek günlük", Amount(p.MealDailyAmount)),
                ("Yol günlük", Amount(p.TransportDailyAmount)),
                ("Ödeme yöntemi", localizer.EnumText<PayrollPaymentMethod>(p.PaymentMethod) ?? Empty),
                ("Hastalıkta kesinti", YesNo(p.SickDaysReducePay)),
                ("Dış bordro kodu", Text(p.ExternalPayrollCode))),
        ]);

        var payslips = EmployeeCardPdfSectionResult.Part(
            await dispatcher.Query(new ListEmployeePayslipsQuery(request.EmployeeId), cancellationToken),
            rows => Table(
                PayslipsHeading,
                ["Dönem", "Brüt", "Net", "İşveren maliyeti", "Durum"],
                rows.Where(r => InMonth(r.Year, r.Month, request))
                    .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                    .Select(r => (IReadOnlyList<string>)
                    [
                        Month(r.Year, r.Month), Amount(r.GrossAmount), Amount(r.NetAmount), Amount(r.EmployerCost),
                        localizer.EnumText<PayrollPeriodStatus>(r.PeriodStatus) ?? Empty,
                    ]),
                [1, 2, 3]));

        var garnishments = EmployeeCardPdfSectionResult.Part(
            await dispatcher.Query(new ListGarnishmentOrdersQuery(request.EmployeeId), cancellationToken),
            rows => Table(
                "İcra / nafaka emirleri",
                ["Tür", "Dosya no", "Aylık taksit", "Kalan", "Başlangıç", "Kapanış"],
                rows.OrderByDescending(r => r.StartDate).Select(r => (IReadOnlyList<string>)
                [
                    localizer.EnumText<GarnishmentType>(r.Type) ?? Empty, r.CaseReference, Amount(r.MonthlyInstallmentAmount),
                    Amount(r.RemainingBalance), Date(r.StartDate), Date(r.ClosedDate),
                ]),
                [2, 3]));

        // No profile yet (NotFound) is a normal state for a visible payroll tab: say so instead of dropping the block.
        var profileMissing = profileResult.IsFailure && profileResult.Error.Type == ErrorType.NotFound;
        if (profile is null && profileMissing && (payslips is not null || garnishments is not null))
        {
            profile = [new SimplePdfParagraph("Bordro profili: kayıt yok.")];
        }

        return EmployeeCardPdfSectionResult.Combine(profile, payslips, garnishments);
    }

    private static bool InMonth(int year, int month, EmployeeCardPdfRequest request)
    {
        var start = new DateOnly(year, month, 1);
        return InPeriod(start, start.AddMonths(1).AddDays(-1), request.From, request.To);
    }

    private static string Month(int year, int month) => $"{year:D4}-{month:D2}";
}

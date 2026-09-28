using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.EmploymentPeriods;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Application.Union;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Employee.Infrastructure.CardPdf;

/// <summary>Base for the Employee-owned card sections: detail lookup (own card → /me query) and enum labels.</summary>
public abstract class EmployeeOwnedCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    protected IDispatcher Dispatcher { get; } = dispatcher;

    public abstract string Key { get; }

    public int Order => 0;

    public string? ModuleKey => null;

    public abstract Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken);

    protected Task<Result<EmployeeDetailDto>> DetailAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        request.IsOwnCard
            ? Dispatcher.Query(new GetMyEmployeeQuery(), cancellationToken)
            : Dispatcher.Query(new GetEmployeeQuery(request.EmployeeId), cancellationToken);

    protected string Label<TEnum>(TEnum? value) where TEnum : struct, Enum => localizer.EnumText(value) ?? Empty;

    protected string Label(string enumName, string? member) => localizer.EnumText(enumName, member) ?? Text(member);
}

public sealed class GeneralCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.General;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
        [
            KeyValues(
                "İş bilgileri",
                ("Ad soyad", e.DisplayName),
                ("Durum", Label("EmployeeStatus", e.Status)),
                ("Departman", Text(e.DepartmentName)),
                ("Unvan", Text(e.TitleName)),
                ("Yönetici", Text(e.ManagerDisplayName)),
                ("İşe giriş", Date(e.HireDate)),
                ("Pozisyon başlangıcı", Date(e.PositionStartDate)),
                ("İzin hak ediş başlangıcı", Date(e.LeaveAccrualStartDate)),
                ("Sözleşme tipi", Label(e.ContractType)),
                ("Çalışma tipi", Label(e.EmploymentType)),
                ("İş e-postası", Text(e.WorkEmail)),
                ("İş telefonu", Text(e.WorkPhone)),
                ("İşten çıkış", Date(e.TerminationDate))),
            KeyValues("Alt işveren", ("Alt işveren", Text(e.SubcontractorName))),
        ]));
}

public sealed class PersonalCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Personal;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        // NationalId arrives masked and the special-category fields null without employee.sensitive.view (EmployeeDetailBuilder).
        var personal = EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
        [
            KeyValues(
                "Kişisel bilgiler",
                ("T.C. kimlik no", Text(e.NationalId)),
                ("Doğum tarihi", Date(e.BirthDate)),
                ("Cinsiyet", Label<Gender>(e.Gender)),
                ("Medeni durum", Label<MaritalStatus>(e.MaritalStatus)),
                ("Çocuk sayısı", e.ChildCount?.ToString() ?? Empty),
                ("Eğitim durumu", Label(e.EducationStatus)),
                ("En yüksek eğitim", Label(e.HighestEducationLevel)),
                ("Askerlik durumu", Label(e.MilitaryStatus)),
                ("Engellilik derecesi", Label(e.DisabilityDegree)),
                ("Kan grubu", Label(e.BloodType))),
            KeyValues(
                "İletişim",
                ("Kişisel e-posta", Text(e.PersonalEmail)),
                ("Cep telefonu", Text(e.MobilePhone)),
                ("Ev telefonu", Text(e.HomePhone)),
                ("Adres", Text(e.AddressLine)),
                ("Şehir", Text(e.City)),
                ("Posta kodu", Text(e.PostalCode))),
        ]);
        if (personal is null)
        {
            return EmployeeCardPdfSectionResult.Unavailable;
        }

        // Union memberships have no self-service query; the own card omits them.
        var union = request.IsOwnCard
            ? null
            : EmployeeCardPdfSectionResult.Part(
                await Dispatcher.Query(new ListUnionMembershipsQuery(request.EmployeeId), cancellationToken),
                rows => Table(
                    "Sendika üyelikleri",
                    ["Sendika", "Üyelik tarihi", "Kapsam", "Temsilci", "Bitiş"],
                    rows.OrderByDescending(r => r.MembershipDate).Select(r => (IReadOnlyList<string>)
                    [
                        r.UnionName, Date(r.MembershipDate), Label<UnionCoverageType>(r.CoverageType), YesNo(r.IsUnionOfficer), Date(r.EndDate),
                    ])));

        return EmployeeCardPdfSectionResult.Combine(personal, union);
    }
}

public sealed class CustomFieldsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.CustomFields;

    private static readonly JsonSerializerOptions MultiSelectJson = new(JsonSerializerDefaults.Web);

    // Custom fields are already filtered by EmployeeDetailBuilder (sensitive fields + AccountShowCustomFields setting).
    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
            e.CustomFields is { Count: > 0 } fields
                ? [KeyValues(null, fields.Select(f => (f.Name, ValueText(f))).ToArray())]
                : Array.Empty<SimplePdfBlock>()));

    /// <summary>
    /// <see cref="CustomFieldValueDto.Value"/> is the canonical string <see cref="CustomFieldValueCoercion"/> stores
    /// (Boolean "true"/"false", MultiSelect a JSON array, Date "yyyy-MM-dd"); render it per <see cref="CustomFieldDataType"/>
    /// instead of printing the raw stored form.
    /// </summary>
    private static string ValueText(CustomFieldValueDto field)
    {
        if (string.IsNullOrWhiteSpace(field.Value))
        {
            return Empty;
        }

        return field.DataType switch
        {
            CustomFieldDataType.Boolean => YesNo(field.Value == "true"),
            CustomFieldDataType.MultiSelect => string.Join(
                ", ", JsonSerializer.Deserialize<List<string>>(field.Value, MultiSelectJson) ?? []),
            CustomFieldDataType.Date => DateOnly.TryParseExact(field.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? Date(date)
                : Text(field.Value),
            _ => Text(field.Value),
        };
    }
}

public sealed class CareerCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Career;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var careerResult = request.IsOwnCard
            ? await Dispatcher.Query(new GetMyCareerQuery(), cancellationToken)
            : await Dispatcher.Query(new GetEmployeeCareerQuery(request.EmployeeId), cancellationToken);
        var career = EmployeeCardPdfSectionResult.Part(careerResult, rows => Table(
            "Kariyer geçmişi",
            ["Başlangıç", "Bitiş", "Değişiklik", "Çalışma tipi", "Yönetici", "Not"],
            rows.OrderByDescending(r => r.ValidFrom).Select(r => (IReadOnlyList<string>)
            [
                Date(r.ValidFrom), Date(r.ValidTo), Label<CareerChangeType>(r.ChangeType), Label(r.EmploymentType),
                Text(r.ManagerDisplayName), Text(r.Note),
            ])));

        // Employment periods have no self-service query.
        var periods = request.IsOwnCard
            ? null
            : EmployeeCardPdfSectionResult.Part(
                await Dispatcher.Query(new GetEmploymentHistoryQuery(request.EmployeeId), cancellationToken),
                history => Table(
                    "İstihdam dönemleri",
                    ["Başlangıç", "Bitiş", "Çıkış nedeni", "Sayılan gün"],
                    history.Periods.OrderByDescending(p => p.StartDate).Select(p => (IReadOnlyList<string>)
                    [
                        Date(p.StartDate), Date(p.EndDate), Text(p.TerminationReasonName), p.CountedDays.ToString(),
                    ]),
                    [3]));

        return EmployeeCardPdfSectionResult.Combine(career, periods);
    }
}

public sealed class EmployeePaymentsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Payments;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var salaryResult = request.IsOwnCard
            ? await Dispatcher.Query(new GetMySalariesQuery(), cancellationToken)
            : await Dispatcher.Query(new GetEmployeeSalariesQuery(request.EmployeeId), cancellationToken);
        var salaries = EmployeeCardPdfSectionResult.Part(salaryResult, rows => Table(
            "Maaş geçmişi",
            ["Geçerlilik", "Tutar", "Maaş tipi", "Ödeme sıklığı", "Yol", "Yemek", "Güncel"],
            rows.OrderByDescending(r => r.EffectiveFrom).Select(r => (IReadOnlyList<string>)
            [
                Date(r.EffectiveFrom), Amount(r.Amount, r.Currency), Label<SalaryType>(r.SalaryType), Label<PayFrequency>(r.PayFrequency),
                Amount(r.TransportAllowance), Amount(r.MealAllowance), YesNo(r.IsCurrent),
            ]),
            [1, 4, 5]));

        // IBANs arrive masked without employee.sensitive.view (the own card sees its own IBAN unmasked, as on screen).
        var bankResult = request.IsOwnCard
            ? await Dispatcher.Query(new GetMyBankAccountsQuery(), cancellationToken)
            : await Dispatcher.Query(new GetEmployeeBankAccountsQuery(request.EmployeeId), cancellationToken);
        var banks = EmployeeCardPdfSectionResult.Part(bankResult, rows => Table(
            "Banka hesapları",
            ["IBAN", "Banka", "Hesap tipi", "Para birimi", "Birincil"],
            rows.Select(r => (IReadOnlyList<string>)
            [
                r.Iban, Text(r.BankName), Label<BankAccountType>(r.AccountType), Text(r.Currency), YesNo(r.IsPrimary),
            ])));

        return EmployeeCardPdfSectionResult.Combine(salaries, banks);
    }
}

public sealed class AccountCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Account;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
        [
            KeyValues(
                null,
                ("Hesap durumu", Label<UserAccountStatus>(e.AccountStatus)),
                ("Erişim tipi", Text(e.AccessType)),
                ("Son davet", Timestamp(e.LastInvitationSentAt))),
        ]));
}

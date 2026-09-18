using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Employee.Application.Import;

/// <summary>
/// Option lists for the "employee.master" template's enum columns ("seçenek değerleri enum listeleriyle eşleşir",
/// 11.3.03 E6). A column offers the Turkish label an HR user picks in the generated workbook; <see cref="TryParse{T}"/>
/// also accepts the enum member name, so a file exported from the API (which serializes enums as their names) can be
/// re-imported unchanged.
/// </summary>
public static class EmployeeImportOptions
{
    /// <summary>Labels positioned by enum value; <see cref="NotOffered"/> marks a member the template does not offer (an empty cell means the same).</summary>
    private const string NotOffered = "";

    private static readonly Dictionary<Type, string[]> LabelsByValue = new()
    {
        [typeof(Gender)] = [NotOffered, "Erkek", "Kadın", "Diğer"],
        [typeof(MaritalStatus)] = [NotOffered, "Bekar", "Evli", "Boşanmış"],
        [typeof(EducationStatus)] = ["Öğrenci", "Mezun"],
        [typeof(HighestEducationLevel)] = ["İlkokul", "Ortaokul", "Lise", "Ön lisans", "Lisans", "Yüksek lisans", "Doktora"],
        [typeof(MilitaryStatus)] = ["Yapılmadı", "Yapıldı", "Tecilli", "Muaf", "Bakaya", "Yoklama kaçağı"],
        [typeof(DisabilityDegree)] = ["Yok", "1. derece", "2. derece", "3. derece"],
        [typeof(BloodType)] = ["0 Rh+", "0 Rh-", "A Rh+", "A Rh-", "B Rh+", "B Rh-", "AB Rh+", "AB Rh-"],
        [typeof(ContractType)] = ["Belirsiz süreli", "Belirli süreli"],
        [typeof(EmploymentType)] =
        [
            "Tam zamanlı", "Yarı zamanlı", "Geçici", "Sözleşmeli", "Serbest çalışan", "Stajyer",
            "Seyyar", "Çırak", "Mevsimlik", "Geçici iş ilişkisi (Büro)", "Alt işveren",
        ],
        [typeof(SalaryType)] = ["Net", "Brüt"],
        [typeof(PayFrequency)] = ["Aylık", "Günlük", "Yıllık", "Haftalık", "Saatlik"],
        // Enum order (BankAccountType): Cheque, TimeDeposit, DemandDeposit, Other - same Turkish labels as the web card's payments-section.tsx.
        [typeof(BankAccountType)] = ["Çek", "Vadeli", "Vadesiz", "Diğer"],
    };

    /// <summary>The values a column offers, in enum order.</summary>
    public static IReadOnlyList<string> For<T>()
        where T : struct, Enum =>
        LabelsByValue[typeof(T)].Where(label => label.Length > 0).ToList();

    /// <summary>Resolves a cell's text to <typeparamref name="T"/> by its Turkish label, then by the enum member name.</summary>
    public static bool TryParse<T>(string text, out T value)
        where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var labels = LabelsByValue[typeof(T)];
        for (var index = 0; index < labels.Length; index++)
        {
            if (labels[index].Length > 0 && ImportValueParser.TextEquals(labels[index], text))
            {
                value = (T)Enum.ToObject(typeof(T), index);
                return true;
            }
        }

        return Enum.TryParse(text.Trim(), ignoreCase: true, out value) && Enum.IsDefined(value);
    }
}

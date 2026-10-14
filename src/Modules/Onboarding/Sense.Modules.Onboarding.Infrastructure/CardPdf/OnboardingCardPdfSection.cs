using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Onboarding.Infrastructure.CardPdf;

/// <summary>Prints onboarding and offboarding task progress for an employee.</summary>
public sealed class OnboardingCardPdfSection(IDispatcher dispatcher) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Onboarding;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Onboarding;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Query(new GetEmployeeCategoryProgressQuery(request.EmployeeId), cancellationToken);
        return new EmployeeCardPdfSectionResult(EmployeeCardPdfSectionResult.Part(result, categories => Table(
            "İşe alış ve işten çıkış ilerlemesi",
            ["Kategori", "Tamamlanan", "Toplam", "İlerleme"],
            categories.Select(category => (IReadOnlyList<string>)[
                Text(category.Name),
                category.Done.ToString(),
                category.Total.ToString(),
                $"{category.Percent}%",
            ]),
            [1, 2, 3])));
    }
}

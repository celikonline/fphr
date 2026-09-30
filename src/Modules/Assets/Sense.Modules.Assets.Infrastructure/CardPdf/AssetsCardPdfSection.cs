using Microsoft.Extensions.Localization;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Assets.Infrastructure.CardPdf;

/// <summary>Card PDF "Zimmetler": assets held during the period; still-held and pending assignments always listed.</summary>
public sealed class AssetsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Assets;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Assets;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = request.IsOwnCard
            ? await dispatcher.Query(new GetMyAssetsQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeAssetsQuery(request.EmployeeId), cancellationToken);
        return EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(result, rows => Table(
            "Zimmetler",
            ["Varlık", "Seri no", "Durum", "Teslim", "İade", "Yenileme"],
            rows.Where(a => a.IssuedAt is not { } issued
                    || InPeriod(issued, a.ReturnedAt ?? DateOnly.MaxValue, request.From, request.To))
                .OrderByDescending(a => a.IssuedAt ?? DateOnly.MaxValue)
                .Select(a => (IReadOnlyList<string>)
                [
                    a.Name, Text(a.SerialNumber), localizer.EnumText<AssetStatus>(a.Status) ?? Empty,
                    Date(a.IssuedAt), Date(a.ReturnedAt), Date(a.ReplacementDueOn),
                ]))));
    }
}

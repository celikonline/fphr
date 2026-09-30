using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Assets.Infrastructure.CardPdf;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

public sealed class AssetsCardPdfSectionTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static IStringLocalizer<SharedResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        return localizer;
    }

    private static AssetDto Asset(string name, DateOnly? issued, DateOnly? returned) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, "SN-1", null, AssetStatus.Assigned, EmployeeId, issued, returned, null);

    [Fact]
    public async Task Assets_FiltersByPeriod_KeepsOpenAndPendingAssignments()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeAssetsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<AssetDto>>(
            [
                Asset("Laptop", new DateOnly(2026, 2, 1), null),
                Asset("Monitör", new DateOnly(2024, 1, 1), null),
                Asset("Telefon", new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1)),
                Asset("Kart", null, null),
            ])));
        var request = new EmployeeCardPdfRequest(Guid.NewGuid(), EmployeeId, false, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var result = await new AssetsCardPdfSection(dispatcher, Localizer()).BuildAsync(request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single().Rows.Select(r => r[0])
            .ShouldBe(["Laptop", "Monitör", "Kart"], ignoreOrder: true);
    }

    [Fact]
    public async Task Assets_Forbidden_IsUnavailable()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeAssetsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<IReadOnlyList<AssetDto>>(Error.Forbidden("auth.forbidden"))));

        var result = await new AssetsCardPdfSection(dispatcher, Localizer())
            .BuildAsync(new EmployeeCardPdfRequest(Guid.NewGuid(), EmployeeId, false, null, null), CancellationToken.None);

        result.IsAvailable.ShouldBeFalse();
    }
}

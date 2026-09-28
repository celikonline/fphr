using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Documents.Infrastructure.CardPdf;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class DocumentsCardPdfSectionTests
{
    [Fact]
    public async Task Documents_OwnCard_UsesMyQueryAndListsRequirements()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        var dispatcher = Substitute.For<IDispatcher>();
        var employeeId = Guid.NewGuid();
        dispatcher.Query(Arg.Any<GetMyDocumentRequirementsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<EmployeeDocumentRequirementDto>>(
            [
                new(Guid.NewGuid(), employeeId, Guid.NewGuid(), "HEALTH", "Sağlık raporu", DocumentTypeStage.Onboarding,
                    DocumentRequirementStatus.Missing, new DateOnly(2026, 10, 1), null, null),
            ])));

        var result = await new DocumentsCardPdfSection(dispatcher, localizer)
            .BuildAsync(new EmployeeCardPdfRequest(Guid.NewGuid(), employeeId, true, null, null), CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single().Rows.ShouldHaveSingleItem()[0].ShouldBe("Sağlık raporu");
        await dispatcher.DidNotReceive().Query(Arg.Any<GetEmployeeDocumentRequirementsQuery>(), Arg.Any<CancellationToken>());
    }
}

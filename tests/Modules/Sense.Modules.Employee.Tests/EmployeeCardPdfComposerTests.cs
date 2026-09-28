using NSubstitute;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.CardPdf;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

public sealed class EmployeeCardPdfComposerTests
{
    private static readonly EmployeeCardPdfRequest Request =
        new(Guid.NewGuid(), Guid.NewGuid(), false, new DateOnly(2025, 9, 28), new DateOnly(2026, 9, 28));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSection(string key, int order, string? module, EmployeeCardPdfSectionResult result) : IEmployeeCardPdfSection
    {
        public int Calls { get; private set; }

        public string Key => key;

        public int Order => order;

        public string? ModuleKey => module;

        public Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static EmployeeCardPdfSectionResult Text(string text) => new([new SimplePdfParagraph(text)]);

    private static FakeSection Fake(string key, string text) => new(key, 0, null, Text(text));

    private static (EmployeeCardPdfComposer Composer, Func<SimplePdfDocument?> Document) Composer(
        IEnumerable<IEmployeeCardPdfSection> sections, params string[] disabledModules)
    {
        var modules = Substitute.For<IModuleAccessService>();
        modules.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(!disabledModules.Contains(call.Arg<string>())));
        SimplePdfDocument? captured = null;
        var writer = Substitute.For<ISimplePdfWriter>();
        writer.Write(Arg.Do<SimplePdfDocument>(d => captured = d)).Returns([1]);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
        return (new EmployeeCardPdfComposer(sections, modules, writer, clock), () => captured);
    }

    private static List<string> Headings(SimplePdfDocument document) =>
        document.Blocks.OfType<SimplePdfSectionHeading>().Select(h => h.Text).ToList();

    [Fact]
    public void GetEmployeeCardPdfQuery_IsScopedToTargetEmployee()
    {
        var id = Guid.NewGuid();
        IScopedRequest query = new GetEmployeeCardPdfQuery(id, ["general"], null, null);

        query.TargetEmployeeId.ShouldBe(id);
        typeof(GetEmployeeCardPdfQuery).GetCustomAttributes(typeof(RequiresPermissionAttribute), false)
            .Cast<RequiresPermissionAttribute>().ShouldHaveSingleItem().Permission.ShouldBe(EmployeePermissions.EmployeesReadKey);
    }

    [Fact]
    public async Task Compose_OrdersSectionsByCardOrder_NotRequestOrder()
    {
        var (composer, document) = Composer([Fake("leave", "İzin"), Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["leave", "general"], CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Headings(document()!).ShouldBe(["Genel Bilgiler", "İzinler"]);
    }

    [Fact]
    public async Task Compose_OmitsSectionsNotRequested()
    {
        var (composer, document) = Composer([Fake("leave", "İzin"), Fake("general", "Genel")]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        Headings(document()!).ShouldBe(["Genel Bilgiler"]);
    }

    [Fact]
    public async Task Compose_DropsUnavailableSection()
    {
        var hidden = new FakeSection("payroll", 0, null, EmployeeCardPdfSectionResult.Unavailable);
        var (composer, document) = Composer([Fake("general", "Genel"), hidden]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general", "payroll"], CancellationToken.None);

        Headings(document()!).ShouldBe(["Genel Bilgiler"]);
    }

    [Fact]
    public async Task Compose_WhenModuleDisabled_SkipsProviderWithoutCallingIt()
    {
        var leave = new FakeSection("leave", 0, ProductModuleKeys.Leave, Text("İzin"));
        var (composer, document) = Composer([Fake("general", "Genel"), leave], ProductModuleKeys.Leave);

        await composer.ComposeAsync(Request, "Ali Veli", ["general", "leave"], CancellationToken.None);

        leave.Calls.ShouldBe(0);
        Headings(document()!).ShouldBe(["Genel Bilgiler"]);
    }

    [Fact]
    public async Task Compose_JoinsProvidersOfSameKeyByOrder()
    {
        var (composer, document) = Composer(
            [new FakeSection("payments", 10, null, Text("B")), new FakeSection("payments", 0, null, Text("A"))]);

        await composer.ComposeAsync(Request, "Ali Veli", ["payments"], CancellationToken.None);

        var texts = document()!.Blocks.OfType<SimplePdfParagraph>().Select(p => p.Text).ToList();
        texts.IndexOf("A").ShouldBeLessThan(texts.IndexOf("B"));
    }

    [Fact]
    public async Task Compose_EmptySection_PrintsNoRecordLine()
    {
        var (composer, document) = Composer([new FakeSection("general", 0, null, new EmployeeCardPdfSectionResult([]))]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        document()!.Blocks.OfType<SimplePdfParagraph>().ShouldContain(p => p.Text == "Bu bölümde kayıt yok.");
    }

    [Fact]
    public async Task Compose_WhenEverySectionIsUnavailable_IsForbidden()
    {
        var (composer, _) = Composer([new FakeSection("payroll", 0, null, EmployeeCardPdfSectionResult.Unavailable)]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["payroll"], CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfNoSections);
    }

    [Fact]
    public async Task Compose_NoSections_IsValidationError()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", [], CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfSectionsRequired);
    }

    [Fact]
    public async Task Compose_UnknownSection_IsValidationError()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["general", "other"], CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfUnknownSection);
    }

    [Fact]
    public async Task Compose_FromAfterTo_IsValidationError()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);
        var reversed = Request with { From = new DateOnly(2026, 9, 28), To = new DateOnly(2026, 1, 1) };

        var result = await composer.ComposeAsync(reversed, "Ali Veli", ["general"], CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfPeriodInvalid);
    }

    [Fact]
    public async Task Compose_FileName_IsTurkishSlugWithDate()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Şule Çağlar", ["general"], CancellationToken.None);

        result.Value.FileName.ShouldBe("calisan-karti-sule-caglar-2026-09-28.pdf");
        result.Value.ContentType.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task Compose_SummaryShowsNameAndPeriod()
    {
        var (composer, document) = Composer([Fake("general", "Genel")]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        var summary = document()!.Blocks[0].ShouldBeOfType<SimplePdfKeyValues>();
        summary.Rows.ShouldContain(r => r.Key == "Çalışan" && r.Value == "Ali Veli");
        summary.Rows.ShouldContain(r => r.Key == "Dönem" && r.Value == "28.09.2025 – 28.09.2026");
    }

    [Fact]
    public async Task Compose_WithRealWriter_ProducesPdfBytes()
    {
        var modules = Substitute.For<IModuleAccessService>();
        modules.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var composer = new EmployeeCardPdfComposer([Fake("general", "Genel")], modules, new QuestPdfSimpleWriter(), TimeProvider.System);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        System.Text.Encoding.ASCII.GetString(result.Value.Content, 0, 4).ShouldBe("%PDF");
    }
}

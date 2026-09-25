using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Documents.Application.PayslipImports;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests.PayslipImports;

public sealed class PayslipBulkImportTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Mehmet = Guid.NewGuid();
    private static readonly Guid Gone = Guid.NewGuid();

    private static readonly byte[] Pdf = "%PDF-1.4 fake"u8.ToArray();

    private sealed class FakeResolver : IPayslipEmployeeResolver
    {
        public Task<PayslipEmployeeMatch> ResolveAsync(Guid tenantId, string registrationNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(registrationNumber.ToUpperInvariant() switch
            {
                "A100" => new PayslipEmployeeMatch(PayslipEmployeeMatchKind.Active, Ayse),
                "M200" => new PayslipEmployeeMatch(PayslipEmployeeMatchKind.Active, Mehmet),
                "T300" => new PayslipEmployeeMatch(PayslipEmployeeMatchKind.Terminated, Gone),
                _ => PayslipEmployeeMatch.Unknown,
            });
    }

    private static (ImportPayslipFilesHandler Handler, ISystemDocumentDeliveryService Delivery, IIntegrationEventOutbox Outbox) Create()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var delivery = Substitute.For<ISystemDocumentDeliveryService>();
        delivery.DeliverGeneratedDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Success(Guid.NewGuid())));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new ImportPayslipFilesHandler(
            new PayslipFileMatcher(new FakeResolver()), delivery, outbox, tenant, NullLogger<ImportPayslipFilesHandler>.Instance);
        return (handler, delivery, outbox);
    }

    private static PayslipUploadFile File(string name, byte[]? content = null) => new(name, content ?? Pdf);

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(content);
            }
        }

        return ms.ToArray();
    }

    private static Task<Result<PayslipImportResult>> Run(ImportPayslipFilesHandler h, params PayslipUploadFile[] files) =>
        h.Handle(new ImportPayslipFilesCommand(files), CancellationToken.None);

    [Fact]
    public async Task Matches_by_registration_number()
    {
        var (handler, delivery, outbox) = Create();

        var result = await Run(handler, File("a100_2026-08.pdf"), File("M200_2026-08.pdf"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(2);
        result.Value.Files.Select(f => f.EmployeeId).ShouldBe([Ayse, Mehmet], ignoreOrder: true);
        await delivery.Received(1).DeliverGeneratedDocumentAsync(
            TenantId, Ayse, Arg.Any<string>(), "a100_2026-08.pdf", "application/pdf", Arg.Any<byte[]>(),
            "Payroll", Arg.Is<string>(r => r.EndsWith("2026-08")), Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(Arg.Is<IIntegrationEvent>(e =>
            e is PayslipDocumentUploaded && ((PayslipDocumentUploaded)e).EmployeeId == Ayse && ((PayslipDocumentUploaded)e).Month == 8));
    }

    [Fact]
    public async Task Duplicate_registration_files_reported_not_imported()
    {
        var (handler, delivery, _) = Create();

        var result = await Run(handler, File("A100_2026-08.pdf"), File("a100_2026-08.pdf"), File("M200_2026-08.pdf"));

        result.Value.Imported.ShouldBe(1);
        result.Value.Files.Count(f => f.Status == PayslipImportFileStatus.Duplicate).ShouldBe(2);
        await delivery.DidNotReceive().DeliverGeneratedDocumentAsync(
            Arg.Any<Guid>(), Ayse, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Same_registration_in_different_months_is_not_a_duplicate()
    {
        var (handler, _, _) = Create();

        var result = await Run(handler, File("A100_2026-07.pdf"), File("A100_2026-08.pdf"));

        result.Value.Imported.ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_registration_reported()
    {
        var (handler, delivery, outbox) = Create();

        var result = await Run(handler, File("X999_2026-08.pdf"));

        result.IsSuccess.ShouldBeTrue();
        var line = result.Value.Files.Single();
        line.Status.ShouldBe(PayslipImportFileStatus.Unmatched);
        line.Reason.ShouldBe(PayslipImportReasons.UnknownRegistration);
        await delivery.DidNotReceiveWithAnyArgs().DeliverGeneratedDocumentAsync(default, default, default!, default!, default!, default!, default!, default!, default);
        outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task Terminated_employee_reported()
    {
        var (handler, delivery, _) = Create();

        var result = await Run(handler, File("T300_2026-08.pdf"));

        var line = result.Value.Files.Single();
        line.Status.ShouldBe(PayslipImportFileStatus.Terminated);
        line.Reason.ShouldBe(PayslipImportReasons.TerminatedEmployee);
        await delivery.DidNotReceiveWithAnyArgs().DeliverGeneratedDocumentAsync(default, default, default!, default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Zip_with_non_pdf_skipped_with_reason()
    {
        var (handler, _, _) = Create();
        var zip = Zip(("A100_2026-08.pdf", Pdf), ("notes.txt", "hello"u8.ToArray()), ("B_2026-08.pdf", "not really pdf"u8.ToArray()), ("bad-name.pdf", Pdf));

        var result = await Run(handler, File("payslips.zip", zip));

        result.Value.Imported.ShouldBe(1);
        result.Value.Files.Single(f => f.FileName == "notes.txt").Reason.ShouldBe(PayslipImportReasons.NotPdf);
        result.Value.Files.Single(f => f.FileName == "B_2026-08.pdf").Reason.ShouldBe(PayslipImportReasons.NotPdf);
        result.Value.Files.Single(f => f.FileName == "bad-name.pdf").Reason.ShouldBe(PayslipImportReasons.InvalidFileName);
    }

    [Fact]
    public async Task Corrupt_zip_reported_not_thrown()
    {
        var (handler, _, _) = Create();

        var result = await Run(handler, File("broken.zip", "PK garbage"u8.ToArray()));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Files.Single().Reason.ShouldBe(PayslipImportReasons.ZipCorrupt);
    }

    [Fact]
    public async Task Result_report_lists_every_file()
    {
        var (handler, _, _) = Create();
        var zip = Zip(("M200_2026-08.pdf", Pdf), ("readme.txt", "x"u8.ToArray()));

        var result = await Run(handler, File("A100_2026-08.pdf"), File("X999_2026-08.pdf"), File("T300_2026-08.pdf"), File("all.zip", zip));

        result.Value.Total.ShouldBe(5);
        result.Value.Files.Count.ShouldBe(5);
        result.Value.Imported.ShouldBe(2);
        result.Value.Files.Select(f => f.Status).ShouldBe(
            [PayslipImportFileStatus.Skipped, PayslipImportFileStatus.Imported, PayslipImportFileStatus.Unmatched, PayslipImportFileStatus.Terminated, PayslipImportFileStatus.Imported],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Delivery_failure_is_reported_and_raises_no_event()
    {
        var (handler, delivery, outbox) = Create();
        delivery.DeliverGeneratedDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Failure<Guid>(Error.Failure("x"))));

        var result = await Run(handler, File("A100_2026-08.pdf"));

        result.Value.Files.Single().Status.ShouldBe(PayslipImportFileStatus.Failed);
        outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Theory]
    [InlineData("A100_2026-08.pdf", true)]
    [InlineData("A-1.00_2026-12.PDF", true)]
    [InlineData("A100_2026-13.pdf", false)]
    [InlineData("A100_2026-8.pdf", false)]
    [InlineData("A100.pdf", false)]
    [InlineData("A100_1899-05.pdf", false)]
    public void ParseFileName_follows_K8_pattern(string name, bool valid) =>
        PayslipFileMatcher.ParseFileName(name).HasValue.ShouldBe(valid);
}

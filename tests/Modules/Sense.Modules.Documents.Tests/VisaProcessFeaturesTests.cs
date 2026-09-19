using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaProcessFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IVisaDocumentProcessRepository _processes = Substitute.For<IVisaDocumentProcessRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDocumentsUnitOfWork _unitOfWork = Substitute.For<IDocumentsUnitOfWork>();

    public VisaProcessFeaturesTests()
    {
        _tenant.TenantId.Returns(TenantId);
    }

    [Fact]
    public async Task ListActive_WhenTenantHasNoProcesses_SeedsTheDefaultTemplates()
    {
        _processes.AnyAsync(TenantId, Arg.Any<CancellationToken>()).Returns(false);
        _processes.GetActiveAsync(TenantId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<VisaDocumentProcess>)[]);

        var result = await new ListVisaDocumentProcessesHandler(_processes, _tenant, _unitOfWork)
            .Handle(new ListVisaDocumentProcessesQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _processes.Received(4).AddAsync(Arg.Is<VisaDocumentProcess>(p => p.TenantId == TenantId && p.IsActive), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListActive_WhenTenantAlreadyHasProcesses_DoesNotSeedAgain()
    {
        _processes.AnyAsync(TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _processes.GetActiveAsync(TenantId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<VisaDocumentProcess>)[]);

        await new ListVisaDocumentProcessesHandler(_processes, _tenant, _unitOfWork)
            .Handle(new ListVisaDocumentProcessesQuery(), CancellationToken.None);

        await _processes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Create_NormalizesCountryAndPersists()
    {
        var result = await new CreateVisaDocumentProcessHandler(_processes, _tenant)
            .Handle(new CreateVisaDocumentProcessCommand("  Japonya vizesi ", "jp"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Japonya vizesi");
        result.Value.CountryCode.ShouldBe("JP");
        await _processes.Received(1).AddAsync(Arg.Any<VisaDocumentProcess>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithDuplicateName_IsRejected()
    {
        _processes.NameExistsAsync(TenantId, "Schengen vizesi", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new CreateVisaDocumentProcessHandler(_processes, _tenant)
            .Handle(new CreateVisaDocumentProcessCommand("Schengen vizesi", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentProcessAlreadyExists);
        await _processes.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Update_RenamesProcessAndExcludesItselfFromTheDuplicateCheck()
    {
        var process = VisaDocumentProcess.Create(TenantId, "Eski ad").Value;
        _processes.GetByIdAsync(process.Id, Arg.Any<CancellationToken>()).Returns(process);

        var result = await new UpdateVisaDocumentProcessHandler(_processes, _tenant)
            .Handle(new UpdateVisaDocumentProcessCommand(process.Id, "Yeni ad", "de"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        process.Name.ShouldBe("Yeni ad");
        process.CountryCode.ShouldBe("DE");
        await _processes.Received(1).NameExistsAsync(TenantId, "Yeni ad", process.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_UnknownProcess_IsNotFound()
    {
        var result = await new UpdateVisaDocumentProcessHandler(_processes, _tenant)
            .Handle(new UpdateVisaDocumentProcessCommand(Guid.NewGuid(), "Ad", null), CancellationToken.None);

        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentProcessNotFound);
    }

    [Fact]
    public async Task SetActive_TogglesTheProcess()
    {
        var process = VisaDocumentProcess.Create(TenantId, "Ad").Value;
        _processes.GetByIdAsync(process.Id, Arg.Any<CancellationToken>()).Returns(process);
        var handler = new SetVisaDocumentProcessActiveHandler(_processes);

        var deactivated = await handler.Handle(new SetVisaDocumentProcessActiveCommand(process.Id, false), CancellationToken.None);
        deactivated.Value.IsActive.ShouldBeFalse();

        var reactivated = await handler.Handle(new SetVisaDocumentProcessActiveCommand(process.Id, true), CancellationToken.None);
        reactivated.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void CreateValidator_RejectsNonLetterCountryCodes()
    {
        var validator = new CreateVisaDocumentProcessValidator();

        validator.Validate(new CreateVisaDocumentProcessCommand("Ad", "D1")).IsValid.ShouldBeFalse();
        validator.Validate(new CreateVisaDocumentProcessCommand("Ad", null)).IsValid.ShouldBeTrue();
        validator.Validate(new CreateVisaDocumentProcessCommand("Ad", "  ")).IsValid.ShouldBeTrue();
    }
}

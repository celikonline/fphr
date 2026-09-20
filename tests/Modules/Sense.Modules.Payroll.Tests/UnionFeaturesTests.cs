using NSubstitute;
using Sense.Modules.Payroll.Application.Unions;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Unions;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Handler-level tests for <see cref="CreateUnionHandler"/>/<see cref="UpdateUnionHandler"/>/<see cref="DeactivateUnionHandler"/>
/// (Faz 3 #58). Repository is an NSubstitute fake, no DB.</summary>
public sealed class UnionFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    [Fact]
    public async Task CreateUnionHandler_WithValidName_AddsUnionAndReturnsItsId()
    {
        var unions = Substitute.For<IUnionRepository>();
        var handler = new CreateUnionHandler(Tenant(), unions);

        var result = await handler.Handle(new CreateUnionCommand("Öz Büro-İş", "10.01"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        unions.Received(1).Add(Arg.Is<Union>(u => u.Name == "Öz Büro-İş" && u.TenantId == TenantId));
    }

    [Fact]
    public async Task CreateUnionHandler_WithEmptyName_ReturnsValidationError()
    {
        var unions = Substitute.For<IUnionRepository>();
        var handler = new CreateUnionHandler(Tenant(), unions);

        var result = await handler.Handle(new CreateUnionCommand("", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionNameRequired);
        unions.DidNotReceive().Add(Arg.Any<Union>());
    }

    [Fact]
    public async Task UpdateUnionHandler_WhenUnionMissing_ReturnsNotFound()
    {
        var unions = Substitute.For<IUnionRepository>();
        unions.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Union?>(null));
        var handler = new UpdateUnionHandler(unions);

        var result = await handler.Handle(new UpdateUnionCommand(Guid.NewGuid(), "Ad", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.UnionNotFound);
    }

    [Fact]
    public async Task DeactivateUnionHandler_WhenUnionExists_DeactivatesIt()
    {
        var union = Union.Create(TenantId, "Sendika", null).Value;
        var unions = Substitute.For<IUnionRepository>();
        unions.FindByIdAsync(union.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Union?>(union));
        var handler = new DeactivateUnionHandler(unions);

        var result = await handler.Handle(new DeactivateUnionCommand(union.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        union.IsActive.ShouldBeFalse();
    }
}

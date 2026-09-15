using System.Reflection;
using Sense.Shared.Infrastructure.Observability;
using Shouldly;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.Observability;

public sealed class SenseMetersTests
{
    [Theory]
    [InlineData("Sense.Modules.Identity.Application.Auth", "Identity")]
    [InlineData("Sense.Modules.Organization", "Organization")]
    [InlineData("Sense.Shared.Infrastructure.Messaging", "shared")]
    [InlineData("System", "shared")]
    public void ModuleOf_ExtractsModuleName_FromNamespace(string ns, string expected) =>
        SenseMeters.ModuleOf(new NamespacedType(ns)).ShouldBe(expected);

    /// <summary>Metrik etiketi yalnızca isim alanına bakar; test için gerçek tip tanımlamadan isim alanı taklit edilir.</summary>
    private sealed class NamespacedType(string ns) : TypeDelegator(typeof(object))
    {
        public override string? Namespace => ns;
    }
}

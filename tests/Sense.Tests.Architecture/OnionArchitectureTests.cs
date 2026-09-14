using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Sense.Tests.Architecture;

/// <summary>ADR 0002 (Onion) ve ADR 0001 (modül sınırları) kuralları. Yeni modül eklendikçe otomatik kapsanır.</summary>
public sealed class OnionArchitectureTests
{
    private const string Prefix = "Sense.";
    private const string ModulesPrefix = "Sense.Modules.";
    private const string DomainSuffix = ".Domain";
    private const string ApplicationSuffix = ".Application";
    private const string InfrastructureSuffix = ".Infrastructure";
    private const string ContractsSuffix = ".Contracts";
    private const string ApiSuffix = ".Api";
    private const string KernelAssembly = "Sense.Shared.Kernel";
    private const string SharedContracts = "Sense.Shared.Contracts";
    private const string SharedInfrastructure = "Sense.Shared.Infrastructure";
    private const string SharedWeb = "Sense.Shared.Web";
    private const string SystemPrefix = "System";
    private const string MicrosoftPrefix = "Microsoft";

    private static readonly Assembly[] All = LoadSolutionAssemblies();

    public static IEnumerable<object[]> DomainAssemblies() => Named(DomainSuffix).Select(a => new object[] { a.GetName().Name! });

    public static IEnumerable<object[]> ApplicationAssemblies() => Named(ApplicationSuffix).Select(a => new object[] { a.GetName().Name! });

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_DependsOnlyOnKernel(string assemblyName)
    {
        var asm = Find(assemblyName);
        var references = asm.GetReferencedAssemblies().Select(r => r.Name!)
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        references.ShouldAllBe(n => n == KernelAssembly, $"{assemblyName} yalnız {KernelAssembly} referans alabilir");

        var external = asm.GetReferencedAssemblies().Select(r => r.Name!)
            .Where(n => !n.StartsWith(SystemPrefix, StringComparison.Ordinal) && !n.StartsWith(MicrosoftPrefix, StringComparison.Ordinal) && !n.StartsWith(Prefix, StringComparison.Ordinal) && n != "netstandard" && n != "mscorlib")
            .ToList();
        external.ShouldBeEmpty($"{assemblyName} NuGet paketi referans alamaz");
    }

    [Theory]
    [MemberData(nameof(ApplicationAssemblies))]
    public void Application_DoesNotDependOnInfrastructureOrApi(string assemblyName)
    {
        var asm = Find(assemblyName);
        var forbidden = asm.GetReferencedAssemblies().Select(r => r.Name!)
            .Where(n => n.EndsWith(InfrastructureSuffix, StringComparison.Ordinal) || n.EndsWith(ApiSuffix, StringComparison.Ordinal) || n == SharedWeb)
            .ToList();
        forbidden.ShouldBeEmpty($"{assemblyName} Infrastructure/Api katmanına bağımlı olamaz");
    }

    [Fact]
    public void Modules_TalkOnlyThroughContracts()
    {
        foreach (var asm in All.Where(a => a.GetName().Name!.StartsWith(ModulesPrefix, StringComparison.Ordinal)))
        {
            var own = ModuleOf(asm.GetName().Name!);
            var crossModule = asm.GetReferencedAssemblies().Select(r => r.Name!)
                .Where(n => n.StartsWith(ModulesPrefix, StringComparison.Ordinal) && ModuleOf(n) != own)
                .ToList();
            crossModule.ShouldAllBe(n => n.EndsWith(ContractsSuffix, StringComparison.Ordinal),
                $"{asm.GetName().Name} başka modülün yalnız Contracts projesine bağımlı olabilir");
        }
    }

    [Fact]
    public void Contracts_DoNotDependOnDomainOrApplication()
    {
        foreach (var asm in Named(ContractsSuffix).Where(a => a.GetName().Name != SharedContracts))
        {
            var forbidden = asm.GetReferencedAssemblies().Select(r => r.Name!)
                .Where(n => n.EndsWith(DomainSuffix, StringComparison.Ordinal) || n.EndsWith(ApplicationSuffix, StringComparison.Ordinal) || n.EndsWith(InfrastructureSuffix, StringComparison.Ordinal))
                .ToList();
            forbidden.ShouldBeEmpty($"{asm.GetName().Name} yalnız Kernel/Shared.Contracts referans alabilir");
        }
    }

    [Fact]
    public void Handlers_AreSealed_AndNamedByConvention()
    {
        var result = Types.InAssemblies(Named(ApplicationSuffix))
            .That().HaveNameEndingWith("Handler")
            .Should().BeSealed()
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Controllers_InheritApiControllerBase()
    {
        var result = Types.InAssemblies(Named(ApiSuffix))
            .That().HaveNameEndingWith("Controller")
            .Should().Inherit(typeof(Sense.Shared.Web.Controllers.ApiControllerBase))
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static string ModuleOf(string assemblyName)
    {
        var rest = assemblyName[ModulesPrefix.Length..];
        var dot = rest.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? rest : rest[..dot];
    }

    private static IEnumerable<Assembly> Named(string suffix) => All.Where(a => a.GetName().Name!.EndsWith(suffix, StringComparison.Ordinal));

    private static Assembly Find(string name) => All.Single(a => a.GetName().Name == name);

    private static Assembly[] LoadSolutionAssemblies()
    {
        // Test projesi Sense.Api'ye referans verir; tüm Sense.* assembly'leri çıktı klasöründe bulunur.
        var dir = AppContext.BaseDirectory;
        var files = Directory.GetFiles(dir, Prefix + "*.dll").Where(f => !f.EndsWith("Tests.dll", StringComparison.Ordinal));
        return files.Select(f => Assembly.LoadFrom(f)).ToArray();
    }
}

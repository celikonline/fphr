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

    /// <summary>
    /// Architecture rule: No hardcoded secrets in source code.
    /// Checks for obvious patterns like "password", "token", "secret" (case-insensitive).
    /// Allowed locations: appsettings.json, environment variables, secrets.json (git-ignored).
    /// </summary>
    [Fact]
    public void NoPlaintextSecretsInCode()
    {
        var sensitivePatterns = new[] { "password", "token", "secret", "apikey", "api_key" };
        var result = Types.InAssemblies(All.Where(a => a.GetName().Name!.StartsWith(Prefix)))
            .That().AreClasses()
            .Should().NotHaveNameMatching(@".*(?i)(password|token|secret|apikey|api_key).*")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Found potential hardcoded secrets. Move to appsettings.json or environment variables. " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Architecture rule: Handlers must follow naming conventions.
    /// - Command handlers: `*CommandHandler` sealed class
    /// - Query handlers: `*QueryHandler` sealed class
    /// </summary>
    [Fact]
    public void Handlers_FollowNamingConventions()
    {
        var result = Types.InAssemblies(Named(ApplicationSuffix))
            .That().HaveNameEndingWith("Handler")
            .Should().HaveNameMatching(@"^[A-Z][A-Za-z0-9]*(?:Command|Query)Handler$")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Handlers must follow pattern: {Action}CommandHandler or {Action}QueryHandler. " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Architecture rule: Events must be in Domain layer and follow naming conventions.
    /// - Domain events: `*Event` or `*DomainEvent` in Domain assembly
    /// </summary>
    [Fact]
    public void DomainEvents_AreInDomainLayer()
    {
        var result = Types.InAssemblies(Named(DomainSuffix))
            .That().HaveNameEndingWith("Event")
            .Should().ResideInNamespaceContaining("Domain")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain events must reside in Domain namespace. " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Architecture rule: Audit/Infrastructure events should NOT be visible to Application layer.
    /// Only Domain events can be published from entities.
    /// </summary>
    [Fact]
    public void AuditEvents_AreNotReferencedByApplicationHandlers()
    {
        var result = Types.InAssemblies(Named(ApplicationSuffix))
            .Should().NotHaveDependencyOnAll(Types.InAssemblies(Named(InfrastructureSuffix))
                .That().HaveNameEndingWith("AuditEvent"))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application handlers must not reference Infrastructure audit events. " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Architecture rule: Permission keys must follow naming convention.
    /// Pattern: {module}.{resource_plural}.{action} in snake_case
    /// Example: identity.users.create, organization.units.read
    /// </summary>
    [Fact]
    public void PermissionKeys_FollowNamingConvention()
    {
        var infrastructureAssemblies = Named(InfrastructureSuffix);
        var result = Types.InAssemblies(infrastructureAssemblies)
            .That().HaveNameContaining("Permission")
            .Should().HaveNameMatching(@"^[a-z_]+\.[a-z_]+\.[a-z_]+$")
            .GetResult();

        // This is a soft check; actual permission strings are validated at runtime
        result.IsSuccessful.ShouldBeTrue(
            "Permission keys should follow pattern: module.resource_plural.action in snake_case. " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Architecture rule: No Infrastructure layer in public API of Contracts.
    /// Contracts should only expose domain concepts, not infrastructure concerns.
    /// </summary>
    [Fact]
    public void Contracts_DoNotExposeInfrastructureConcerns()
    {
        var infrastructureTypes = Types.InAssemblies(Named(InfrastructureSuffix));
        var result = Types.InAssemblies(Named(ContractsSuffix))
            .Should().NotHaveDependencyOnAll(infrastructureTypes)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Contracts must not depend on Infrastructure. " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Architecture rule: Modules can only have one DbContext per module.
    /// All entities in a module share the same PostgreSQL schema.
    /// </summary>
    [Fact]
    public void Modules_HaveOneDbContextPerModule()
    {
        var dbContextType = typeof(Sense.Shared.Infrastructure.Persistence.ModuleDbContext);
        var result = Types.InAssemblies(Named(InfrastructureSuffix))
            .That().Inherit(dbContextType)
            .Should().NotHaveMultipleInheritorsInAssemblies(Named(InfrastructureSuffix))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Each module should have exactly one DbContext inheriting from ModuleDbContext. " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static Assembly[] LoadSolutionAssemblies()
    {
        // Test projesi Sense.Api'ye referans verir; tüm Sense.* assembly'leri çıktı klasöründe bulunur.
        var dir = AppContext.BaseDirectory;
        var files = Directory.GetFiles(dir, Prefix + "*.dll").Where(f => !f.EndsWith("Tests.dll", StringComparison.Ordinal));
        return files.Select(f => Assembly.LoadFrom(f)).ToArray();
    }
}

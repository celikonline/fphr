using System.Reflection;
using Shouldly;
using Xunit;

namespace Sense.Tests.Contract;

/// <summary>
/// TALIMAT madde 1: PO'nun sözleşme sahipliğinin mekanik karşılığı.
/// İddia: Hiçbir Sense.Modules.*.Contracts assembly'si *.Application, *.Infrastructure, *.Api
/// veya EF Core assembly'lerine referans vermez.
///
/// Ayrıca: Contracts'taki public tipler ITenantEntity/DbContext/EF attribute'u taşımaz
/// (ham kalıcılık tipi sınır geçmez).
/// </summary>
public sealed class ContractAssemblyIsolationTests
{
    private const string Prefix = "Sense.";
    private const string ModulesPrefix = "Sense.Modules.";
    private const string ContractsSuffix = ".Contracts";

    [Fact]
    public void ContractAssemblies_DoNotReferenceApplicationInfrastructureOrApi()
    {
        var contractAssemblies = FindAllContractAssemblies();
        var violations = new List<(string Contract, string ForbiddenRef)>();

        var forbiddenPatterns = new[]
        {
            ".Application",
            ".Infrastructure",
            ".Api",
            "EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore",
        };

        foreach (var contractAsm in contractAssemblies)
        {
            var references = contractAsm.GetReferencedAssemblies()
                .Select(r => r.Name ?? "")
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                .ToList();

            foreach (var pattern in forbiddenPatterns)
            {
                var matching = references.Where(r => r.Contains(pattern, StringComparison.Ordinal)).ToList();
                foreach (var forbidden in matching)
                {
                    violations.Add((contractAsm.GetName().Name ?? "Unknown", forbidden));
                }
            }
        }

        violations.ShouldBeEmpty(
            "Contracts assembly'leri layer sınırını aşamaz. İhlaller: " +
            string.Join("; ", violations.Select(v => $"{v.Contract} → {v.ForbiddenRef}")));
    }

    [Fact]
    public void ContractTypes_DoNotCarryPersistenceConcerns()
    {
        var contractAssemblies = FindAllContractAssemblies();
        var violations = new List<(string Assembly, string Type, string Concern)>();

        var tenantEntityType = typeof(Sense.Shared.Kernel.Domain.ITenantEntity);
        var dbContextMarkerType = Type.GetType("Microsoft.EntityFrameworkCore.DbContext");

        foreach (var contractAsm in contractAssemblies)
        {
            var publicTypes = contractAsm.GetTypes()
                .Where(t => t.IsPublic && !t.IsInterface && !t.IsAbstract)
                .ToList();

            foreach (var type in publicTypes)
            {
                // ITenantEntity implement ediyor mu?
                if (type.GetInterfaces().Contains(tenantEntityType))
                {
                    violations.Add((contractAsm.GetName().Name ?? "Unknown", type.Name, "implements ITenantEntity"));
                }

                // DbContext inherit ediyor mu?
                if (dbContextMarkerType != null && dbContextMarkerType.IsAssignableFrom(type))
                {
                    violations.Add((contractAsm.GetName().Name ?? "Unknown", type.Name, "inherits DbContext"));
                }

                // EF attribute taşıyor mu? (simple heuristic: type name contains "Config" + IEntityTypeConfiguration)
                var efConfigType = Type.GetType(
                    "Microsoft.EntityFrameworkCore.Metadata.Builders.IEntityTypeConfiguration`1");
                if (efConfigType != null && type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == efConfigType))
                {
                    violations.Add((contractAsm.GetName().Name ?? "Unknown", type.Name, "implements IEntityTypeConfiguration"));
                }
            }
        }

        violations.ShouldBeEmpty(
            "Contracts'taki public tipler kalıcılık detayı taşıyamaz. İhlaller: " +
            string.Join("; ", violations.Select(v => $"{v.Assembly}.{v.Type} ({v.Concern})")));
    }

    private static Assembly[] FindAllContractAssemblies()
    {
        var dir = AppContext.BaseDirectory;
        var contractDlls = Directory.GetFiles(dir, ModulesPrefix + "*" + ContractsSuffix + ".dll", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(dir, Prefix + "Shared.Contracts.dll"))
            .Distinct()
            .ToList();

        return contractDlls.Select(f => Assembly.LoadFrom(f)).ToArray();
    }
}

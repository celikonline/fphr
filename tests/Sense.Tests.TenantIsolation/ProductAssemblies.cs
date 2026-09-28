using System.Reflection;

namespace Sense.Tests.TenantIsolation;

/// <summary>
/// Loads every product assembly (<c>Sense.*.dll</c> without "Tests" in the file name) from the test output folder,
/// same pattern as <c>Sense.Tests.Architecture.TenantIsolationArchitectureTests.LoadSolutionAssemblies</c>.
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c> is not enough: module assemblies are loaded lazily, so at test time
/// most of them are not in the AppDomain yet and the conventions would silently scan nothing.
/// </summary>
internal static class ProductAssemblies
{
    private const string Prefix = "Sense.";

    public static List<Assembly> Load() =>
        Directory.GetFiles(AppContext.BaseDirectory, Prefix + "*.dll")
            .Where(f => !Path.GetFileName(f).Contains("Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .ToList();
}

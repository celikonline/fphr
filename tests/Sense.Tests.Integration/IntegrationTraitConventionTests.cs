using System.Reflection;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// Meta-test: Verifies that all test methods in this assembly follow the established
/// Convention: every [Fact] and [Theory] must have [Trait("Category", "Integration")].
///
/// This test has NO trait itself, so it's found by the filter that excludes Integration tests.
/// It ensures the discipline is mechanically enforced and cannot be accidentally violated.
///
/// CRITICAL: This test must NOT have a Trait attribute, otherwise it will be filtered out
/// and the meta-check will be skipped!
/// </summary>
public sealed class IntegrationTraitConventionTests
{
    /// <summary>
    /// Verifies that every [Fact] and [Theory] test method in this assembly
    /// has the [Trait("Category", "Integration")] attribute.
    ///
    /// This is a structural guarantee: the test suite enforces its own categorization.
    /// </summary>
    [Fact]  // <-- NOTE: Deliberately NO [Trait(...)] on this test
    public void AllIntegrationTests_HaveIntegrationTrait()
    {
        // Arrange: Get all test methods in this assembly
        var assembly = typeof(IntegrationTraitConventionTests).Assembly;
        var allTypes = assembly.GetTypes();

        var testMethods = new List<(Type, MethodInfo)>();

        foreach (var type in allTypes)
        {
            // Skip non-test classes (non-public, non-sealed, or obviously not test fixtures). This meta-test class is
            // skipped too: its own tests are deliberately trait-less (see the class doc comment), so scanning it would
            // always report them as violations.
            if (!type.IsPublic || type.Name.EndsWith("Tests") == false || type == typeof(IntegrationTraitConventionTests))
            {
                continue;
            }

            // Find all public methods with [Fact] or [Theory] attributes
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var factAttr = method.GetCustomAttribute<FactAttribute>();
                var theoryAttr = method.GetCustomAttribute<TheoryAttribute>();

                if (factAttr != null || theoryAttr != null)
                {
                    testMethods.Add((type, method));
                }
            }
        }

        // Act & Assert: Verify each test method has the Integration trait
        var testMethodsWithoutTrait = new List<string>();

        foreach (var (testClass, testMethod) in testMethods)
        {
            var traits = testMethod.GetCustomAttributes<TraitAttribute>().ToList();

            var hasIntegrationTrait = traits.Any(t =>
                t.Name == "Category" && t.Value == "Integration");

            if (!hasIntegrationTrait)
            {
                testMethodsWithoutTrait.Add($"{testClass.FullName}.{testMethod.Name}");
            }
        }

        // Assert with a detailed error message listing all missing traits
        testMethodsWithoutTrait.ShouldBeEmpty(
            $"The following test methods are missing [Trait(\"Category\", \"Integration\")] attribute:\n" +
            $"{string.Join("\n", testMethodsWithoutTrait)}");
    }

    /// <summary>
    /// Secondary check: Verifies that the Integration trait values are consistent
    /// (they all use exactly "Integration" as the value, not typos like "Integrtion").
    /// </summary>
    [Fact]  // <-- NOTE: Deliberately NO [Trait(...)] on this test
    public void IntegrationTraitValues_AreConsistent()
    {
        // Arrange: Get all test methods
        var assembly = typeof(IntegrationTraitConventionTests).Assembly;
        var allTypes = assembly.GetTypes();

        var inconsistentTraits = new List<string>();

        foreach (var type in allTypes)
        {
            if (!type.IsPublic || type.Name.EndsWith("Tests") == false)
            {
                continue;
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var factAttr = method.GetCustomAttribute<FactAttribute>();
                var theoryAttr = method.GetCustomAttribute<TheoryAttribute>();

                if (factAttr == null && theoryAttr == null)
                {
                    continue;
                }

                var traits = method.GetCustomAttributes<TraitAttribute>().ToList();
                var categoryTraits = traits.Where(t => t.Name == "Category").ToList();

                foreach (var trait in categoryTraits)
                {
                    // Verify the value is exactly "Integration" (case-sensitive)
                    if (trait.Value != "Integration")
                    {
                        inconsistentTraits.Add(
                            $"{type.FullName}.{method.Name}: Trait value is '{trait.Value}', " +
                            $"expected 'Integration'");
                    }
                }
            }
        }

        // Assert
        inconsistentTraits.ShouldBeEmpty(
            $"The following tests have inconsistent Category trait values:\n" +
            $"{string.Join("\n", inconsistentTraits)}");
    }

    /// <summary>
    /// Verifies that this meta-test class itself is properly discoverable
    /// and follows the test naming convention.
    /// </summary>
    [Fact]  // <-- NOTE: Deliberately NO [Trait(...)] on this test
    public void IntegrationMetaTest_IsProperlyStructured()
    {
        // Arrange
        var testClass = typeof(IntegrationTraitConventionTests);

        // Act & Assert
        testClass.Name.ShouldEndWith("Tests");

        testClass.IsSealed.ShouldBeTrue(
            "Test class should be sealed (no inheritance)");

        testClass.IsPublic.ShouldBeTrue(
            "Test class should be public");

        // Verify this test method itself has NO trait
        var thisMethod = testClass.GetMethod(nameof(IntegrationMetaTest_IsProperlyStructured));
        thisMethod.ShouldNotBeNull();

        var traits = thisMethod!.GetCustomAttributes<TraitAttribute>().ToList();
        traits.ShouldBeEmpty(
            "This meta-test method must NOT have a [Trait] attribute, " +
            "so it can be discovered by the 'Category!=Integration' filter");
    }
}

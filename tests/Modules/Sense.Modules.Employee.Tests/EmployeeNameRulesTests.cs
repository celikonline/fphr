using Sense.Modules.Employee.Domain.Employees;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>İK süreç testi O-12: ad/soyad alanlarında HTML/betik karakterleri reddedilir, gerçek adlar etkilenmez.</summary>
public sealed class EmployeeNameRulesTests
{
    [Theory]
    [InlineData("Ayşe")]
    [InlineData("Ahmet Yılmaz")]
    [InlineData("O'Brien")]
    [InlineData("Ali-Veli")]
    [InlineData("İbrahim")]
    [InlineData("Jean-Luc Picard Jr.")]
    [InlineData(null)]
    [InlineData("")]
    public void IsSafe_RealNames_AreAccepted(string? name) => EmployeeNameRules.IsSafe(name).ShouldBeTrue();

    [Theory]
    [InlineData("<b>x</b>")]
    [InlineData("Ali<script>alert(1)</script>")]
    [InlineData("Ali>")]
    [InlineData("Ali\tVeli")]
    [InlineData("Ali\nVeli")]
    public void IsSafe_MarkupAndControlCharacters_AreRejected(string name) => EmployeeNameRules.IsSafe(name).ShouldBeFalse();
}

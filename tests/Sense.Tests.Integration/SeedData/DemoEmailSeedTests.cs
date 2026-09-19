using Sense.Api.SeedData;
using Sense.Modules.Employee.Domain.Employees;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.SeedData;

/// <summary>
/// The Employee module rejects non-ASCII work e-mails (EmployeeEmailRules, senseik-044), so every address the demo
/// seeder derives from the Turkish name pools must fold to ASCII - otherwise seeding a fresh database fails with
/// employee.work_email_invalid and the API never starts.
/// </summary>
public sealed class DemoEmailSeedTests
{
    [Theory]
    [InlineData("Bülent", "bulent")]
    [InlineData("Ayşe", "ayse")]
    [InlineData("Gülşen", "gulsen")]
    [InlineData("Pınar", "pinar")]
    [InlineData("Burçin", "burcin")]
    [InlineData("İbrahim", "ibrahim")]
    [InlineData("Öztürk", "ozturk")]
    [InlineData("Yılmaz", "yilmaz")]
    [InlineData("Doğan", "dogan")]
    public void ToAsciiEmailPart_FoldsTurkishLettersToAscii(string name, string expected)
    {
        DataSeeder.ToAsciiEmailPart(name).ShouldBe(expected);
    }

    [Fact]
    public void EveryDemoNamePair_ProducesAnEmailTheEmployeeModuleAccepts()
    {
        var firstNames = SeedDataConstants.TurkishMaleFirstNames.Concat(SeedDataConstants.TurkishFemaleFirstNames);

        foreach (var first in firstNames)
        {
            foreach (var last in SeedDataConstants.TurkishLastNames)
            {
                var email = $"{DataSeeder.ToAsciiEmailPart(first)}.{DataSeeder.ToAsciiEmailPart(last)}@democompany.com";

                EmployeeEmailRules.IsValid(email).ShouldBeTrue(email);
            }
        }
    }
}

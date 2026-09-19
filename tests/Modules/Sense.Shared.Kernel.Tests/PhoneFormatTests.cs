using Sense.Shared.Kernel.ValueObjects;
using Shouldly;
using Xunit;

namespace Sense.Shared.Kernel.Tests;

/// <summary>Regression: free-typed phone fields accepted anything ("abc"); PhoneNormalizer just dropped non-digits.</summary>
public sealed class PhoneFormatTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("tel")]
    [InlineData("0532 abc 45 67")]
    [InlineData("0532-123-45-67 dahili 12")]
    [InlineData("123")]
    [InlineData("+")]
    [InlineData("05321234567+")]
    [InlineData("0532 123 45 67 #")]
    [InlineData("<script>1234567</script>")]
    [InlineData("123456789012345678901")]
    public void IsValid_RejectsLettersJunkAndImplausibleLengths(string value) =>
        PhoneFormat.IsValid(value).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("5551234567")]
    [InlineData("0532 123 45 67")]
    [InlineData("+90 (532) 123-45-67")]
    [InlineData("+90 212 000 00 01")]
    [InlineData("0212.555.00.00")]
    [InlineData("0212/555 00 00")]
    [InlineData("1234")]
    public void IsValid_AcceptsRealWorldFormatsAndEmpty(string? value) =>
        PhoneFormat.IsValid(value).ShouldBeTrue();
}

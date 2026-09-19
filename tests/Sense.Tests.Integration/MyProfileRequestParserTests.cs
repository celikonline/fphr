using System.Text.Json;
using Sense.Modules.Employee.Api.Controllers;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>Regresyon: <c>PUT /employees/me/profile</c> boş <c>{}</c> gövdesiyle mevcut iletişim verisini siliyordu.</summary>
public sealed class MyProfileRequestParserTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"x\"")]
    public void EmptyOrNonObjectBody_IsRejected(string json) =>
        MyProfileRequestParser.Parse(Body(json), Options).ShouldBeNull();

    [Fact]
    [Trait("Category", "Integration")]
    public void BodyWithExplicitNulls_IsAccepted_SoWebFormCanClearFields()
    {
        var request = MyProfileRequestParser.Parse(
            Body("""{"personalEmail":null,"mobilePhone":null,"homePhone":null,"addressLine":null,"city":null,"postalCode":null,"customFieldValues":null}"""), Options);

        request.ShouldNotBeNull();
        request.PersonalEmail.ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void PartialBody_IsAccepted()
    {
        var request = MyProfileRequestParser.Parse(Body("""{"city":"İstanbul"}"""), Options);

        request.ShouldNotBeNull();
        request.City.ShouldBe("İstanbul");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void WrongTypedProperty_IsRejectedInsteadOfThrowing() =>
        MyProfileRequestParser.Parse(Body("""{"city":123}"""), Options).ShouldBeNull();
}

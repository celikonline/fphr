using System.Text.Json;
using System.Text.Json.Serialization;
using Sense.Modules.Recruitment.Application.Candidates;
using Sense.Modules.Recruitment.Domain.Candidates;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// senseik-008 (i18n enum key leak) backend companion: <see cref="CandidateDto.Source"/> used to be produced via
/// <c>candidate.Source.ToString()</c>, which serialized PascalCase ("Manual") and bypassed the API's global
/// <see cref="JsonStringEnumConverter"/> (only applies to fields still typed as the enum). The web then looked up
/// <c>recruitment:candidates.sources.Manual</c>, which does not exist, and rendered the raw i18n key. Keeping
/// <see cref="CandidateDto.Source"/> typed as <see cref="CandidateSource"/> lets the converter camelCase it, which
/// this test verifies using the exact <see cref="JsonSerializerOptions"/> shape the API configures in
/// `WebServiceCollectionExtensions.AddJsonOptions`.
/// </summary>
public sealed class CandidateDtoSerializationTests
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Theory]
    [InlineData(CandidateSource.Manual, "manual")]
    [InlineData(CandidateSource.CareerSite, "careerSite")]
    [InlineData(CandidateSource.Referral, "referral")]
    [InlineData(CandidateSource.Other, "other")]
    public void Source_SerializesCamelCase_MatchingWebLocaleKeys(CandidateSource source, string expectedJsonValue)
    {
        var dto = new CandidateDto(Guid.NewGuid(), "Ada", "Yılmaz", "ada.yilmaz@example.com", null, source, [], false);

        var json = JsonSerializer.Serialize(dto, ApiJsonOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("source", out var sourceProperty).ShouldBeTrue();
        sourceProperty.GetString().ShouldBe(expectedJsonValue);
        // Case.Sensitive: Shouldly's ShouldNotContain is case-insensitive by default, which would also flag the
        // (correct) lowercase "source" property name this test wants present.
        json.ShouldNotContain("\"Source\"", Case.Sensitive);
    }
}

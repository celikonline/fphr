using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.ErrorHandling;
using Sense.Shared.Web.Localization;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>Regresyon: model-binding 400'ü İngilizce framework metni ve CLR tip adı sızdırıyordu.</summary>
public sealed class ModelStateProblemFactoryTests
{
    private const string LeakedMessage =
        "The JSON value could not be converted to Sense.Modules.Identity.Api.Controllers.LoginRequest. Path: $.email | LineNumber: 0";

    private static ActionContext ContextWith(ModelStateDictionary modelState)
    {
        var localizer = Substitute.For<IErrorLocalizer>();
        localizer.Title(Arg.Any<ErrorType>()).Returns("Doğrulama hatası");
        localizer.Message(Arg.Any<Error>()).Returns("Gönderilen veriler doğrulamadan geçmedi.");
        localizer.MessageOrText(ModelStateProblemFactory.InvalidFieldKey).Returns("Alan geçersiz veya eksik.");

        var services = new ServiceCollection()
            .AddSingleton(localizer)
            .AddSingleton(Options.Create(new ProblemMappingOptions()))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Path = "/api/v1/auth/login";
        return new ActionContext(http, new RouteData(), new ActionDescriptor(), modelState);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void BindingError_DoesNotLeakClrTypeNameOrEnglishFrameworkText()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.email", LeakedMessage);
        modelState.AddModelError("request", "A non-empty request body is required.");

        var result = (ObjectResult)ModelStateProblemFactory.Create(ContextWith(modelState));

        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        var problem = result.Value.ShouldBeOfType<ProblemDetails>();
        problem.Extensions["code"].ShouldBe(ErrorCodes.ValidationError);
        var errors = problem.Extensions["errors"].ShouldBeOfType<Dictionary<string, string[]>>();
        errors.Keys.ShouldBe(["email", "request"], ignoreOrder: true);
        var everything = string.Join('|', errors.SelectMany(e => e.Value)) + problem.Detail + problem.Title;
        everything.ShouldNotContain("Sense.Modules");
        everything.ShouldNotContain("LoginRequest");
        everything.ShouldNotContain("could not be converted");
        errors["email"].ShouldBe(["Alan geçersiz veya eksik."]);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("$.email", "email")]
    [InlineData("$.items[0].name", "items[0].name")]
    [InlineData("$", "body")]
    [InlineData("", "body")]
    public void NormalizeFieldName_StripsJsonPathPrefix(string key, string expected) =>
        ModelStateProblemFactory.NormalizeFieldName(key).ShouldBe(expected);
}

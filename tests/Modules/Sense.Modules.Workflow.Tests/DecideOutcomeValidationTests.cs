using System.Text.Json;
using System.Text.Json.Serialization;
using Sense.Modules.Workflow.Api.Controllers;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// Regression: a decision body without a valid "outcome" (e.g. {"decision":"approve"}) used to bind to the enum's
/// default value Approved(0) and silently approve the request. A missing or undefined outcome must be a validation error.
/// </summary>
public sealed class DecideOutcomeValidationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void RequestBodyWithoutOutcome_BindsToNull_NotApproved()
    {
        var body = JsonSerializer.Deserialize<DecideTaskRequest>("""{"decision":"approve"}""", Json);

        body!.Outcome.ShouldBeNull();
    }

    [Fact]
    public void TokenRequestBodyWithoutOutcome_BindsToNull_NotApproved()
    {
        var body = JsonSerializer.Deserialize<DecideByTokenRequest>("""{"token":"abc"}""", Json);

        body!.Outcome.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"outcome":"approved"}""", ApprovalOutcome.Approved)]
    [InlineData("""{"outcome":"rejected"}""", ApprovalOutcome.Rejected)]
    public void RequestBodyWithOutcome_StillBinds(string json, ApprovalOutcome expected)
    {
        JsonSerializer.Deserialize<DecideTaskRequest>(json, Json)!.Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(99, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void DecideValidator_RequiresADefinedOutcome(int? outcome, bool valid)
    {
        var command = new DecideApprovalTaskCommand(
            Guid.NewGuid(), Guid.NewGuid(), (ApprovalOutcome?)outcome, null, ApprovalChannel.Web, null, null);

        new DecideApprovalTaskValidator().Validate(command).IsValid.ShouldBe(valid);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(99, false)]
    [InlineData(1, true)]
    public void OverrideValidator_RequiresADefinedOutcome(int? outcome, bool valid)
    {
        var command = new OverrideDecideApprovalTaskCommand(
            Guid.NewGuid(), Guid.NewGuid(), (ApprovalOutcome?)outcome, null, ApprovalChannel.Web, null, null);

        new OverrideDecideApprovalTaskValidator().Validate(command).IsValid.ShouldBe(valid);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(99, false)]
    [InlineData(0, true)]
    public void EmailTokenValidator_RequiresADefinedOutcome(int? outcome, bool valid)
    {
        var command = new DecideByEmailTokenCommand("token", (ApprovalOutcome?)outcome, null, null, null);

        new DecideByEmailTokenValidator().Validate(command).IsValid.ShouldBe(valid);
    }
}

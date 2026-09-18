using FluentValidation;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// TerminateEmployeeValidator's SGK exit code <-> termination type consistency rule (senseik-006/F-01) in
/// isolation, no DB needed. The validator only sees the command's own overrides - it cannot resolve the chosen
/// reason's defaults, so it only rejects the case where both fields are overridden and inconsistent with each
/// other. The fully-resolved pair (reason defaults included), the "reason default is snapshotted onto the
/// employee" behavior, and the md.24/md.25 family acceptance are exercised end to end (real DB, real handler) by
/// TerminationIntegrationTests.SgkExitCodeConsistency_RejectsMismatchedPairsAndAcceptsFamilyMembers and
/// .Terminate_FromActiveAndSuspended_AppliesReasonDefaultsAndRejectsInvalidTransitions.
/// </summary>
public sealed class TerminateEmployeeValidatorTests
{
    private readonly TerminateEmployeeValidator _validator = new();

    [Fact]
    public void Validate_BothOverridesInconsistent_Fails()
    {
        var command = new TerminateEmployeeCommand(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), TerminationType.FixedTermExpiry, "26");

        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == EmployeeDomainErrors.TerminationSgkExitCodeInconsistent);
    }

    [Fact]
    public void Validate_BothOverridesConsistent_Passes()
    {
        var command = new TerminateEmployeeCommand(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), TerminationType.EmployeeJustCause_Art24, "24");

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OnlyTypeOverridden_PassesHere_HandlerChecksTheResolvedPairLater()
    {
        // No SgkExitCode override: the validator cannot know yet whether the chosen reason's default code will be
        // consistent - it defers to TerminateEmployeeHandler, which has the reason loaded.
        var command = new TerminateEmployeeCommand(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), TerminationType.EmployeeJustCause_Art24);

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_NeitherOverridden_Passes()
    {
        var command = new TerminateEmployeeCommand(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid());

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_InvalidEnumValue_Fails()
    {
        var command = new TerminateEmployeeCommand(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), (TerminationType)999);

        _validator.Validate(command).IsValid.ShouldBeFalse();
    }
}

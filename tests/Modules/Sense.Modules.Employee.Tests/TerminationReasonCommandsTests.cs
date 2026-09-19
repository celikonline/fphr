using FluentValidation;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// CreateTerminationReasonValidator/UpdateTerminationReasonValidator's SGK exit code <-> termination type
/// consistency rule (senseik-006/F-01) in isolation, no DB needed.
/// </summary>
public sealed class TerminationReasonCommandsTests
{
    private readonly CreateTerminationReasonValidator _createValidator = new();
    private readonly UpdateTerminationReasonValidator _updateValidator = new();

    [Fact]
    public void Create_WithCodeOutsideTheTypesFamily_FailsValidation()
    {
        var command = new CreateTerminationReasonCommand("Bad", "Kötü", "99", TerminationType.FixedTermExpiry, 0);

        var result = _createValidator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == EmployeeDomainErrors.TerminationSgkExitCodeInconsistent);
    }

    [Fact]
    public void Create_WithCodeInTheTypesFamily_Passes()
    {
        var command = new CreateTerminationReasonCommand("Good", "İyi", "05", TerminationType.FixedTermExpiry, 0);

        _createValidator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithAmbiguousFamilyTypeAndNoCode_Passes()
    {
        // The system seed itself relies on this: md.24/md.25 have no safe single default.
        var command = new CreateTerminationReasonCommand("Family", "Aile", null, TerminationType.EmployeeJustCause_Art24, 0);

        _createValidator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithoutATerminationType_AnyKnownSgkCodeIsAccepted_ButNotAnInventedOne()
    {
        var command = new CreateTerminationReasonCommand("NoType", "Türsüz", "10", null, 0);

        _createValidator.Validate(command).IsValid.ShouldBeTrue();
        _createValidator.Validate(command with { DefaultSgkExitCode = "99" }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Update_WithCodeOutsideTheTypesFamily_FailsValidation()
    {
        var command = new UpdateTerminationReasonCommand(Guid.NewGuid(), "Kötü", "99", TerminationType.ValidCause_Art18, 0);

        var result = _updateValidator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage == EmployeeDomainErrors.TerminationSgkExitCodeInconsistent);
    }

    [Fact]
    public void Update_WithCodeInTheTypesFamily_Passes()
    {
        var command = new UpdateTerminationReasonCommand(Guid.NewGuid(), "İyi", "04", TerminationType.ValidCause_Art18, 0);

        _updateValidator.Validate(command).IsValid.ShouldBeTrue();
    }
}

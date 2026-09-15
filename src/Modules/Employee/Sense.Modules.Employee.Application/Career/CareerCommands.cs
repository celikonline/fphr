using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Career;

/// <summary>"Pozisyon ekle": adds a career period; the previous open record is closed at ValidFrom - 1.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record AddCareerRecordCommand(
    Guid EmployeeId,
    DateOnly ValidFrom,
    CareerChangeType ChangeType,
    EmploymentType? EmploymentType,
    Guid? ManagerEmployeeId,
    string? Note,
    IReadOnlyList<OrgAssignmentDto> Assignments) : ICommand<IReadOnlyList<CareerRecordDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Edits a career record; only the latest one may move its ValidFrom.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record UpdateCareerRecordCommand(
    Guid EmployeeId,
    Guid RecordId,
    DateOnly ValidFrom,
    CareerChangeType ChangeType,
    EmploymentType? EmploymentType,
    Guid? ManagerEmployeeId,
    string? Note,
    IReadOnlyList<OrgAssignmentDto> Assignments) : ICommand<IReadOnlyList<CareerRecordDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Deletes the latest career record and reopens the previous one.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record DeleteCareerRecordCommand(Guid EmployeeId, Guid RecordId) : ICommand<IReadOnlyList<CareerRecordDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class AddCareerRecordValidator : AbstractValidator<AddCareerRecordCommand>
{
    public AddCareerRecordValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        CareerRules.Apply(this, x => x.Note, x => x.Assignments);
    }
}

public sealed class UpdateCareerRecordValidator : AbstractValidator<UpdateCareerRecordCommand>
{
    public UpdateCareerRecordValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        CareerRules.Apply(this, x => x.Note, x => x.Assignments);
    }
}

public sealed class DeleteCareerRecordValidator : AbstractValidator<DeleteCareerRecordCommand>
{
    public DeleteCareerRecordValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
    }
}

/// <summary>Field rules shared by add/edit (the aggregate re-checks them; these give the client per-field 400s).</summary>
public static class CareerRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, string?> note, Func<T, IReadOnlyList<OrgAssignmentDto>?> assignments)
    {
        validator.RuleFor(x => note(x))
            .MaximumLength(EmployeeLimits.CareerNoteMaxLength)
            .WithMessage(EmployeeDomainErrors.CareerNoteTooLong)
            .OverridePropertyName(nameof(CareerRecordDto.Note));
        validator.RuleFor(x => assignments(x))
            .Must(list => list is null || list.Select(a => a.OrgUnitTypeId).Distinct().Count() == list.Count)
            .WithMessage(EmployeeDomainErrors.CareerDuplicateLevel)
            .OverridePropertyName(nameof(CareerRecordDto.Assignments));
    }

    public static IReadOnlyList<OrgAssignmentInput> ToInputs(IReadOnlyList<OrgAssignmentDto>? assignments) =>
        (assignments ?? []).Select(a => new OrgAssignmentInput(a.OrgUnitTypeId, a.OrgUnitId)).ToList();
}

public sealed class AddCareerRecordHandler(
    IEmployeeRepository employees,
    CareerService career,
    CareerReader reader) : ICommandHandler<AddCareerRecordCommand, IReadOnlyList<CareerRecordDto>>
{
    public async Task<Result<IReadOnlyList<CareerRecordDto>>> Handle(AddCareerRecordCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var assignments = CareerRules.ToInputs(command.Assignments);
        var prepared = await CareerWrite.PrepareAsync(employee, assignments, career, employees, command.ManagerEmployeeId, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var data = new CareerRecordData(command.ValidFrom, command.ChangeType, command.EmploymentType, command.ManagerEmployeeId, command.Note, assignments);
        var outcome = employee.AddCareerRecord(data, prepared.Value.Context);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        career.Publish(employee, outcome.Value);
        return Result.Success(await reader.ReadAsync(employee, cancellationToken));
    }
}

public sealed class UpdateCareerRecordHandler(
    IEmployeeRepository employees,
    CareerService career,
    CareerReader reader) : ICommandHandler<UpdateCareerRecordCommand, IReadOnlyList<CareerRecordDto>>
{
    public async Task<Result<IReadOnlyList<CareerRecordDto>>> Handle(UpdateCareerRecordCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var assignments = CareerRules.ToInputs(command.Assignments);
        var prepared = await CareerWrite.PrepareAsync(employee, assignments, career, employees, command.ManagerEmployeeId, cancellationToken, command.RecordId);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var data = new CareerRecordData(command.ValidFrom, command.ChangeType, command.EmploymentType, command.ManagerEmployeeId, command.Note, assignments);
        var outcome = employee.UpdateCareerRecord(command.RecordId, data, prepared.Value.Context);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        career.Publish(employee, outcome.Value);
        return Result.Success(await reader.ReadAsync(employee, cancellationToken));
    }
}

public sealed class DeleteCareerRecordHandler(
    IEmployeeRepository employees,
    CareerService career,
    CareerReader reader) : ICommandHandler<DeleteCareerRecordCommand, IReadOnlyList<CareerRecordDto>>
{
    public async Task<Result<IReadOnlyList<CareerRecordDto>>> Handle(DeleteCareerRecordCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var placement = await career.LoadPlacementAsync(cancellationToken);
        career.Materialize(employee, placement);
        var context = await career.BuildContextAsync(placement, cancellationToken);

        var outcome = employee.DeleteCareerRecord(command.RecordId, context);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        career.Publish(employee, outcome.Value);
        return Result.Success(await reader.ReadAsync(employee, cancellationToken));
    }
}

/// <summary>Steps every career write shares: materialize the initial record, check the manager exists, validate the assignments and their scope.</summary>
internal static class CareerWrite
{
    internal sealed record Prepared(OrgPlacement Placement, CareerContext Context);

    public static async Task<Result<Prepared>> PrepareAsync(
        EmployeeEntity employee,
        IReadOnlyList<OrgAssignmentInput> assignments,
        CareerService career,
        IEmployeeRepository employees,
        Guid? managerEmployeeId,
        CancellationToken ct,
        Guid? editedRecordId = null)
    {
        var placement = await career.LoadPlacementAsync(ct).ConfigureAwait(false);
        career.Materialize(employee, placement);

        if (managerEmployeeId is { } managerId && managerId != employee.ManagerEmployeeId &&
            !await employees.ExistsActiveAsync(managerId, ct).ConfigureAwait(false))
        {
            return Error.NotFound(EmployeeErrors.ManagerNotFound);
        }

        // Scope is compared against the record being replaced/edited: untouched levels stay valid.
        var previous = editedRecordId is { } recordId
            ? employee.CareerTimeline.FirstOrDefault(r => r.Id == recordId)
            : employee.CareerTimeline.LastOrDefault();

        var validated = await career.ValidateAssignmentsAsync(assignments, previous, placement, EmployeePermissions.EmployeesUpdateKey, ct).ConfigureAwait(false);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        return new Prepared(placement, await career.BuildContextAsync(placement, ct).ConfigureAwait(false));
    }
}

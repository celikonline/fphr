using FluentValidation;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Contracts;
// Not "using Sense.Modules.Employee.Contracts.Events" - it declares its own SuspensionReason/TerminationType
// (Contracts stays independent of Domain, per architecture rule), which would collide with Domain.Employees'
// versions below; event types (EmployeeHired/EmployeeSuspended/EmployeeReactivated/EmployeeTerminated) are
// referenced as Contracts.Events.* instead.
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Terminations;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// Terminates an Active or Suspended employee ("İşten ayrılış sihirbazı", 11.3.03 E5a); does not touch the Identity
/// account. TerminationType/SgkExitCode are optional overrides - when omitted, the chosen reason's defaults are used
/// (both null when the reason has none, which the domain accepts: TerminationType.Other has no implicit fallback here,
/// the caller must send one or pick a reason that defaults it).
/// </summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record TerminateEmployeeCommand(
    Guid Id,
    DateOnly TerminationDate,
    Guid TerminationReasonId,
    TerminationType? TerminationType = null,
    string? SgkExitCode = null,
    bool SeverancePaid = false,
    bool LeaveSettledAtExit = false) : ICommand<TerminateEmployeeResultDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class TerminateEmployeeValidator : AbstractValidator<TerminateEmployeeCommand>
{
    public TerminateEmployeeValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.TerminationReasonId).NotEmpty();
        RuleFor(x => x.TerminationType).IsInEnum().When(x => x.TerminationType is not null);

        // Catches the case where the caller overrides both fields inconsistently. This is a partial check only -
        // when either override is omitted the effective value comes from the chosen reason's defaults, which this
        // validator cannot see; TerminateEmployeeHandler re-checks the fully resolved pair before writing.
        RuleFor(x => x.SgkExitCode)
            .Must((command, sgkExitCode) => command.TerminationType is null || SgkExitCodeRules.IsCatalogConsistent(command.TerminationType, sgkExitCode))
            .WithMessage(EmployeeDomainErrors.TerminationSgkExitCodeInconsistent);
    }
}

public sealed class TerminateEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    ITerminationReasonRepository reasons,
    TerminationReasonProvisioningService reasonProvisioning,
    JobSecurityEvaluator jobSecurity,
    CollectiveDismissalEvaluator collectiveDismissal,
    IAssetLookup assetLookup,
    IIntegrationEventOutbox outbox,
    EmployeeDetailBuilder details) : ICommandHandler<TerminateEmployeeCommand, TerminateEmployeeResultDto>
{
    public async Task<Result<TerminateEmployeeResultDto>> Handle(TerminateEmployeeCommand command, CancellationToken cancellationToken)
    {
        // Employment periods come along: the open one is closed by Terminate with the settlement flags, and the
        // job-security seniority is measured across all of them ("fasılalı dönemler birleşik", 11.3.03).
        var found = await employees.GetWithEmploymentPeriodsAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        employee.EnsureInitialEmploymentPeriod();

        // The reason catalog may not exist yet for a tenant that never opened the wizard/list before terminating.
        await reasonProvisioning.EnsureSeededAsync(tenant.TenantId, cancellationToken);
        var foundReason = await reasons.GetByIdAsync(command.TerminationReasonId, cancellationToken);
        if (foundReason.IsFailure)
        {
            return foundReason.Error;
        }

        var reason = foundReason.Value;
        var terminationType = command.TerminationType ?? reason.DefaultTerminationType ?? Domain.Employees.TerminationType.Other;
        var sgkExitCode = command.SgkExitCode ?? reason.DefaultSgkExitCode;

        // Final resolved pair, re-checked here because either half may have come from the reason's own defaults
        // rather than the command - TerminateEmployeeValidator only sees the command's overrides. A family with
        // more than one valid code (md.24/md.25) has no safe default, so a null code is rejected too.
        if (!SgkExitCodeRules.IsTerminationConsistent(terminationType, sgkExitCode))
        {
            return Error.Validation(EmployeeDomainErrors.TerminationSgkExitCodeInconsistent);
        }

        var evaluation = await jobSecurity.EvaluateAsync(employee, terminationType, cancellationToken);

        // Counted before the write so the stored dismissals exclude this one; the pending type is added on top.
        var collective = await collectiveDismissal.EvaluateAsync(command.TerminationDate, terminationType, cancellationToken);

        var terminated = employee.Terminate(
            command.TerminationDate, reason.Id, terminationType, sgkExitCode, command.SeverancePaid, command.LeaveSettledAtExit);
        if (terminated.IsFailure)
        {
            return terminated.Error;
        }

        outbox.Enqueue(new Contracts.Events.EmployeeTerminated(
            tenant.TenantId, employee.Id, ToContractType(terminationType), reason.Code, sgkExitCode, command.TerminationDate)
        { ActorUserId = currentUser.UserId });

        // Advisory only (roadmap: offboarding <-> zimmet), same non-blocking pattern as the job-security/collective
        // dismissal warnings above: HR sees the open-asset count and can still complete the exit without returning
        // them first. The fact itself stays visible afterwards through Assets' own data (an Asset stays
        // Status == Assigned, pointing at a now-terminated employee, until someone records its return) - there is
        // no dedicated structured audit-log entry yet (Faz 1's audit-log backend is still being built).
        var openAssetCount = await assetLookup.CountOpenAssetsAsync(tenant.TenantId, employee.Id, cancellationToken);

        var dto = await details.BuildAsync(employee, cancellationToken);
        return new TerminateEmployeeResultDto(dto, evaluation.InScope, evaluation.ShowWarning, evaluation.WarningMessage, collective, openAssetCount);
    }

    private static Contracts.Events.TerminationType ToContractType(Domain.Employees.TerminationType type) => type switch
    {
        Domain.Employees.TerminationType.ValidCause_Art18 => Contracts.Events.TerminationType.ValidCause_Art18,
        Domain.Employees.TerminationType.EmployeeJustCause_Art24 => Contracts.Events.TerminationType.EmployeeJustCause_Art24,
        Domain.Employees.TerminationType.EmployerJustCause_Art25 => Contracts.Events.TerminationType.EmployerJustCause_Art25,
        Domain.Employees.TerminationType.MutualRescission => Contracts.Events.TerminationType.MutualRescission,
        Domain.Employees.TerminationType.Resignation => Contracts.Events.TerminationType.Resignation,
        Domain.Employees.TerminationType.FixedTermExpiry => Contracts.Events.TerminationType.FixedTermExpiry,
        _ => Contracts.Events.TerminationType.Other,
    };
}

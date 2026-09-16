using FluentValidation;
using Sense.Modules.Expense.Contracts.Events;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Application.ExpenseClaims;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.Advances;

/// <summary>Amount is the only strictly required field (source walkthrough: web "Tutar -> Kaydet"); everything else defaults.</summary>
[RequiresPermission("expense.my.advances")]
public sealed record CreateAdvanceRequestCommand(
    decimal Amount, string Currency, DateOnly? RequestedDate, bool IsInstallmentRequested, int? InstallmentCount, string? Description)
    : ICommand<Guid>;

public sealed class CreateAdvanceRequestValidator : AbstractValidator<CreateAdvanceRequestCommand>
{
    public CreateAdvanceRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.InstallmentCount).InclusiveBetween(1, ExpenseLimits.MaxInstallments).When(x => x.IsInstallmentRequested);
    }
}

public sealed class CreateAdvanceRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, TimeProvider clock, IAdvanceRequestRepository requests, IWorkflowService workflow, IIntegrationEventOutbox outbox)
    : ICommandHandler<CreateAdvanceRequestCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAdvanceRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(ExpenseErrors.NoEmployeeForUser);
        }

        var requestedDate = command.RequestedDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var submitted = AdvanceRequest.Submit(
            tenant.TenantId, employeeId, command.Amount, command.Currency, requestedDate,
            command.IsInstallmentRequested, command.InstallmentCount, command.Description);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var advance = submitted.Value;
        var approval = await workflow.StartAsync(ExpenseRequestTypes.Advance, advance.Id, employeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        advance.AttachApproval(approval.Value);
        requests.Add(advance);
        outbox.Enqueue(new AdvanceRequestSubmitted(tenant.TenantId, advance.Id, employeeId, advance.Amount, advance.Currency));
        return advance.Id;
    }
}

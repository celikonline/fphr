using FluentValidation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Severance;

/// <summary>
/// Adds a manual line to an exit settlement (ikale bedeli, N aylık ek ödeme, çıkış ayı ücreti...) and renets it. A tax flag left
/// null takes the default for <paramref name="Kind"/> (see <see cref="ExitSettlementNetCalculator.DefaultTreatment"/>).
/// </summary>
[RequiresPermission("payroll.exitsettlement.manage")]
public sealed record AddExitSettlementLineCommand(
    Guid SettlementId, ExitPayKind Kind, string Label, decimal GrossAmount, bool? SgkSubject, bool? IncomeTaxSubject, bool? StampTaxSubject) : ICommand<Guid>;

public sealed class AddExitSettlementLineValidator : AbstractValidator<AddExitSettlementLineCommand>
{
    public AddExitSettlementLineValidator()
    {
        RuleFor(x => x.SettlementId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(PayrollLimits.ExitLineLabelMaxLength);
        RuleFor(x => x.GrossAmount).GreaterThan(0);
    }
}

public sealed class AddExitSettlementLineHandler(IExitSettlementRepository settlements, ExitSettlementNetService netService)
    : ICommandHandler<AddExitSettlementLineCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddExitSettlementLineCommand command, CancellationToken cancellationToken)
    {
        var settlement = await settlements.FindByIdAsync(command.SettlementId, cancellationToken);
        if (settlement is null)
        {
            return Error.NotFound(PayrollDomainErrors.ExitSettlementNotFound);
        }

        var treatment = ExitSettlementLineTreatment.Resolve(command.Kind, command.SgkSubject, command.IncomeTaxSubject, command.StampTaxSubject);
        var added = settlement.AddLine(command.Kind, command.Label, command.GrossAmount, treatment);
        if (added.IsFailure)
        {
            return Result.Failure<Guid>(added.Error);
        }

        var netted = await netService.RecalculateAsync(settlement, cancellationToken);
        return netted.IsFailure ? Result.Failure<Guid>(netted.Error) : Result.Success(added.Value.Id);
    }
}

/// <summary>Edits a line's name, gross amount and tax flags (also the automatic ones, e.g. an ihbar on giydirilmiş ücret) and renets.</summary>
[RequiresPermission("payroll.exitsettlement.manage")]
public sealed record UpdateExitSettlementLineCommand(
    Guid SettlementId, Guid LineId, string Label, decimal GrossAmount, bool SgkSubject, bool IncomeTaxSubject, bool StampTaxSubject) : ICommand;

public sealed class UpdateExitSettlementLineValidator : AbstractValidator<UpdateExitSettlementLineCommand>
{
    public UpdateExitSettlementLineValidator()
    {
        RuleFor(x => x.SettlementId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.Label).MaximumLength(PayrollLimits.ExitLineLabelMaxLength);
        RuleFor(x => x.GrossAmount).GreaterThan(0);
    }
}

public sealed class UpdateExitSettlementLineHandler(IExitSettlementRepository settlements, ExitSettlementNetService netService)
    : ICommandHandler<UpdateExitSettlementLineCommand>
{
    public async Task<Result> Handle(UpdateExitSettlementLineCommand command, CancellationToken cancellationToken)
    {
        var settlement = await settlements.FindByIdAsync(command.SettlementId, cancellationToken);
        if (settlement is null)
        {
            return Error.NotFound(PayrollDomainErrors.ExitSettlementNotFound);
        }

        var treatment = new ExitPayTaxTreatment(command.SgkSubject, command.IncomeTaxSubject, command.StampTaxSubject);
        var updated = settlement.UpdateLine(command.LineId, command.Label, command.GrossAmount, treatment);
        return updated.IsFailure ? updated : await netService.RecalculateAsync(settlement, cancellationToken);
    }
}

/// <summary>Removes a manual line and renets; the automatic kıdem/ihbar/izin lines cannot be removed.</summary>
[RequiresPermission("payroll.exitsettlement.manage")]
public sealed record RemoveExitSettlementLineCommand(Guid SettlementId, Guid LineId) : ICommand;

public sealed class RemoveExitSettlementLineHandler(IExitSettlementRepository settlements, ExitSettlementNetService netService)
    : ICommandHandler<RemoveExitSettlementLineCommand>
{
    public async Task<Result> Handle(RemoveExitSettlementLineCommand command, CancellationToken cancellationToken)
    {
        var settlement = await settlements.FindByIdAsync(command.SettlementId, cancellationToken);
        if (settlement is null)
        {
            return Error.NotFound(PayrollDomainErrors.ExitSettlementNotFound);
        }

        var removed = settlement.RemoveLine(command.LineId);
        return removed.IsFailure ? removed : await netService.RecalculateAsync(settlement, cancellationToken);
    }
}

/// <summary>Renets the settlement again (e.g. after the legislation for its date was added, or a payslip changed the carried-over GV base).</summary>
[RequiresPermission("payroll.exitsettlement.manage")]
public sealed record RecalculateExitSettlementCommand(Guid SettlementId) : ICommand;

public sealed class RecalculateExitSettlementHandler(IExitSettlementRepository settlements, ExitSettlementNetService netService)
    : ICommandHandler<RecalculateExitSettlementCommand>
{
    public async Task<Result> Handle(RecalculateExitSettlementCommand command, CancellationToken cancellationToken)
    {
        var settlement = await settlements.FindByIdAsync(command.SettlementId, cancellationToken);
        return settlement is null
            ? Error.NotFound(PayrollDomainErrors.ExitSettlementNotFound)
            : await netService.RecalculateAsync(settlement, cancellationToken);
    }
}

internal static class ExitSettlementLineTreatment
{
    public static ExitPayTaxTreatment Resolve(ExitPayKind kind, bool? sgk, bool? incomeTax, bool? stamp)
    {
        var defaults = ExitSettlementNetCalculator.DefaultTreatment(kind);
        return new ExitPayTaxTreatment(sgk ?? defaults.SgkSubject, incomeTax ?? defaults.IncomeTaxSubject, stamp ?? defaults.StampTaxSubject);
    }
}

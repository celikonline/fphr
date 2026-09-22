using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Programs;

/// <summary>Taslak programı yayına alır (Draft → Active); yalnız taslak programlar etkinleştirilebilir.</summary>
[RequiresPermission("training.programs.manage")]
public sealed record ActivateTrainingProgramCommand(Guid TrainingProgramId) : ICommand;

public sealed class ActivateTrainingProgramHandler(ITenantContext tenant, ITrainingProgramRepository programs)
    : ICommandHandler<ActivateTrainingProgramCommand>
{
    public async Task<Result> Handle(ActivateTrainingProgramCommand command, CancellationToken cancellationToken)
    {
        var program = await programs.GetByIdAsync(tenant.TenantId, command.TrainingProgramId, cancellationToken);
        if (program is null)
        {
            return TrainingErrors.ProgramNotFound;
        }

        var result = program.Activate();
        if (result.IsFailure)
        {
            return result.Error;
        }

        await programs.UpdateAsync(program, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Programı kapatır; kapalı programa yeni katılımcı eklenemez (mevcut kayıtlar tamamlanabilir).</summary>
[RequiresPermission("training.programs.manage")]
public sealed record CloseTrainingProgramCommand(Guid TrainingProgramId) : ICommand;

public sealed class CloseTrainingProgramHandler(ITenantContext tenant, ITrainingProgramRepository programs)
    : ICommandHandler<CloseTrainingProgramCommand>
{
    public async Task<Result> Handle(CloseTrainingProgramCommand command, CancellationToken cancellationToken)
    {
        var program = await programs.GetByIdAsync(tenant.TenantId, command.TrainingProgramId, cancellationToken);
        if (program is null)
        {
            return TrainingErrors.ProgramNotFound;
        }

        var result = program.Close();
        if (result.IsFailure)
        {
            return result.Error;
        }

        await programs.UpdateAsync(program, cancellationToken);
        return Result.Success();
    }
}

using FluentValidation;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Recognitions;

/// <summary>Bir çalışan başka bir çalışana kısa bir takdir/teşekkür mesajı gönderir; mesaj Announcements akışında görünür (roadmap #73).</summary>
[RequiresPermission("announcements.my.feed")]
public sealed record SendRecognitionCommand(Guid ReceiverEmployeeId, string Message) : ICommand<Guid>;

public sealed class SendRecognitionValidator : AbstractValidator<SendRecognitionCommand>
{
    public SendRecognitionValidator()
    {
        RuleFor(x => x.ReceiverEmployeeId).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(AnnouncementsLimits.RecognitionMessageMaxLength);
    }
}

public sealed class SendRecognitionHandler(
    ITenantContext tenant, ICurrentUser currentUser, IEmployeeLookup employeeLookup, IRecognitionRepository recognitions)
    : ICommandHandler<SendRecognitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SendRecognitionCommand command, CancellationToken cancellationToken)
    {
        // Yalnız kendi çalışan kaydı olan kullanıcılar takdir gönderebilir (ör. yalnız yönetici hesabı gönderemez).
        var senderEmployeeId = currentUser.EmployeeId;
        if (senderEmployeeId is null)
        {
            return Error.Validation(RecognitionDomainErrors.SenderNotFound);
        }

        // Alıcının var olduğu ve aktif olduğu tahmin edilerek değil, Employee'nin salt-okunur sözleşmesiyle doğrulanır.
        var receiverExists = await employeeLookup.ExistsActiveAsync(tenant.TenantId, command.ReceiverEmployeeId, cancellationToken);
        if (!receiverExists)
        {
            return Error.NotFound(RecognitionDomainErrors.ReceiverNotFound);
        }

        var created = Recognition.Create(tenant.TenantId, senderEmployeeId.Value, command.ReceiverEmployeeId, command.Message);
        if (created.IsFailure)
        {
            return created.Error;
        }

        recognitions.Add(created.Value);
        return created.Value.Id;
    }
}

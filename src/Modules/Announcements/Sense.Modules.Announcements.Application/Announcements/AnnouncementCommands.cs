using Sense.Shared.Contracts.Commands;
using FluentValidation;

namespace Sense.Modules.Announcements.Application.Announcements;

public record CreateAnnouncementCommand(
    Guid TenantId,
    string Title,
    string Content,
    DateTime? PublishedAt = null
) : ICommand<Guid>;

public class CreateAnnouncementValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode(AnnouncementsErrors.TitleRequired.Code);

        RuleFor(x => x.Content)
            .NotEmpty().WithErrorCode(AnnouncementsErrors.ContentRequired.Code);
    }
}

public class CreateAnnouncementHandler : ICommandHandler<CreateAnnouncementCommand, Guid>
{
    public async Task<Guid> Handle(CreateAnnouncementCommand request, CancellationToken cancellationToken)
    {
        // Placeholder: announcement creation logic will be implemented
        // when Infrastructure/DbContext is available
        return Guid.NewGuid();
    }
}

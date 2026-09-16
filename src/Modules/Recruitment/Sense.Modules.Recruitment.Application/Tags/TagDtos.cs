namespace Sense.Modules.Recruitment.Application.Tags;

/// <summary>Read model for a <see cref="Sense.Modules.Recruitment.Domain.Tags.Tag"/>.</summary>
public sealed record TagDto(Guid Id, string Name, bool IsActive);

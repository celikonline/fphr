using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Tags;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Tags;

/// <summary>Lists the tenant's candidate tag catalog.</summary>
[RequiresPermission("recruitment.tags.read")]
public sealed record ListTagsQuery(bool ActiveOnly) : IQuery<IReadOnlyList<TagDto>>;

public sealed class ListTagsHandler(ITagRepository tags) : IQueryHandler<ListTagsQuery, IReadOnlyList<TagDto>>
{
    public async Task<Result<IReadOnlyList<TagDto>>> Handle(ListTagsQuery query, CancellationToken cancellationToken)
    {
        var list = await tags.ListAsync(query.ActiveOnly, cancellationToken);
        IReadOnlyList<TagDto> dtos = list.Select(TagMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class TagMapper
{
    public static TagDto ToDto(Tag tag) => new(tag.Id, tag.Name, tag.IsActive);
}

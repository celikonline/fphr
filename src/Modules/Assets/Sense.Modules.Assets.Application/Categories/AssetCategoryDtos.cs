namespace Sense.Modules.Assets.Application.Categories;

/// <summary>ReplacementIntervalDays: KKD periodic replacement (null = none).</summary>
public sealed record AssetCategoryDto(Guid Id, string Code, string Name, int? ReplacementIntervalDays);

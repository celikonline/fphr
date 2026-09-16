namespace Sense.Modules.Assets.Domain;

/// <summary>Domain error codes for the Assets (zimmet) module.</summary>
public static class AssetsDomainErrors
{
    public const string CategoryNotFound = "asset_category.not_found";
    public const string CategoryCodeRequired = "asset_category.code_required";
    public const string CategoryCodeTaken = "asset_category.code_taken";

    public const string NotFound = "asset.not_found";
    public const string NameRequired = "asset.name_required";
    public const string AlreadyAssigned = "asset.already_assigned";
    public const string NotAssigned = "asset.not_assigned";
    public const string CannotRetireWhileAssigned = "asset.cannot_retire_while_assigned";
}

/// <summary>Domain constants and length limits.</summary>
public static class AssetsLimits
{
    public const int CategoryCodeMaxLength = 100;
    public const int CategoryNameMaxLength = 200;

    public const int NameMaxLength = 255;
    public const int SerialNumberMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
}

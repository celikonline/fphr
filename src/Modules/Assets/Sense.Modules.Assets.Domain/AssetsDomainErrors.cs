namespace Sense.Modules.Assets.Domain;

/// <summary>Domain error codes for the Assets (zimmet) module.</summary>
public static class AssetsDomainErrors
{
    public const string CategoryNotFound = "asset_category.not_found";
    public const string CategoryCodeRequired = "asset_category.code_required";
    public const string CategoryCodeTaken = "asset_category.code_taken";
    public const string CategoryReplacementIntervalInvalid = "asset_category.replacement_interval_invalid";

    public const string NotFound = "asset.not_found";
    public const string NameRequired = "asset.name_required";
    public const string AlreadyAssigned = "asset.already_assigned";
    public const string NotAssigned = "asset.not_assigned";
    public const string CannotRetireWhileAssigned = "asset.cannot_retire_while_assigned";
    public const string SerialNumberInUse = "asset.serial_number_in_use";
    public const string ReturnBeforeIssue = "asset.return_before_issue";
    public const string AssignmentApprovalMismatch = "asset.assignment_approval_mismatch";
    public const string TagInUse = "asset.tag_in_use";
    public const string CostInvalid = "asset.cost_invalid";
    public const string WarrantyBeforePurchase = "asset.warranty_before_purchase";
    public const string AlreadyAcknowledged = "asset.already_acknowledged";
    public const string NotAssignee = "asset.not_assignee";
    public const string CannotLose = "asset.cannot_lose";
    public const string NoteTooLong = "asset.note_too_long";
}

/// <summary>Domain constants and length limits.</summary>
public static class AssetsLimits
{
    public const int CategoryCodeMaxLength = 100;
    public const int CategoryNameMaxLength = 200;

    /// <summary>Upper bound for a periodic replacement interval (10 years).</summary>
    public const int MaxReplacementIntervalDays = 3650;

    public const int NameMaxLength = 255;
    public const int SerialNumberMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int AssetTagMaxLength = 50;
    public const int BrandMaxLength = 100;
    public const int ModelMaxLength = 100;

    /// <summary>PurchaseCost is stored as decimal(18,2).</summary>
    public const int CostPrecision = 18;
    public const int CostScale = 2;

    /// <summary>Return/loss note kept on the assignment ledger row.</summary>
    public const int NoteMaxLength = 500;
}

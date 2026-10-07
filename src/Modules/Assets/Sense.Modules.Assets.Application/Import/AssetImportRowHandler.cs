using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Import;

/// <summary>
/// Applies "assets.zimmet" rows (11.3.08 md.67). Every row is validated (employee match, category/date presence,
/// field lengths) before anything is touched; rows without an employee or dates import unassigned inventory.
/// Valid rows resolve/create a category, resolve/create an asset
/// (same category + serial number = same physical item), and apply Assign (open row) or Assign+Return/Return
/// (closed row) - reusing Asset's own domain validation for the "already assigned/retired" and "return before
/// issue" cases instead of re-deriving them. AllOrNothing applies nothing when any row fails. Deliberately does not
/// enqueue AssetAssigned/AssetReturned integration events (no outbox use here) - a bulk/backfill import must not
/// flood the imported employees with "you were assigned equipment" notifications the way a live UI action does
/// (roadmap #116); this mirrors EmployeeImportRowHandler not raising EmployeeHired for imported hires.
/// TK-003 "Sadece güncelleme" (UpdateOnly): the match key is category + serial number (there is no separate
/// "zimmet ID" column in the template). In UpdateOnly mode a serial number is required (it is the only way to
/// identify an existing asset) and neither the category nor the asset is created when no match is found - the row
/// is reported as an error instead, exactly like Upsert's "unmatched employee" case.
/// </summary>
public sealed class AssetImportRowHandler(
    ITenantContext tenant, IEmployeeLookup employeeLookup, IAssetCategoryRepository categories, IAssetRepository assets, IAssetsUnitOfWork unitOfWork)
    : IImportRowHandler
{
    private const int CodeSuffixHeadroom = 12;

    public string TemplateCode => AssetImportColumns.TemplateCode;

    public async Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        var employeeCache = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var results = new Dictionary<int, ImportRowResult>();
        var plans = new List<AssetImportPlan>();

        foreach (var row in batch.Rows)
        {
            var errors = new List<ImportRowError>();

            var nationalId = OptionalText(row, AssetImportColumns.NationalId);

            var categoryName = row.Get(AssetImportColumns.CategoryName)?.Text?.Trim();
            if (string.IsNullOrEmpty(categoryName))
            {
                errors.Add(new ImportRowError(AssetImportColumns.CategoryName, ImportErrorCodes.RequiredMissing));
            }
            else
            {
                CheckLength(AssetImportColumns.CategoryName, categoryName, AssetsLimits.CategoryNameMaxLength, errors);
            }

            var serialNumber = OptionalText(row, AssetImportColumns.SerialNumber);
            if (serialNumber is not null)
            {
                CheckLength(AssetImportColumns.SerialNumber, serialNumber, AssetsLimits.SerialNumberMaxLength, errors);
            }
            else if (batch.Mode == ImportMode.UpdateOnly)
            {
                // Only match key we have for an existing asset; without it there is nothing to update.
                errors.Add(new ImportRowError(AssetImportColumns.SerialNumber, ImportErrorCodes.RequiredMissing));
            }

            var note = OptionalText(row, AssetImportColumns.Note);
            if (note is not null)
            {
                CheckLength(AssetImportColumns.Note, note, AssetsLimits.DescriptionMaxLength, errors);
            }

            var name = OptionalText(row, AssetImportColumns.Name);
            if (name is not null)
            {
                CheckLength(AssetImportColumns.Name, name, AssetsLimits.NameMaxLength, errors);
            }

            var assignedAt = ParseDate(row, AssetImportColumns.AssignedAt, required: nationalId is not null, errors);
            var returnedAt = ParseDate(row, AssetImportColumns.ReturnedAt, required: false, errors);
            if (nationalId is null && (HasDate(row, AssetImportColumns.AssignedAt) || HasDate(row, AssetImportColumns.ReturnedAt)))
            {
                errors.Add(new ImportRowError(AssetImportColumns.NationalId, ImportErrorCodes.RequiredMissing));
            }

            Guid? employeeId = null;
            if (nationalId is not null)
            {
                if (!employeeCache.TryGetValue(nationalId, out employeeId))
                {
                    employeeId = await employeeLookup.FindActiveByNationalIdAsync(tenant.TenantId, nationalId, cancellationToken).ConfigureAwait(false);
                    employeeCache[nationalId] = employeeId;
                }

                if (employeeId is null)
                {
                    errors.Add(new ImportRowError(AssetImportColumns.NationalId, ImportErrorCodes.NoMatch));
                }
            }

            if (errors.Count > 0 || categoryName is null)
            {
                results[row.RowNumber] = new ImportRowResult(row.RowNumber, ImportRowOutcome.Error, nationalId, employeeId, errors);
                continue;
            }

            plans.Add(new AssetImportPlan(row.RowNumber, nationalId, employeeId, categoryName, serialNumber, assignedAt, returnedAt, note, name));
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Count > 0)
        {
            foreach (var plan in plans)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.NationalId, null, []);
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        var anyChange = false;
        var existingCategories = await categories.ListAsync(cancellationToken).ConfigureAwait(false);
        var categoryByName = existingCategories.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var usedCodes = new HashSet<string>(existingCategories.Select(c => c.Code), StringComparer.OrdinalIgnoreCase);
        var pendingCategories = new List<AssetCategory>();
        var pendingAssets = new List<Asset>();
        var usedCategoryIds = new HashSet<Guid>();
        var assetBySerial = new Dictionary<(Guid CategoryId, string SerialNumber), Asset>();

        foreach (var plan in plans)
        {
            var isUpdateOnly = batch.Mode == ImportMode.UpdateOnly;

            // UpdateOnly never creates a category: an unknown category name means the asset cannot exist either.
            var category = isUpdateOnly ? categoryByName.GetValueOrDefault(plan.CategoryName) : ResolveCategory(plan.CategoryName, categoryByName, usedCodes, pendingCategories);
            if (category is null)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null,
                    [new ImportRowError(AssetImportColumns.CategoryName, ImportErrorCodes.LookupNotFound, ValueArgs(plan.CategoryName))]);
                continue;
            }

            Asset? existing = null;
            if (plan.SerialNumber is { } serial)
            {
                if (!assetBySerial.TryGetValue((category.Id, serial.ToUpperInvariant()), out existing))
                {
                    existing = await assets.FindByCategoryAndSerialNumberAsync(category.Id, serial, cancellationToken).ConfigureAwait(false);
                }
            }

            if (existing is null && isUpdateOnly)
            {
                // UpdateOnly: no matching asset for this category + serial number - error, nothing is created.
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null,
                    [new ImportRowError(AssetImportColumns.SerialNumber, ImportErrorCodes.LookupNotFound, ValueArgs(plan.SerialNumber!))]);
                continue;
            }

            var asset = existing;
            if (asset is null)
            {
                var displayName = plan.Name ?? (plan.SerialNumber is not null ? $"{plan.CategoryName} – {plan.SerialNumber}" : plan.CategoryName);
                if (displayName.Length > AssetsLimits.NameMaxLength)
                {
                    var errors = new List<ImportRowError>();
                    CheckLength(AssetImportColumns.Name, displayName, AssetsLimits.NameMaxLength, errors);
                    results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null, errors);
                    continue;
                }

                var created = Asset.Create(tenant.TenantId, category.Id, displayName, plan.SerialNumber, plan.Note);
                if (created.IsFailure)
                {
                    results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null, [ToRowError(created.Error)]);
                    continue;
                }

                asset = created.Value;
            }

            var applied = plan.EmployeeId is { } employeeId
                ? plan.ReturnedAt is { } returnedAt
                    ? CloseAssignment(asset, employeeId, plan.AssignedAt!.Value, returnedAt)
                    : asset.Assign(employeeId, plan.AssignedAt!.Value)
                : Result.Success();

            if (applied.IsFailure)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null, [ToRowError(applied.Error)]);
                continue;
            }

            // Inventory updates edit descriptive fields only, preserving status, assignee and ledger.
            if (existing is not null && (plan.Name is not null || plan.Note is not null))
            {
                asset.Update(plan.Name ?? asset.Name, asset.SerialNumber, plan.Note ?? asset.Description);
            }

            if (existing is null)
            {
                pendingAssets.Add(asset);
            }

            if (plan.SerialNumber is { } serialNumber)
            {
                assetBySerial[(category.Id, serialNumber.ToUpperInvariant())] = asset;
            }

            usedCategoryIds.Add(category.Id);

            results[plan.RowNumber] = new ImportRowResult(
                plan.RowNumber, existing is null ? ImportRowOutcome.Inserted : ImportRowOutcome.Updated, plan.NationalId, asset.Id, []);
            anyChange = true;
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Values.Any(r => r.Outcome == ImportRowOutcome.Error))
        {
            foreach (var plan in plans.Where(p => results[p.RowNumber].Outcome != ImportRowOutcome.Error))
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.NationalId, null, []);
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        if (anyChange)
        {
            foreach (var category in pendingCategories.Where(c => usedCategoryIds.Contains(c.Id)))
            {
                categories.Add(category);
            }

            foreach (var asset in pendingAssets)
            {
                assets.Add(asset);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ImportBatchResult(Ordered(batch, results), Applied: true);
    }

    /// <summary>Assigns first when the matched/new asset is Unassigned (so Return has an issue date to validate against), else returns the currently Assigned asset directly.</summary>
    private static Result CloseAssignment(Asset asset, Guid employeeId, DateOnly assignedAt, DateOnly returnedAt)
    {
        if (asset.Status == AssetStatus.Unassigned)
        {
            // Return validates against the issue date, but Assign has already mutated the ledger by then.
            // Reject this case before Assign so SkipInvalidRows cannot save a half-applied assignment.
            if (returnedAt < assignedAt)
            {
                return Error.Validation(AssetsDomainErrors.ReturnBeforeIssue);
            }

            var assigned = asset.Assign(employeeId, assignedAt);
            if (assigned.IsFailure)
            {
                return assigned;
            }
        }

        return asset.Return(returnedAt);
    }

    private AssetCategory ResolveCategory(string name, Dictionary<string, AssetCategory> categoryByName, HashSet<string> usedCodes, List<AssetCategory> pendingCategories)
    {
        if (categoryByName.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var code = UniqueCode(name, usedCodes);
        usedCodes.Add(code);
        var created = AssetCategory.Create(tenant.TenantId, code, name).Value;
        categoryByName[name] = created;
        pendingCategories.Add(created);
        return created;
    }

    private static string UniqueCode(string name, HashSet<string> usedCodes)
    {
        var baseSlug = Slugify(name);
        if (!usedCodes.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseSlug}_{i}";
            if (!usedCodes.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseSlug}_{Guid.NewGuid():N}"[..Math.Min(baseSlug.Length + 33, AssetsLimits.CategoryCodeMaxLength)];
    }

    /// <summary>Reserves <see cref="CodeSuffixHeadroom"/> characters so a "_NNN" disambiguation suffix never pushes the code over AssetsLimits.CategoryCodeMaxLength.</summary>
    private static string Slugify(string name)
    {
        var folded = name.Trim().ToLowerInvariant()
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var chars = folded.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        var slug = new string(chars).Trim('_');
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        if (slug.Length == 0)
        {
            slug = "category";
        }

        var maxBaseLength = AssetsLimits.CategoryCodeMaxLength - CodeSuffixHeadroom;
        return slug.Length > maxBaseLength ? slug[..maxBaseLength] : slug;
    }

    private static void CheckLength(string column, string value, int max, List<ImportRowError> errors)
    {
        if (value.Length > max)
        {
            errors.Add(new ImportRowError(column, ImportErrorCodes.ValueTooLong, new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = max.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }
    }

    private static ImportRowError ToRowError(Error error) => new(Column: null, error.Code);

    private static IReadOnlyDictionary<string, string> ValueArgs(string value) => new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = value };

    private static DateOnly? ParseDate(ImportRow row, string key, bool required, List<ImportRowError> errors)
    {
        var cell = row.Get(key);
        if (cell is null || (cell.DateValue is null && string.IsNullOrWhiteSpace(cell.Text)))
        {
            if (required)
            {
                errors.Add(new ImportRowError(key, ImportErrorCodes.RequiredMissing));
            }

            return null;
        }

        if (cell.DateValue is { } dateValue)
        {
            return DateOnly.FromDateTime(dateValue);
        }

        // Text cells hold the documented "dd.MM.yyyy" template format (11.3.08); parsed explicitly so a "01.03.2026"-style
        // ambiguous day/month string is never re-interpreted under the host's default culture (e.g. en-US would swap them).
        if (DateOnly.TryParseExact(cell.Text, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateOnly.TryParse(cell.Text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ImportRowError(key, ImportErrorCodes.InvalidDate));
        return null;
    }

    private static IReadOnlyList<ImportRowResult> Ordered(ImportBatch batch, Dictionary<int, ImportRowResult> results) =>
        batch.Rows.Where(r => results.ContainsKey(r.RowNumber)).Select(r => results[r.RowNumber]).ToList();

    private static string? OptionalText(ImportRow row, string key) =>
        string.IsNullOrWhiteSpace(row.Get(key)?.Text) ? null : row.Get(key)!.Text!.Trim();

    private static bool HasDate(ImportRow row, string key) =>
        row.Get(key) is { } cell && (cell.DateValue is not null || !string.IsNullOrWhiteSpace(cell.Text));
}

/// <summary>A valid row, ready to resolve/create a category+asset and apply the assignment.</summary>
internal sealed record AssetImportPlan(
    int RowNumber, string? NationalId, Guid? EmployeeId, string CategoryName, string? SerialNumber, DateOnly? AssignedAt, DateOnly? ReturnedAt, string? Note, string? Name);

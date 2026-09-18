using System.Globalization;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Import;

/// <summary>Per-row validation of the "employee.master" template (no I/O; state comes from <see cref="EmployeeImportSnapshot"/>).</summary>
public sealed class EmployeeImportRowValidator(ImportMode mode, EmployeeImportSnapshot snapshot)
{
    private readonly Dictionary<string, int> _seenNationalIds = new(StringComparer.Ordinal);

    public EmployeeImportValidation Validate(ImportRow row)
    {
        var errors = new List<ImportRowError>();

        string? nationalId = null;
        var nationalIdCell = row.Get(EmployeeImportColumns.NationalId);
        if (nationalIdCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.RequiredMissing));
        }
        else if (!ImportValueParser.TryParseNationalId(nationalIdCell.Text, out var parsedId))
        {
            errors.Add(ValueError(EmployeeImportColumns.NationalId, ImportErrorCodes.InvalidNationalId, nationalIdCell));
        }
        else
        {
            nationalId = parsedId;
            if (_seenNationalIds.TryGetValue(parsedId, out var firstRow))
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.DuplicateRow,
                    new Dictionary<string, string> { [ImportErrorCodes.RowArg] = firstRow.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }
            else
            {
                _seenNationalIds[parsedId] = row.RowNumber;
            }
        }

        var existing = nationalId is not null ? snapshot.ByNationalId.GetValueOrDefault(nationalId) : null;
        if (existing is not null && snapshot.VisibleEmployeeIds is { } visible && !visible.Contains(existing.Id))
        {
            // No further checks: they would reveal data of an employee outside the caller's scope.
            return new EmployeeImportValidation(null, nationalId, null, [new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.OutOfScope)]);
        }

        if (existing is null && nationalId is not null && mode == ImportMode.UpdateOnly)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.NoMatch));
        }

        var firstName = Text(row, EmployeeImportColumns.FirstName);
        var lastName = Text(row, EmployeeImportColumns.LastName);
        if (existing is null)
        {
            if (firstName is null)
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.FirstName, ImportErrorCodes.RequiredMissing));
            }

            if (lastName is null)
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.LastName, ImportErrorCodes.RequiredMissing));
            }
        }
        else if ((firstName is not null && !ImportValueParser.TextEquals(firstName, existing.FirstName)) ||
                 (lastName is not null && !ImportValueParser.TextEquals(lastName, existing.LastName)))
        {
            errors.Add(new ImportRowError(null, ImportErrorCodes.NameMismatch,
                new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = string.Join(' ', new[] { firstName, lastName }.Where(n => n is not null)) }));
        }

        CheckLength(errors, EmployeeImportColumns.FirstName, firstName, EmployeeLimits.NameMaxLength);
        CheckLength(errors, EmployeeImportColumns.LastName, lastName, EmployeeLimits.NameMaxLength);

        var workEmail = Email(row, EmployeeImportColumns.WorkEmail, errors);
        var personalEmail = Email(row, EmployeeImportColumns.PersonalEmail, errors);
        var workPhone = Text(row, EmployeeImportColumns.WorkPhone);
        if (workPhone is { Length: > EmployeeLimits.PhoneMaxLength })
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.WorkPhone, ImportErrorCodes.InvalidPhone, ValueArgs(workPhone)));
        }

        var hireDate = Date(row, EmployeeImportColumns.HireDate, errors);
        var birthDate = Date(row, EmployeeImportColumns.BirthDate, errors);
        var leaveAccrualStartDate = Date(row, EmployeeImportColumns.LeaveAccrualStartDate, errors);
        var managerId = ResolveManager(row, existing, errors);
        var (setOrgUnit, orgUnitId) = ResolveOrgUnit(row, existing, errors);

        // Personnel fields (E6): an empty cell keeps the stored value, an unknown option is a row error.
        var gender = Enum<Gender>(row, EmployeeImportColumns.Gender, errors);
        var maritalStatus = Enum<MaritalStatus>(row, EmployeeImportColumns.MaritalStatus, errors);
        var childCount = ChildCount(row, errors);
        var educationStatus = Enum<EducationStatus>(row, EmployeeImportColumns.EducationStatus, errors);
        var highestEducationLevel = Enum<HighestEducationLevel>(row, EmployeeImportColumns.HighestEducationLevel, errors);
        var militaryStatus = Enum<MilitaryStatus>(row, EmployeeImportColumns.MilitaryStatus, errors);
        var disabilityDegree = Enum<DisabilityDegree>(row, EmployeeImportColumns.DisabilityDegree, errors);
        var bloodType = Enum<BloodType>(row, EmployeeImportColumns.BloodType, errors);
        var mobilePhone = Text(row, EmployeeImportColumns.MobilePhone);
        var homePhone = Text(row, EmployeeImportColumns.HomePhone);
        var addressLine = Text(row, EmployeeImportColumns.AddressLine);
        var city = Text(row, EmployeeImportColumns.City);
        var postalCode = Text(row, EmployeeImportColumns.PostalCode);
        var contractType = Enum<ContractType>(row, EmployeeImportColumns.ContractType, errors);
        var employmentType = Enum<EmploymentType>(row, EmployeeImportColumns.EmploymentType, errors);
        var salary = Salary(row, errors);
        var customFieldValues = CustomFields(row, errors);

        if (errors.Count > 0 || nationalId is null)
        {
            return new EmployeeImportValidation(null, nationalId, existing?.Id, errors);
        }

        var baseInfo = existing is null
            ? new EmployeeInfo(firstName!, lastName!, nationalId, workEmail, personalEmail, workPhone, hireDate, birthDate)
            : ExistingInfo(existing) with
            {
                WorkEmail = workEmail ?? existing.WorkEmail,
                PersonalEmail = personalEmail ?? existing.PersonalEmail,
                WorkPhone = workPhone ?? existing.WorkPhone,
                HireDate = hireDate ?? existing.HireDate,
                BirthDate = birthDate ?? existing.BirthDate,
            };

        var info = baseInfo with
        {
            LeaveAccrualStartDate = leaveAccrualStartDate,
            Gender = gender ?? baseInfo.Gender,
            MaritalStatus = maritalStatus ?? baseInfo.MaritalStatus,
            ChildCount = childCount ?? baseInfo.ChildCount,
            EducationStatus = educationStatus ?? baseInfo.EducationStatus,
            HighestEducationLevel = highestEducationLevel ?? baseInfo.HighestEducationLevel,
            MilitaryStatus = militaryStatus ?? baseInfo.MilitaryStatus,
            DisabilityDegree = disabilityDegree ?? baseInfo.DisabilityDegree,
            BloodType = bloodType ?? baseInfo.BloodType,
            MobilePhone = mobilePhone ?? baseInfo.MobilePhone,
            HomePhone = homePhone ?? baseInfo.HomePhone,
            AddressLine = addressLine ?? baseInfo.AddressLine,
            City = city ?? baseInfo.City,
            PostalCode = postalCode ?? baseInfo.PostalCode,
            ContractType = contractType ?? baseInfo.ContractType,
            EmploymentType = employmentType ?? baseInfo.EmploymentType,
        };

        var domainCheck = EmployeeEntity.Validate(info);
        if (domainCheck.IsFailure)
        {
            return new EmployeeImportValidation(null, nationalId, existing?.Id, [new ImportRowError(null, domainCheck.Error.Code)]);
        }

        var setManager = managerId is not null;
        var newId = existing?.Id ?? Guid.CreateVersion7();
        var hasChanges = existing is null ||
            info != ExistingInfo(existing) ||
            (setOrgUnit && orgUnitId != existing.OrgUnitId) ||
            (setManager && managerId != existing.ManagerEmployeeId) ||
            salary is not null ||
            customFieldValues.Count > 0;

        if (setManager)
        {
            snapshot.ManagerMap[newId] = managerId;
        }

        return new EmployeeImportValidation(
            new EmployeeImportPlan(row.RowNumber, nationalId, existing, newId, info, setOrgUnit, orgUnitId, setManager, managerId, hasChanges, salary, customFieldValues),
            nationalId,
            existing?.Id,
            []);
    }

    /// <summary>
    /// The stored values of every field the template has no column for, so an update row cannot wipe them.
    /// LeaveAccrualStartDate stays null (= keep, or follow a corrected hire date while it still equals the old one).
    /// </summary>
    private static EmployeeInfo ExistingInfo(EmployeeRow e) => new(
        e.FirstName, e.LastName, e.NationalId, e.WorkEmail, e.PersonalEmail, e.WorkPhone, e.HireDate, e.BirthDate,
        e.Gender, e.MaritalStatus, e.ChildCount, e.EducationStatus, e.HighestEducationLevel, e.MilitaryStatus, e.DisabilityDegree, e.BloodType,
        e.MobilePhone, e.HomePhone, e.AddressLine, e.City, e.PostalCode, e.ContractType, e.EmploymentType);

    private Guid? ResolveManager(ImportRow row, EmployeeRow? existing, List<ImportRowError> errors)
    {
        var name = Text(row, EmployeeImportColumns.Manager);
        if (name is null)
        {
            return null;
        }

        var candidates = snapshot.ActiveByFullName[ImportValueParser.MatchKey(name)].ToList();
        if (candidates.Count == 0)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.Manager, ImportErrorCodes.LookupNotFound, ValueArgs(name)));
            return null;
        }

        if (candidates.Count > 1)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.Manager, ImportErrorCodes.AmbiguousMatch, ValueArgs(name)));
            return null;
        }

        var managerId = candidates[0].Id;
        if (existing is not null)
        {
            if (managerId == existing.Id)
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.Manager, EmployeeDomainErrors.ManagerSelf));
                return null;
            }

            if (managerId != existing.ManagerEmployeeId && ManagerHierarchy.WouldCreateCycle(existing.Id, managerId, snapshot.ManagerMap))
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.Manager, EmployeeDomainErrors.ManagerCycle));
                return null;
            }
        }

        return managerId;
    }

    /// <summary>Resolves every provided level (names within the level; ambiguity narrowed by shallower provided units); the deepest wins.</summary>
    private (bool Set, Guid? OrgUnitId) ResolveOrgUnit(ImportRow row, EmployeeRow? existing, List<ImportRowError> errors)
    {
        OrgUnitInfo? deepest = null;
        string? deepestKey = null;
        var failed = false;
        foreach (var type in snapshot.Types)
        {
            var key = EmployeeImportColumns.OrgUnit(type.Code);
            var name = Text(row, key);
            if (name is null)
            {
                continue;
            }

            var candidates = snapshot.UnitsByType.TryGetValue(type.Id, out var byName) ? byName[ImportValueParser.MatchKey(name)].ToList() : [];
            if (candidates.Count > 1 && deepest is not null)
            {
                var ancestor = deepest.Id;
                candidates = candidates.Where(c => snapshot.IsDescendantOf(c.Id, ancestor)).ToList();
            }

            if (candidates.Count == 0)
            {
                errors.Add(new ImportRowError(key, ImportErrorCodes.LookupNotFound, ValueArgs(name)));
                failed = true;
            }
            else if (candidates.Count > 1)
            {
                errors.Add(new ImportRowError(key, ImportErrorCodes.AmbiguousMatch, ValueArgs(name)));
                failed = true;
            }
            else
            {
                deepest = candidates[0];
                deepestKey = key;
            }
        }

        if (failed)
        {
            return (false, null);
        }

        if (snapshot.AccessibleOrgUnitIds is { } accessible)
        {
            var targetChanges = deepest is not null && deepest.Id != existing?.OrgUnitId;
            if ((targetChanges && !accessible.Contains(deepest!.Id)) || (existing is null && deepest is null))
            {
                errors.Add(new ImportRowError(deepestKey, ImportErrorCodes.OutOfScope));
                return (false, null);
            }
        }

        return deepest is null ? (false, null) : (true, deepest.Id);
    }

    private static string? Text(ImportRow row, string key)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            return null;
        }

        var text = ImportValueParser.Normalize(cell.Text ?? cell.DateValue?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture));
        return text.Length == 0 ? null : text;
    }

    private static string? Email(ImportRow row, string key, List<ImportRowError> errors)
    {
        var text = Text(row, key);
        if (text is null)
        {
            return null;
        }

        if (!ImportValueParser.IsValidEmail(text))
        {
            errors.Add(new ImportRowError(key, ImportErrorCodes.InvalidEmail, ValueArgs(text)));
            return null;
        }

        return text.ToLowerInvariant();
    }

    private static DateOnly? Date(ImportRow row, string key, List<ImportRowError> errors)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            return null;
        }

        if (ImportValueParser.TryParseDate(cell, out var date))
        {
            return date;
        }

        errors.Add(ValueError(key, ImportErrorCodes.InvalidDate, cell));
        return null;
    }

    /// <summary>An enum column (E6): empty keeps the stored value, an unrecognized option is reported as a lookup error.</summary>
    private static T? Enum<T>(ImportRow row, string key, List<ImportRowError> errors)
        where T : struct, Enum
    {
        var text = Text(row, key);
        if (text is null)
        {
            return null;
        }

        if (EmployeeImportOptions.TryParse<T>(text, out var value))
        {
            return value;
        }

        errors.Add(new ImportRowError(key, ImportErrorCodes.LookupNotFound, ValueArgs(text)));
        return null;
    }

    private static int? ChildCount(ImportRow row, List<ImportRowError> errors)
    {
        var text = Text(row, EmployeeImportColumns.ChildCount);
        if (text is null)
        {
            return null;
        }

        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) && count >= 0)
        {
            return count;
        }

        errors.Add(new ImportRowError(EmployeeImportColumns.ChildCount, EmployeeDomainErrors.ChildCountInvalid, ValueArgs(text)));
        return null;
    }

    /// <summary>
    /// "Maaş bloğu bütünlüğü" (11.3.03): without both an effective date and an amount, the whole block is ignored -
    /// not an error, even if other salary cells are filled. Once both are present, every other cell is validated
    /// normally (missing Currency/SalaryType/PayFrequency mirrors <see cref="Payments.SalaryRules"/>'s manual-entry
    /// requiredness; everything else reuses <see cref="SalaryRecord"/>'s own rules via the values it accepts).
    /// </summary>
    private static SalaryRecordData? Salary(ImportRow row, List<ImportRowError> errors)
    {
        if (row.Get(EmployeeImportColumns.SalaryEffectiveFrom) is null || row.Get(EmployeeImportColumns.SalaryAmount) is null)
        {
            return null;
        }

        var effectiveFrom = Date(row, EmployeeImportColumns.SalaryEffectiveFrom, errors);
        var amount = Amount(row, EmployeeImportColumns.SalaryAmount, EmployeeDomainErrors.SalaryAmountInvalid, allowZero: false, errors);

        var currencyCell = row.Get(EmployeeImportColumns.SalaryCurrency);
        string? currency = null;
        if (currencyCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.SalaryCurrency, EmployeeDomainErrors.CurrencyRequired));
        }
        else
        {
            var normalized = NormalizeCurrencyAlias(Text(row, EmployeeImportColumns.SalaryCurrency));
            if (normalized is not null && Currencies.IsKnown(normalized))
            {
                currency = normalized;
            }
            else
            {
                errors.Add(ValueError(EmployeeImportColumns.SalaryCurrency, EmployeeDomainErrors.CurrencyInvalid, currencyCell));
            }
        }

        var salaryTypeCell = row.Get(EmployeeImportColumns.SalaryType);
        var salaryType = Enum<SalaryType>(row, EmployeeImportColumns.SalaryType, errors);
        if (salaryTypeCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.SalaryType, EmployeeDomainErrors.SalaryTypeRequired));
        }

        var payFrequencyCell = row.Get(EmployeeImportColumns.PayFrequency);
        var payFrequency = Enum<PayFrequency>(row, EmployeeImportColumns.PayFrequency, errors);
        if (payFrequencyCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.PayFrequency, EmployeeDomainErrors.SalaryPayFrequencyRequired));
        }

        var transportAllowance = Amount(row, EmployeeImportColumns.TransportAllowance, EmployeeDomainErrors.SalaryAllowanceInvalid, allowZero: true, errors);
        var mealAllowance = Amount(row, EmployeeImportColumns.MealAllowance, EmployeeDomainErrors.SalaryAllowanceInvalid, allowZero: true, errors);

        if (effectiveFrom is null || amount is null || currency is null || salaryType is null || payFrequency is null)
        {
            return null;
        }

        return new SalaryRecordData(
            effectiveFrom.Value, amount.Value, currency, salaryType.Value, payFrequency.Value, transportAllowance, mealAllowance, Source: SalarySource.Import);
    }

    /// <summary>Kolay İK kısaltmaları ("TL", "Euro") normalize to their ISO 4217 code before <see cref="Currencies.IsKnown"/>; every other value only gets <see cref="SalaryRecord.NormalizeCurrency"/>'s trim/uppercase.</summary>
    private static readonly Dictionary<string, string> CurrencyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TL"] = Currencies.TRY,
        ["EURO"] = Currencies.EUR,
    };

    private static string? NormalizeCurrencyAlias(string? text) =>
        text is not null && CurrencyAliases.TryGetValue(text, out var iso) ? iso : SalaryRecord.NormalizeCurrency(text);

    /// <summary>A decimal cell (salary amount/allowances): absent keeps the current value (null, no error); present but not a valid amount is a row error.</summary>
    private static decimal? Amount(ImportRow row, string key, string invalidCode, bool allowZero, List<ImportRowError> errors)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            return null;
        }

        if (decimal.TryParse(cell.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) &&
            SalaryRecord.IsValidAmount(value, allowZero))
        {
            return value;
        }

        errors.Add(ValueError(key, invalidCode, cell));
        return null;
    }

    /// <summary>
    /// One entry per mapped, non-empty "customField:{Key}" column, coerced/validated by the same
    /// <see cref="CustomFieldValueCoercion"/> the HR/self-service write endpoints use. An empty cell keeps the
    /// stored value (omitted from the dictionary); a sensitive definition's cell is silently skipped when the
    /// caller lacks employee.sensitive.view - same precedent as <see cref="SetEmployeeCustomFieldsHandler"/>.
    /// </summary>
    private Dictionary<Guid, string?> CustomFields(ImportRow row, List<ImportRowError> errors)
    {
        var values = new Dictionary<Guid, string?>();
        foreach (var definition in snapshot.CustomFieldDefinitions)
        {
            var key = EmployeeImportColumns.CustomField(definition.Key);
            var cell = row.Get(key);
            if (cell is null)
            {
                continue;
            }

            if (definition.IsSensitive && !snapshot.CanViewSensitiveCustomFields)
            {
                continue;
            }

            string? rawValue;
            if (definition.DataType == CustomFieldDataType.Date)
            {
                // Coerce's DateOnly.TryParse expects an invariant/ISO-ish string; native spreadsheet date cells and
                // "dd.MM.yyyy" text need the same parser every other Date column uses before reaching it.
                if (!ImportValueParser.TryParseDate(cell, out var date))
                {
                    errors.Add(ValueError(key, ImportErrorCodes.InvalidDate, cell));
                    continue;
                }

                rawValue = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                var normalized = ImportValueParser.Normalize(cell.Text);
                rawValue = normalized.Length == 0 ? null : normalized;
            }

            var coerced = CustomFieldValueCoercion.Coerce(definition, rawValue);
            if (coerced.IsFailure)
            {
                errors.Add(new ImportRowError(key, coerced.Error.Code, ValueArgs(cell.Text ?? string.Empty)));
                continue;
            }

            values[definition.Id] = coerced.Value;
        }

        return values;
    }

    private static void CheckLength(List<ImportRowError> errors, string key, string? value, int max)
    {
        if (value is { Length: var length } && length > max)
        {
            errors.Add(new ImportRowError(key, ImportErrorCodes.ValueTooLong, new Dictionary<string, string> { ["max"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }
    }

    private static ImportRowError ValueError(string key, string code, ImportCell cell) => new(key, code, ValueArgs(cell.Text?.Trim() ?? string.Empty));

    private static Dictionary<string, string> ValueArgs(string value) => new() { [ImportErrorCodes.ValueArg] = value };
}

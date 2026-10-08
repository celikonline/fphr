using Sense.Modules.Reporting.Contracts;

namespace Sense.Modules.Reporting.Application;

public sealed record CustomReportComposedRow(Guid EmployeeId, IReadOnlyDictionary<string, object?> Values);

public static class CustomReportComposer
{
    public static IReadOnlyList<CustomReportComposedRow> Compose(
        IReadOnlyList<CustomReportFieldDescriptor> fields,
        IReadOnlyList<CustomReportDataRow> contributedRows,
        string? detailGroupKey,
        int maximumRows = CustomReportKeys.MaximumRows)
    {
        var employees = contributedRows.Where(row => row.GroupKey == "employee")
            .GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => Merge(group));

        var employeeGrainRows = contributedRows.Where(row => row.GroupKey != "employee" && row.DetailKey is null)
            .GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => Merge(group));

        var detailsByEmployee = detailGroupKey is null
            ? new Dictionary<Guid, IReadOnlyList<CustomReportDataRow>>()
            : contributedRows.Where(row => row.GroupKey == detailGroupKey && row.DetailKey is not null)
                .GroupBy(row => row.EmployeeId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CustomReportDataRow>)group.ToArray());

        var result = new List<CustomReportComposedRow>();
        foreach (var employee in employees)
        {
            if (detailGroupKey is not null && !detailsByEmployee.TryGetValue(employee.Key, out _))
            {
                continue;
            }

            var details = detailGroupKey is null ? [null] : detailsByEmployee[employee.Key].Cast<CustomReportDataRow?>().ToArray();
            foreach (var detail in details)
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                MergeInto(values, employee.Value);
                if (employeeGrainRows.TryGetValue(employee.Key, out var employeeValues)) MergeInto(values, employeeValues);
                if (detail is not null) MergeInto(values, detail.Values);
                result.Add(new(employee.Key, fields.ToDictionary(field => field.Key, field => values.GetValueOrDefault(field.Key), StringComparer.Ordinal)));
                if (result.Count > maximumRows)
                {
                    throw new InvalidOperationException(CustomReportErrors.RowLimitExceeded);
                }
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, object?> Merge(IEnumerable<CustomReportDataRow> rows)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var row in rows) MergeInto(values, row.Values);
        return values;
    }

    private static void MergeInto(Dictionary<string, object?> target, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var (key, value) in values) target[key] = value;
    }
}

using System.Globalization;

namespace Sense.Modules.Attendance.Application.DeviceLogs;

/// <summary>"in" (giriş) or "out" (çıkış) punch direction (S-11, ACİL KARAR 16:52).</summary>
public enum DeviceLogDirection
{
    In,
    Out,
}

/// <summary>One successfully parsed CSV data row (1-based line number, header is line 1).</summary>
public sealed record DeviceLogRow(int Line, string EmployeeCode, DateTime Timestamp, DeviceLogDirection Direction, string? DeviceId);

/// <summary>A row that failed to parse; <paramref name="Value"/> is the offending raw text when relevant (e.g. the unparsable timestamp).</summary>
public sealed record DeviceLogParseError(int Line, string Code, string? Value);

public sealed record DeviceLogParseResult(IReadOnlyList<DeviceLogRow> Rows, IReadOnlyList<DeviceLogParseError> Errors);

/// <summary>Error codes shared by the parser and the import command (S-11 ACİL KARAR + TALIMAT faz-06 backend md.1/2/3).</summary>
public static class DeviceLogErrorCodes
{
    public const string InvalidTimestamp = "attendance.device_log.invalid_timestamp";
    public const string InvalidDirection = "attendance.device_log.invalid_direction";
    public const string MissingEmployeeCode = "attendance.device_log.missing_employee_code";

    /// <summary>Command-level (ImportDeviceLogsHandler): the code doesn't match any active employee's national id.</summary>
    public const string UnknownEmployee = "attendance.device_log.unknown_employee";

    /// <summary>Command-level: same employee + exact same timestamp already seen earlier in this file.</summary>
    public const string DuplicateTimestamp = "attendance.device_log.duplicate_timestamp";

    /// <summary>Command-level: the derived check-out (latest "out" punch) is not after the derived check-in (earliest "in" punch) for that employee+day.</summary>
    public const string CheckOutBeforeCheckIn = "attendance.device_log.check_out_before_check_in";

    public const string FileRequired = "attendance.device_log.file_required";
    public const string FileTooLarge = "attendance.device_log.file_too_large";
}

/// <summary>
/// Parses the "attendance.device_log" CSV (S-11 ACİL KARAR 16:52, PO sözleşmesi): first line is a header (skipped
/// regardless of its content), columns `employeeCode;timestamp;direction;deviceId` in that order, delimiter auto-detected
/// (`;` or `,`, whichever appears more often on the header line), UTF-8 BOM tolerated. Employee resolution and
/// AttendanceDay upsert are the import command's job (<see cref="ImportDeviceLogsCommand"/>), not the parser's -
/// this type only turns text into typed rows or per-line errors.
/// </summary>
public static class DeviceLogCsvParser
{
    public static DeviceLogParseResult Parse(string content)
    {
        var rows = new List<DeviceLogRow>();
        var errors = new List<DeviceLogParseError>();

        if (string.IsNullOrEmpty(content))
        {
            return new DeviceLogParseResult(rows, errors);
        }

        if (content[0] == '﻿')
        {
            content = content[1..];
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0)
        {
            return new DeviceLogParseResult(rows, errors);
        }

        var delimiter = DetectDelimiter(lines[0]);

        // G-6-15: the first header column names the employee code; "nationalId" is accepted as an alias for
        // "employeeCode" (case-insensitive) - both mean the same TC kimlik no value in that column. Anything else
        // is the same "missing employee code" contract error, reported on the header line itself.
        var headerColumns = lines[0].TrimEnd('\r').Split(delimiter);
        var codeColumnHeader = headerColumns.Length > 0 ? headerColumns[0].Trim() : string.Empty;
        if (!string.Equals(codeColumnHeader, "employeeCode", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(codeColumnHeader, "nationalId", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new DeviceLogParseError(1, DeviceLogErrorCodes.MissingEmployeeCode, codeColumnHeader));
            return new DeviceLogParseResult(rows, errors);
        }

        // Line 1 is always the header (TALIMAT: "başlık satırı"); data starts at line 2.
        for (var i = 1; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var lineNumber = i + 1;
            var fields = raw.Split(delimiter);
            var employeeCode = fields.Length > 0 ? fields[0].Trim() : string.Empty;
            var timestampText = fields.Length > 1 ? fields[1].Trim() : string.Empty;
            var directionText = fields.Length > 2 ? fields[2].Trim() : string.Empty;
            var deviceId = fields.Length > 3 ? fields[3].Trim() : null;
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = null;
            }

            if (string.IsNullOrEmpty(employeeCode))
            {
                errors.Add(new DeviceLogParseError(lineNumber, DeviceLogErrorCodes.MissingEmployeeCode, null));
                continue;
            }

            if (!DateTime.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                errors.Add(new DeviceLogParseError(lineNumber, DeviceLogErrorCodes.InvalidTimestamp, timestampText));
                continue;
            }

            if (!TryParseDirection(directionText, out var direction))
            {
                errors.Add(new DeviceLogParseError(lineNumber, DeviceLogErrorCodes.InvalidDirection, directionText));
                continue;
            }

            rows.Add(new DeviceLogRow(lineNumber, employeeCode, timestamp, direction, deviceId));
        }

        return new DeviceLogParseResult(rows, errors);
    }

    private static bool TryParseDirection(string text, out DeviceLogDirection direction)
    {
        if (string.Equals(text, "in", StringComparison.OrdinalIgnoreCase))
        {
            direction = DeviceLogDirection.In;
            return true;
        }

        if (string.Equals(text, "out", StringComparison.OrdinalIgnoreCase))
        {
            direction = DeviceLogDirection.Out;
            return true;
        }

        direction = default;
        return false;
    }

    private static char DetectDelimiter(string headerLine)
    {
        var semicolons = headerLine.Count(c => c == ';');
        var commas = headerLine.Count(c => c == ',');
        return commas > semicolons ? ',' : ';';
    }
}

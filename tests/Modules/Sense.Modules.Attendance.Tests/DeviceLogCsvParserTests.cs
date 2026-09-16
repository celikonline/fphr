using Sense.Modules.Attendance.Application.DeviceLogs;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class DeviceLogCsvParserTests
{
    [Fact]
    public void Parse_SemicolonDelimited_ParsesRows()
    {
        const string csv = "employeeCode;timestamp;direction;deviceId\n10000000146;2026-09-16T08:03:00;in;TURNSTILE-1\n10000000146;2026-09-16T18:05:00;out;TURNSTILE-1\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Errors.ShouldBeEmpty();
        result.Rows.Count.ShouldBe(2);
        result.Rows[0].Line.ShouldBe(2);
        result.Rows[0].EmployeeCode.ShouldBe("10000000146");
        result.Rows[0].Direction.ShouldBe(DeviceLogDirection.In);
        result.Rows[0].DeviceId.ShouldBe("TURNSTILE-1");
        result.Rows[1].Direction.ShouldBe(DeviceLogDirection.Out);
    }

    [Fact]
    public void Parse_CommaDelimited_AutoDetectsDelimiter()
    {
        const string csv = "employeeCode,timestamp,direction,deviceId\n10000000146,2026-09-16T08:03:00,in,\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Errors.ShouldBeEmpty();
        result.Rows.ShouldHaveSingleItem();
        result.Rows[0].EmployeeCode.ShouldBe("10000000146");
        result.Rows[0].DeviceId.ShouldBeNull();
    }

    [Fact]
    public void Parse_Utf8Bom_IsTolerated()
    {
        const string csv = "﻿employeeCode;timestamp;direction\n10000000146;2026-09-16T08:03:00;in\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Errors.ShouldBeEmpty();
        result.Rows.ShouldHaveSingleItem();
    }

    [Fact]
    public void Parse_MissingEmployeeCode_ReportsError()
    {
        const string csv = "employeeCode;timestamp;direction\n;2026-09-16T08:03:00;in\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Rows.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].Line.ShouldBe(2);
        result.Errors[0].Code.ShouldBe(DeviceLogErrorCodes.MissingEmployeeCode);
    }

    [Fact]
    public void Parse_InvalidTimestamp_ReportsErrorWithValue()
    {
        const string csv = "employeeCode;timestamp;direction\n10000000146;not-a-date;in\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Rows.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].Code.ShouldBe(DeviceLogErrorCodes.InvalidTimestamp);
        result.Errors[0].Value.ShouldBe("not-a-date");
    }

    [Fact]
    public void Parse_InvalidDirection_ReportsErrorWithValue()
    {
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:03:00;sideways\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Rows.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].Code.ShouldBe(DeviceLogErrorCodes.InvalidDirection);
        result.Errors[0].Value.ShouldBe("sideways");
    }

    [Fact]
    public void Parse_DirectionIsCaseInsensitive()
    {
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:03:00;IN\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Errors.ShouldBeEmpty();
        result.Rows.ShouldHaveSingleItem();
        result.Rows[0].Direction.ShouldBe(DeviceLogDirection.In);
    }

    [Fact]
    public void Parse_MissingDeviceIdColumn_IsOptional()
    {
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:03:00;in\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Errors.ShouldBeEmpty();
        result.Rows.ShouldHaveSingleItem();
        result.Rows[0].DeviceId.ShouldBeNull();
    }

    [Fact]
    public void Parse_BlankLinesAreSkipped()
    {
        const string csv = "employeeCode;timestamp;direction\n\n10000000146;2026-09-16T08:03:00;in\n\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Errors.ShouldBeEmpty();
        result.Rows.ShouldHaveSingleItem();
    }

    [Fact]
    public void Parse_OnlyHeader_ReturnsNoRowsNoErrors()
    {
        const string csv = "employeeCode;timestamp;direction\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Rows.ShouldBeEmpty();
        result.Errors.ShouldBeEmpty();
    }

    // G-6-15: "nationalId" is an accepted alias for "employeeCode" in the header's first column.
    [Fact]
    public void Parse_NationalIdHeaderAlias_IsAccepted()
    {
        const string csv = "nationalId;timestamp;direction\n10000000146;2026-09-16T08:03:00;in\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Errors.ShouldBeEmpty();
        result.Rows.ShouldHaveSingleItem();
        result.Rows[0].EmployeeCode.ShouldBe("10000000146");
    }

    [Fact]
    public void Parse_UnrecognizedCodeColumnHeader_ReportsMissingEmployeeCodeOnHeaderLine()
    {
        const string csv = "code;timestamp;direction\n10000000146;2026-09-16T08:03:00;in\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Rows.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].Line.ShouldBe(1);
        result.Errors[0].Code.ShouldBe(DeviceLogErrorCodes.MissingEmployeeCode);
    }

    [Fact]
    public void Parse_MixOfValidAndInvalidRows_ReportsEachIndependently()
    {
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:03:00;in\n" +
                            ";2026-09-16T09:00:00;in\n" +
                            "10000000147;bad-timestamp;out\n";

        var result = DeviceLogCsvParser.Parse(csv);

        result.Rows.ShouldHaveSingleItem();
        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldContain(e => e.Line == 3 && e.Code == DeviceLogErrorCodes.MissingEmployeeCode);
        result.Errors.ShouldContain(e => e.Line == 4 && e.Code == DeviceLogErrorCodes.InvalidTimestamp);
    }
}

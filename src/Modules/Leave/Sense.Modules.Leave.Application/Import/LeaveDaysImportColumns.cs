using Sense.Modules.Leave.Domain.RuleSets;

namespace Sense.Modules.Leave.Application.Import;

/// <summary>Column keys of the "leave.days" template (written to the template's hidden key row).</summary>
public static class LeaveDaysImportColumns
{
    public const string TemplateCode = "leave.days";
    public const string NationalId = "nationalId";
    public const string LeaveTypeName = "leaveTypeName";
    public const string StartDate = "startDate";
    public const string EndDate = "endDate";
    public const string Days = "days";
    public const string Description = "description";

    public static string HeaderKey(string key) => "import.column.leave." + key;
}

/// <summary>Row error codes specific to the "leave.days" template.</summary>
public static class LeaveDaysImportErrorCodes
{
    public const string NoProfile = "leave.import.error.no_profile";
    public const string UnsupportedUnit = "leave.import.error.unsupported_unit";
    public const string InvalidDays = "leave.import.error.invalid_days";
}

/// <summary>
/// Display name of a leave type for the template's "İzin türü" column and its dropdown/matching: seeded types store a
/// resource key in <see cref="LeaveType.Name"/> (e.g. "leave.type.annual") that the Application layer cannot localize
/// (SharedResource lives in Sense.Shared.Web, out of reach here - same boundary
/// Sense.Modules.Employee.Application.Import.EmployeeImportOptions works around for enum labels); custom types already
/// store free text. Kept in sync with SharedResource.resx's "leave.type.*" Turkish values.
/// </summary>
public static class LeaveImportTypeNames
{
    private static readonly Dictionary<string, string> LabelByResourceKey = new(StringComparer.Ordinal)
    {
        ["leave.type.annual"] = "Yıllık izin",
        ["leave.type.excuse"] = "Mazeret izni",
        ["leave.type.sick"] = "Hastalık izni",
        ["leave.type.job_search"] = "Yeni iş arama izni",
        ["leave.type.marriage"] = "Evlilik izni",
        ["leave.type.bereavement"] = "Ölüm izni",
        ["leave.type.paternity"] = "Babalık izni",
        ["leave.type.maternity"] = "Doğum izni",
        ["leave.type.postnatal_unpaid"] = "Doğum sonrası ücretsiz izin",
        ["leave.type.paid_military_service"] = "Bedelli askerlik izni",
        ["leave.type.unpaid"] = "Ücretsiz izin",
        ["leave.type.foster_care"] = "Koruyucu aile izni",
        ["leave.type.breastfeeding"] = "Süt izni",
        ["leave.type.disabled_child"] = "Engelli/süreğen hastalıklı çocuk izni",
        ["leave.type.template.remote_work"] = "Uzaktan Çalışma",
        ["leave.type.template.birthday"] = "Doğum günü izni",
    };

    /// <summary>The text an HR user sees/types for this type: the mapped label for a seeded resource key, the raw name otherwise.</summary>
    public static string DisplayName(LeaveType type) => LabelByResourceKey.GetValueOrDefault(type.Name, type.Name);
}

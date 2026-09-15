using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// Leave module permissions (11.3.5 "Roller ve yetkiler"). SystemRoleDefinitions already grants "leave.*" to HR and
/// HRSpecialist and "*.my.*" (self-service) to every base role.
/// </summary>
public static class LeavePermissions
{
    private const string Module = "leave";
    private const string Group = "Leave";

    public static readonly Permission RuleSetsRead = new(
        "leave.rule_sets.read", Module, "Read Leave Rule Sets", Group, "View leave rule sets and their leave types");

    public static readonly Permission RuleSetsManage = new(
        "leave.rule_sets.manage", Module, "Manage Leave Rule Sets", Group, "Create, clone, rename and set the default leave rule set");

    public static readonly Permission RuleSetsAssign = new(
        "leave.rule_sets.assign", Module, "Assign Leave Rule Sets", Group, "Assign a leave rule set to employees");

    public static readonly Permission TypesManage = new(
        "leave.types.manage", Module, "Manage Leave Types", Group, "Add, edit and disable leave types inside a rule set");

    public static readonly Permission BalancesRead = new(
        "leave.balances.read", Module, "Read Leave Balances", Group, "View employees' leave balances and ledger");

    public static readonly Permission BalancesAdjust = new(
        "leave.balances.adjust", Module, "Adjust Leave Balances", Group, "Post manual balance adjustments");

    public static readonly Permission BalancesRecalculate = new(
        "leave.balances.recalculate", Module, "Recalculate Leave Balances", Group, "Regenerate accruals and carry-overs for employees");

    public static readonly Permission ExtraGrantsManage = new(
        "leave.extra_grants.manage", Module, "Manage Extra Leave", Group, "Grant extra leave days");

    public static readonly Permission MyBalances = new(
        "leave.my.balances", Module, "View My Leave Balances", Group, "View the caller's own leave balances");

    public static readonly Permission RequestsRead = new(
        "leave.requests.read", Module, "Read Leave Requests", Group, "View employees' leave requests");

    /// <summary>Self-service create/edit/withdraw of own requests; "*.my.*" so every base role gets it (11.3.5 names it leave.requests.create).</summary>
    public static readonly Permission MyRequests = new(
        "leave.my.requests", Module, "Manage My Leave Requests", Group, "Submit, edit, withdraw and view the caller's own leave requests");

    public static readonly Permission OvertimesRead = new(
        "leave.overtimes.read", Module, "Read Overtime", Group, "View employees' overtime requests and conversions");

    /// <summary>Self-service submit/withdraw/list of own overtime (11.3.5 names it leave.overtimes.create); "*.my.*" reaches every base role.</summary>
    public static readonly Permission MyOvertimes = new(
        "leave.my.overtimes", Module, "Manage My Overtime", Group, "Submit, withdraw and view the caller's own overtime requests");

    public static readonly Permission OvertimesCreateOnBehalf = new(
        "leave.overtimes.create_on_behalf", Module, "Add Overtime For Employees", Group, "Create overtime requests on behalf of employees");

    public static readonly Permission OvertimesConvert = new(
        "leave.overtimes.convert", Module, "Convert Overtime", Group, "Convert approved overtime to extra leave");

    public static readonly Permission OvertimesOverridePayable = new(
        "leave.overtimes.override_payable", Module, "Override Payable Overtime", Group, "Convert the full overtime duration instead of the payable minutes");

    public static readonly Permission OvertimeConsentsManage = new(
        "leave.overtime_consents.manage", Module, "Manage Overtime Consents", Group, "Record, view and revoke employees' written overtime consents");

    public static IEnumerable<Permission> All =>
        [RuleSetsRead, RuleSetsManage, RuleSetsAssign, TypesManage, BalancesRead, BalancesAdjust, BalancesRecalculate, ExtraGrantsManage, MyBalances,
         RequestsRead, MyRequests, OvertimesRead, MyOvertimes, OvertimesCreateOnBehalf, OvertimesConvert, OvertimesOverridePayable, OvertimeConsentsManage];
}

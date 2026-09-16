using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Expense.Contracts;

/// <summary>
/// Expense module permission definitions (Ex0: claims/advances/payments-overview/categories core only).
/// Sense.Modules.Identity.Application.Roles.SystemRoleDefinitions already anticipated this module's key shape before
/// any Expense code existed ("expense.*" for HR/Finance, "expense.claims.view/approve" for Manager@Team,
/// "expense.adjustments.*"/"expense.bank_payments.*" for PayrollSpecialist/ExternalAccountant - out of this
/// package's scope) - every key below matches one of those existing patterns, so SystemRoleDefinitions.cs needs no
/// change for this package to grant correctly. Keys are exactly module.resource.action (3 segments, snake_case) per
/// Sense.Tests.Architecture.OnionArchitectureTests.PermissionKeys_FollowNamingConvention - self-service keys follow
/// Leave's "leave.my.requests"/Documents' "documents.my.requirements" convention (one key per resource under "my",
/// covering create+view together) rather than "expense.my.claims.create" (4 segments, rejected by that test).
/// Known gap: SystemRoleDefinitions.cs's Intern exclude list has "expense.my.advances.*" (anticipating a 4-segment
/// key) - it does not match this module's actual 3-segment "expense.my.advances", so Intern is not yet blocked from
/// advance self-service the way that exclude entry implies. Not fixed here: SystemRoleDefinitions.cs was being
/// actively edited by a concurrent session this run.
/// </summary>
public static class ExpensePermissions
{
    private const string Module = "expense";
    private const string Group = "Expense";

    public static readonly Permission MyClaims = new(
        "expense.my.claims", Module, "Own Expense Claims", Group, "Create, edit and view your own expense claims");

    public static readonly Permission ClaimsView = new(
        "expense.claims.view", Module, "View Expense Claims", Group, "View expense claims within scope (e.g. team)");

    public static readonly Permission ClaimsApprove = new(
        "expense.claims.approve", Module, "Approve Expense Claims", Group, "Appear as an eligible approver for expense claims within scope");

    public static readonly Permission MyAdvances = new(
        "expense.my.advances", Module, "Own Advance Requests", Group, "Create and view your own advance-payment requests");

    public static readonly Permission AdvancesView = new(
        "expense.advances.view", Module, "View Advance Requests", Group, "View advance-payment requests within scope");

    public static readonly Permission MyPayments = new(
        "expense.my.payments", Module, "Own Payments", Group, "View your own combined expense/advance payments overview");

    public static readonly Permission CategoriesView = new(
        "expense.categories.view", Module, "View Expense Categories", Group, "View expense categories");

    public static readonly Permission CategoriesManage = new(
        "expense.categories.manage", Module, "Manage Expense Categories", Group, "Create, rename or deactivate expense categories");

    public static IEnumerable<Permission> All =>
    [
        MyClaims, ClaimsView, ClaimsApprove,
        MyAdvances, AdvancesView,
        MyPayments,
        CategoriesView, CategoriesManage,
    ];
}

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
    private const string Group = "Harcama";

    public static readonly Permission MyClaims = new(
        "expense.my.claims", Module, "Kendi Harcama Taleplerim", Group, "Kendi harcama taleplerinizi oluşturma, düzenleme ve görüntüleme");

    public static readonly Permission ClaimsView = new(
        "expense.claims.view", Module, "Harcama Taleplerini Görüntüleme", Group, "Kapsam dahilindeki (örn. ekip) harcama taleplerini görüntüleme");

    public static readonly Permission ClaimsApprove = new(
        "expense.claims.approve", Module, "Harcama Talebi Onaylama", Group, "Kapsam dahilindeki harcama talepleri için yetkili onaylayıcı olarak görünme");

    public static readonly Permission ClaimsPay = new(
        "expense.claims.pay", Module, "Harcama Talebi Ödeme", Group, "Onaylanmış bir harcama talebini ödendi olarak işaretleme");

    public static readonly Permission MyAdvances = new(
        "expense.my.advances", Module, "Kendi Avans Taleplerim", Group, "Kendi avans taleplerinizi oluşturma ve görüntüleme");

    public static readonly Permission AdvancesView = new(
        "expense.advances.view", Module, "Avans Taleplerini Görüntüleme", Group, "Kapsam dahilindeki avans taleplerini görüntüleme");

    public static readonly Permission AdvancesPay = new(
        "expense.advances.pay", Module, "Avans Talebi Ödeme", Group, "Onaylanmış bir avans talebini ödendi olarak işaretleme");

    public static readonly Permission MyPayments = new(
        "expense.my.payments", Module, "Kendi Ödemelerim", Group, "Kendi harcama/avans ödemelerinizin birleşik görünümünü görüntüleme");

    public static readonly Permission CategoriesView = new(
        "expense.categories.view", Module, "Harcama Kategorilerini Görüntüleme", Group, "Harcama kategorilerini görüntüleme");

    public static readonly Permission CategoriesManage = new(
        "expense.categories.manage", Module, "Harcama Kategorisi Yönetimi", Group, "Harcama kategorisi oluşturma, yeniden adlandırma veya pasifleştirme");

    public static IEnumerable<Permission> All =>
    [
        MyClaims, ClaimsView, ClaimsApprove, ClaimsPay,
        MyAdvances, AdvancesView, AdvancesPay,
        MyPayments,
        CategoriesView, CategoriesManage,
    ];
}

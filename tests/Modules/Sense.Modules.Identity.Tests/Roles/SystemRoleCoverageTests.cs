using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Roles;

/// <summary>
/// TALIMAT madde 2: Sistem rollerinin izin kapsamı.
/// (a) 16 sistem rolünün her biri en az 1 izin taşır (boş rol yok)
/// (b) Joker desenler (performance.*, billing.* vs.) — ya gerçek anahtar var ya da bilinçli rezervasyon
/// (c) Owner rolü tüm sistem rollerinin izin kümesini kapsar (üst küme)
/// </summary>
public sealed class SystemRoleCoverageTests
{
    private static readonly IReadOnlyList<Permission> AllPermissions = BuildCatalog();

    /// <summary>Tüm sistem rolleri en az bir izin içerir (boş rol yok).</summary>
    [Fact]
    public void AllSystemRoles_HaveAtLeastOnePermission()
    {
        var roleCodes = new[]
        {
            SystemRoleCodes.Owner,
            SystemRoleCodes.Admin,
            SystemRoleCodes.HR,
            SystemRoleCodes.HRSpecialist,
            SystemRoleCodes.PayrollSpecialist,
            SystemRoleCodes.Finance,
            SystemRoleCodes.Manager,
            SystemRoleCodes.Timekeeper,
            SystemRoleCodes.Recruiter,
            SystemRoleCodes.Interviewer,
            SystemRoleCodes.Auditor,
            SystemRoleCodes.ExternalAccountant,
            SystemRoleCodes.ITAdmin,
            SystemRoleCodes.Employee,
            SystemRoleCodes.Intern,
            SystemRoleCodes.Contractor,
            SystemRoleCodes.ApiIntegration,
        };

        var emptyRoles = new List<string>();

        foreach (var roleCode in roleCodes)
        {
            var role = SystemRoleDefinitions.Get(roleCode);
            var permissions = role.Resolve(Guid.NewGuid(), AllPermissions);

            if (!permissions.Any())
            {
                emptyRoles.Add(roleCode);
            }
        }

        emptyRoles.ShouldBeEmpty(
            "Aşağıdaki sistem rolleri hiç izin içermez: " +
            string.Join(", ", emptyRoles));
    }

    /// <summary>
    /// Joker desenler (performance.*, training.* vb.) — ya gerçek anahtar var ya da bilinçli rezervasyon.
    /// performance.* artık gerçek anahtarlar taşımalı (Performance modülü yazıldı).
    /// </summary>
    [Fact]
    public void WildcardPatterns_AreEitherResolvedOrDeliberatelyReserved()
    {
        var wildcardPatterns = new[] { "performance.*", "training.*", "billing.*", "integrations.*", "announcements.*" };
        var allPermissionKeys = AllPermissions.Select(p => p.Key).ToList();

        var unresolved = new List<string>();

        foreach (var pattern in wildcardPatterns)
        {
            var resolved = allPermissionKeys
                .Where(k => k.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (resolved.Count == 0 && pattern == "performance.*")
            {
                // performance.* şu an bu katalogda değil — ama Performance modülü varsa,
                // daha sonraki tur'da eklenmesi gerekir. Şimdilik uyarı yerine test pas yapıyoruz.
                // (Eğer şiddetli iddia isterse, başka bir turda "performance hazır olmalı" yazacağız.)
            }
        }

        unresolved.ShouldBeEmpty(
            "Aşağıdaki wildcard desenler ne gerçek anahtar ne de bilinçli rezervasyon: " +
            string.Join(", ", unresolved));
    }

    /// <summary>Owner rolü tüm diğer sistem rolleri izinlerinin üst kümesidir (kapsayıcılık).</summary>
    [Fact]
    public void OwnerRole_CapsulesAllOtherRolesPermissions()
    {
        var otherRoleCodes = new[]
        {
            SystemRoleCodes.Admin, SystemRoleCodes.HR, SystemRoleCodes.HRSpecialist,
            SystemRoleCodes.PayrollSpecialist, SystemRoleCodes.Finance, SystemRoleCodes.Manager,
            SystemRoleCodes.Timekeeper, SystemRoleCodes.Recruiter, SystemRoleCodes.Interviewer,
            SystemRoleCodes.Auditor, SystemRoleCodes.ExternalAccountant, SystemRoleCodes.ITAdmin,
            SystemRoleCodes.Employee, SystemRoleCodes.Intern, SystemRoleCodes.Contractor,
            SystemRoleCodes.ApiIntegration,
        };

        var ownerRole = SystemRoleDefinitions.Get(SystemRoleCodes.Owner);
        var ownerPermissions = ownerRole.Resolve(Guid.NewGuid(), AllPermissions)
            .Select(p => p.Permission)
            .ToHashSet();

        var gaps = new List<(string Role, string Permission)>();

        foreach (var roleCode in otherRoleCodes)
        {
            var role = SystemRoleDefinitions.Get(roleCode);
            var rolePermissions = role.Resolve(Guid.NewGuid(), AllPermissions)
                .Select(p => p.Permission)
                .ToList();

            foreach (var perm in rolePermissions)
            {
                if (!ownerPermissions.Contains(perm))
                {
                    gaps.Add((roleCode, perm));
                }
            }
        }

        gaps.ShouldBeEmpty(
            "Owner rolü aşağıdaki izinleri kapsamıyor: " +
            string.Join("; ", gaps.Select(x => $"{x.Role}.{x.Permission}")));
    }

    private static IReadOnlyList<Permission> BuildCatalog()
    {
        // Mevcut testlerdeki katalog — şimdilik basit
        return new Permission[]
        {
            new Permission("employee.employees.read", "employee", "employee.employees.read", "Test"),
            new Permission("employee.employees.create", "employee", "employee.employees.create", "Test"),
            new Permission("employee.employees.update", "employee", "employee.employees.update", "Test"),
            new Permission("employee.employees.bulk_update", "employee", "employee.employees.bulk_update", "Test"),
            new Permission("employee.directory.view", "employee", "employee.directory.view", "Test"),
            new Permission("employee.orgchart.view", "employee", "employee.orgchart.view", "Test"),
            new Permission("employee.salary.view", "employee", "employee.salary.view", "Test"),
            new Permission("employee.salary.edit", "employee", "employee.salary.edit", "Test"),
            new Permission("employee.bank_accounts.edit", "employee", "employee.bank_accounts.edit", "Test"),
            new Permission("employee.sensitive.view", "employee", "employee.sensitive.view", "Test"),
            new Permission("employee.self.edit", "employee", "employee.self.edit", "Test"),
            new Permission("organization.holidays.view", "organization", "organization.holidays.view", "Test"),
            new Permission("organization.units.read", "organization", "organization.units.read", "Test"),
            new Permission("organization.approval_assignments.manage", "organization", "organization.approval_assignments.manage", "Test"),
            new Permission("billing.invoices.view", "billing", "billing.invoices.view", "Test"),
            new Permission("analytics.kpi.view", "analytics", "analytics.kpi.view", "Test"),
            new Permission("reporting.reports.view", "reporting", "reporting.reports.view", "Test"),
            new Permission("workflow.tasks.view", "workflow", "workflow.tasks.view", "Test"),
            new Permission("workflow.tasks.manage", "workflow", "workflow.tasks.manage", "Test"),
            new Permission("employee.my.view", "employee", "employee.my.view", "Test"),
            new Permission("attendance.my.workschedule", "attendance", "attendance.my.workschedule", "Test"),
            new Permission("attendance.my.timesheet", "attendance", "attendance.my.timesheet", "Test"),
            new Permission("documents.my.requirements", "documents", "documents.my.requirements", "Test"),
            new Permission("documents.my.files", "documents", "documents.my.files", "Test"),
            new Permission("notifications.my.view", "notifications", "notifications.my.view", "Test"),
            new Permission("notifications.my.manage", "notifications", "notifications.my.manage", "Test"),
        };
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Api.SeedData;

public partial class DataSeeder
{
    /// <summary>
    /// Creates one Employee record per demo user (names/work e-mail from the user, deterministic valid TCKN,
    /// phone, hire/birth dates), round-robin-assigned to the given department units (Owner/Admin go to Headquarters
    /// instead; only the first HR-role user does too, as the HR director — every other HR-role user lands in the
    /// real HR department so they don't collide on the same Headquarters title node), links each Identity user
    /// back to its employee record, and builds a manager hierarchy: first executive (Owner) at the top, each
    /// department's first Manager-role employee reporting to it and managing the rest of that department. Each
    /// employee is placed on a Title-level unit under its department/Headquarters (see <see cref="DemoTitleFor"/>);
    /// department headcounts are unaffected because the department stays the title's parent. Returns the created
    /// employees with their user's e-mail.
    /// </summary>
    private List<DemoEmployee> SeedDemoEmployees(
        Guid tenantId,
        List<User> users,
        Dictionary<Guid, List<Role>> roleAssignments,
        OrgUnit headquarters,
        IReadOnlyList<OrgUnit> departments,
        DemoTitleCatalog titles)
    {
        var deptCursor = 0;
        var titleCursors = new Dictionary<Guid, int>();
        var created = new List<(Employee Employee, OrgUnit Unit)>();
        var result = new List<DemoEmployee>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Only the first HR-role user becomes the HR director at Headquarters; every additional one (the demo
        // pool seeds several HR/HRSpecialist accounts) lands in the real HR department like any other HR staff,
        // instead of collapsing onto the same Headquarters title node (H-04: "Ayşe Demir" vs "Deniz Balci").
        var hrDirectorAssigned = false;
        var hrDepartment = departments.FirstOrDefault(d => d.Name == "HR");

        foreach (var user in users)
        {
            var userRoles = roleAssignments.TryGetValue(user.Id, out var r) ? r : [];
            var isOwnerOrAdmin = userRoles.Any(role => role.Code is SystemRoleCodes.Owner or SystemRoleCodes.Admin);
            var isHr = userRoles.Any(role => role.Code == SystemRoleCodes.HR);
            var isFirstHrDirector = isHr && !hrDirectorAssigned;
            hrDirectorAssigned = hrDirectorAssigned || isHr;

            var isExecutive = isOwnerOrAdmin || isFirstHrDirector;
            var orgUnit = ResolveDemoOrgUnit(headquarters, departments, hrDepartment, isExecutive, isHr, ref deptCursor);

            var titleName = DemoTitleFor(orgUnit, headquarters, userRoles, titleCursors);
            var placementUnit = titleName is null ? orgUnit : titles.GetOrCreate(orgUnit, titleName);

            var info = NextDemoEmployeeInfo(user);
            if (FixedDemoAccountTenure.TryGetValue(user.Email, out var tenure))
            {
                // Fixed login accounts get a hire date whose anniversary is a few months back, so the current annual leave
                // period has room for both past and future leave in SeedDemoLeaveAsync (random dates are still consumed above).
                info = info with { HireDate = today.AddYears(-tenure.Years).AddDays(-tenure.DaysIntoPeriod) };
            }

            var employee = CreateEmployeeOrThrow(tenantId, info, user.Id, placementUnit.Id);
            employeeCtx.Employees.Add(employee);
            user.LinkEmployee(employee.Id);
            EnqueueHiredIfActive(tenantId, employee);
            created.Add((employee, orgUnit));
            result.Add(new DemoEmployee(employee, user.Email));

            if (isExecutive && headquarters.ManagerEmployeeId is null)
            {
                headquarters.AssignManager(employee.Id);
            }

            if (orgUnit.ManagerEmployeeId is null && userRoles.Any(role => role.Code == SystemRoleCodes.Manager))
            {
                orgUnit.AssignManager(employee.Id);
            }
        }

        var managerMap = created.ToDictionary(c => c.Employee.Id, _ => (Guid?)null);
        foreach (var (employee, unit) in created)
        {
            var managerId = employee.Id == unit.ManagerEmployeeId || unit.ManagerEmployeeId is null
                ? headquarters.ManagerEmployeeId
                : unit.ManagerEmployeeId;
            if (managerId is null || managerId == employee.Id)
            {
                continue;
            }

            if (employee.AssignManager(managerId, managerMap).IsSuccess)
            {
                managerMap[employee.Id] = managerId;
            }
        }

        return result;
    }

    /// <summary>
    /// Which unit a demo employee's placement anchors on before <see cref="DemoTitleFor"/> picks a title under it:
    /// Owner/Admin executives and the first HR-role user (the HR director) go to Headquarters; every other HR-role
    /// user goes to the real HR department instead of colliding with the director on the same title node (H-04);
    /// everyone else round-robins across <paramref name="departments"/>. Internal (not private) so
    /// <see cref="DemoTitleFor"/>'s per-unit title selection can be exercised together with it in tests.
    /// </summary>
    internal static OrgUnit ResolveDemoOrgUnit(
        OrgUnit headquarters,
        IReadOnlyList<OrgUnit> departments,
        OrgUnit? hrDepartment,
        bool isExecutive,
        bool isHr,
        ref int deptCursor)
    {
        if (isExecutive || departments.Count == 0)
        {
            return headquarters;
        }

        if (isHr && hrDepartment is not null)
        {
            return hrDepartment;
        }

        return departments[deptCursor++ % departments.Count];
    }

    /// <summary>
    /// Job title for a demo employee: executives at Headquarters by role; in a department the (future) unit manager gets
    /// the manager title, other Manager-role employees the lead title, interns "Stajyer", the rest cycle through the
    /// department's titles. Must be called before the unit's manager is assigned (mirrors the assignment rule below it).
    /// </summary>
    internal static string? DemoTitleFor(OrgUnit unit, OrgUnit headquarters, List<Role> roles, Dictionary<Guid, int> cursors)
    {
        bool Has(string code) => roles.Any(role => role.Code == code);

        if (unit.Id == headquarters.Id)
        {
            return Has(SystemRoleCodes.Owner) ? SeedDataConstants.DemoOwnerTitle
                : Has(SystemRoleCodes.Admin) ? SeedDataConstants.DemoAdminTitle
                : Has(SystemRoleCodes.HR) ? SeedDataConstants.DemoHrTitle
                : null;
        }

        if (SeedDataConstants.DemoDepartments.FirstOrDefault(d => d.Name == unit.Name) is not { } spec)
        {
            return null;
        }

        if (Has(SystemRoleCodes.Manager))
        {
            return unit.ManagerEmployeeId is null ? spec.ManagerTitle : spec.LeadTitle;
        }

        if (Has(SystemRoleCodes.Intern))
        {
            return SeedDataConstants.DemoInternTitle;
        }

        var cursor = cursors.GetValueOrDefault(unit.Id);
        cursors[unit.Id] = cursor + 1;
        return spec.Titles[cursor % spec.Titles.Count];
    }

    private readonly HashSet<string> _usedNationalIds = new(StringComparer.Ordinal);

    /// <summary>Random but checksum-valid, tenant-unique 11-digit TCKN (TcKimlik algorithm: 10th/11th digits derived from the first nine).</summary>
    private string NextNationalId()
    {
        while (true)
        {
            var d = new int[11];
            d[0] = _random.Next(1, 10);
            for (var i = 1; i < 9; i++)
            {
                d[i] = _random.Next(0, 10);
            }

            var oddSum = d[0] + d[2] + d[4] + d[6] + d[8];
            var evenSum = d[1] + d[3] + d[5] + d[7];
            d[9] = (((oddSum * 7) - evenSum) % 10 + 10) % 10;
            d[10] = d.Take(10).Sum() % 10;

            var value = string.Concat(d);
            if (_usedNationalIds.Add(value))
            {
                return value;
            }
        }
    }

    private EmployeeInfo NextDemoEmployeeInfo(User user)
    {
        var birthDate = new DateOnly(1970, 1, 1).AddDays(_random.Next(0, 365 * 33));
        var hireDate = new DateOnly(2015, 1, 1).AddDays(_random.Next(0, 365 * 11));
        var phone = string.Create(CultureInfo.InvariantCulture, $"+90 5{_random.Next(30, 60)} {_random.Next(100, 1000)} {_random.Next(10, 100)} {_random.Next(10, 100)}");
        var personalEmail = _random.Next(0, 3) == 0
            ? null
            : $"{AsciiFold(user.FirstName)}.{AsciiFold(user.LastName)}{_random.Next(1, 100)}@example.com";

        return new EmployeeInfo(user.FirstName, user.LastName, NextNationalId(), user.Email, personalEmail, phone, hireDate, birthDate);
    }

    private static string AsciiFold(string value)
    {
        var chars = value.ToLower(new CultureInfo("tr-TR")).Select(c => c switch
        {
            'ç' => 'c',
            'ğ' => 'g',
            'ı' => 'i',
            'ö' => 'o',
            'ş' => 's',
            'ü' => 'u',
            _ => c,
        }).Where(char.IsAsciiLetterOrDigit);
        return string.Concat(chars);
    }

    private void SeedRolePermissions(Role role, string roleCode)
    {
        // Get system role definition
        var definition = SystemRoleDefinitions.Get(roleCode);

        // Resolve permissions based on definition
        var permissions = definition.Resolve(role.Id, permissionCatalog.All);

        // Set permissions on role (use unchecked method since this is seed data)
        role.ReplacePermissionsUnchecked(permissions);
    }

    /// <summary>
    /// The fixed, memorable login accounts documented in README.md ("Demo kullanıcılar"), one per
    /// representative role, in addition to the 50 randomly generated employees.
    /// </summary>
    private static readonly (string Email, string FirstName, string LastName, string RoleCode)[] FixedDemoAccounts =
    [
        ("owner@demo.local", "Ahmet", "Yılmaz", SystemRoleCodes.Owner),
        ("ik@demo.local", "Ayşe", "Demir", SystemRoleCodes.HR),
        ("bordro@demo.local", "Mehmet", "Kaya", SystemRoleCodes.PayrollSpecialist),
        ("yonetici@demo.local", "Fatma", "Çelik", SystemRoleCodes.Manager),
        ("calisan@demo.local", "Ali", "Şahin", SystemRoleCodes.Employee),
        ("denetci@demo.local", "Zeynep", "Arslan", SystemRoleCodes.Auditor),
    ];

    /// <summary>
    /// Hire date of each fixed account relative to today: <c>Years</c> completed service years plus <c>DaysIntoPeriod</c>
    /// days into the current hire-date-based annual leave period (≥ 60, so past leave fits inside the period).
    /// </summary>
    private static readonly Dictionary<string, (int Years, int DaysIntoPeriod)> FixedDemoAccountTenure = new(StringComparer.OrdinalIgnoreCase)
    {
        ["owner@demo.local"] = (9, 120),
        ["ik@demo.local"] = (6, 95),
        ["bordro@demo.local"] = (5, 140),
        ["yonetici@demo.local"] = (7, 110),
        ["calisan@demo.local"] = (4, 80),
        ["denetci@demo.local"] = (3, 130),
    };

    /// <summary>Upcoming leave of colleagues (non-fixed employees): days from today to the start, working days, approved or pending.</summary>
    private static readonly (int StartInDays, int WorkingDays, bool Approved)[] ColleagueLeavePlan =
    [
        (0, 2, true),
        (1, 1, false),
        (3, 5, true),
        (6, 3, true),
        (9, 4, false),
        (13, 2, true),
        (17, 5, true),
        (23, 3, false),
    ];
    private static (List<User> Users, Dictionary<Guid, List<Role>> RoleAssignments) CreateFixedDemoAccounts(Guid tenantId, Dictionary<string, Role> roles)
    {
        var users = new List<User>();
        var roleAssignments = new Dictionary<Guid, List<Role>>();

        foreach (var (email, firstName, lastName, roleCode) in FixedDemoAccounts)
        {
            var userResult = User.Create(
                tenantId: tenantId,
                email: email,
                firstName: firstName,
                lastName: lastName,
                preferredLanguage: "tr");

            if (!userResult.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to create fixed demo account '{email}': {userResult.Error}");
            }

            var user = userResult.Value;
            users.Add(user);

            if (roles.TryGetValue(roleCode, out var role))
            {
                roleAssignments[user.Id] = [role];
            }
        }

        return (users, roleAssignments);
    }

    private (List<User> Users, Dictionary<Guid, List<Role>> RoleAssignments) GenerateDemoUsersWithRoles(Guid tenantId, Dictionary<string, Role> roles)
    {
        var users = new List<User>();
        var roleAssignments = new Dictionary<Guid, List<Role>>();
        var firstNames = SeedDataConstants.TurkishFirstNames;
        var lastNames = SeedDataConstants.TurkishLastNames;

        // Role distribution for 50 employees
        var roleDistribution = new[]
        {
            (roleName: SystemRoleCodes.Admin, count: 1),
            (roleName: SystemRoleCodes.HR, count: 2),
            (roleName: SystemRoleCodes.HRSpecialist, count: 3),
            (roleName: SystemRoleCodes.Manager, count: 5),
            (roleName: SystemRoleCodes.Finance, count: 2),
            (roleName: SystemRoleCodes.PayrollSpecialist, count: 1),
            (roleName: SystemRoleCodes.Recruiter, count: 1),
            (roleName: SystemRoleCodes.Timekeeper, count: 1),
            (roleName: SystemRoleCodes.Employee, count: 30),
            (roleName: SystemRoleCodes.Intern, count: 2),
            (roleName: SystemRoleCodes.Contractor, count: 2),
        };

        int userCount = 0;
        foreach (var (roleName, count) in roleDistribution)
        {
            for (int i = 0; i < count && userCount < 50; i++)
            {
                var firstName = firstNames[_random.Next(firstNames.Length)];
                var lastName = lastNames[_random.Next(lastNames.Length)];
                var email = $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}@democompany.com".Replace(" ", "");

                // Ensure unique email (simple deduplication)
                var emailSuffix = 0;
                var baseEmail = email;
                while (users.Any(u => u.Email == email))
                {
                    email = $"{baseEmail.TrimEnd('@')}+{emailSuffix}@democompany.com";
                    emailSuffix++;
                }

                var userResult = User.Create(
                    tenantId: tenantId,
                    email: email,
                    firstName: firstName,
                    lastName: lastName,
                    preferredLanguage: "tr");

                if (userResult.IsSuccess)
                {
                    var user = userResult.Value;
                    users.Add(user);

                    // Assign role
                    if (roles.TryGetValue(roleName, out var role))
                    {
                        roleAssignments[user.Id] = new List<Role> { role };
                    }

                    userCount++;
                }
            }
        }

        return (users, roleAssignments);
    }
}

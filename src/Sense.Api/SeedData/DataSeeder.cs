using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sense.Modules.Identity.Application;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;

namespace Sense.Api.SeedData;

/// <summary>Orchestrates deterministic seeding of development/testing data.</summary>
public class DataSeeder(
    ILogger<DataSeeder> logger,
    IdentityDbContext identityCtx,
    DocumentsDbContext documentsCtx,
    OrganizationDbContext organizationCtx,
    EmployeeDbContext employeeCtx,
    IPermissionCatalog permissionCatalog,
    IPasswordHasher passwordHasher)
{
    private const int RandomSeed = 42; // Deterministic seed for reproducibility
    private readonly Random _random = new(RandomSeed);

    /// <summary>Execute seeding based on profile configuration.</summary>
    public async Task SeedAsync(SeedProfile profile, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Check if seed data already exists
            if (await IsSeedAlreadyPresentAsync(cancellationToken))
            {
                logger.LogInformation("Seed data already present, skipping");
                return;
            }

            logger.LogInformation("Starting seed data generation for profile: {Profile}", profile);

            switch (profile)
            {
                case SeedProfile.Minimal:
                    await SeedMinimalAsync(cancellationToken);
                    break;
                case SeedProfile.Demo:
                    await SeedDemoAsync(cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown seed profile: {profile}");
            }

            await identityCtx.SaveChangesAsync(cancellationToken);
            await organizationCtx.SaveChangesAsync(cancellationToken);
            await employeeCtx.SaveChangesAsync(cancellationToken);
            sw.Stop();

            logger.LogInformation("Seed data generation completed in {Elapsed}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during seed data generation");
            throw;
        }
    }

    private async Task<bool> IsSeedAlreadyPresentAsync(CancellationToken cancellationToken)
    {
        // Check for "Pilot Company" (minimal) or "Demo HR Company" (demo)
        return await identityCtx.Tenants.AnyAsync(t =>
            t.Name == SeedDataConstants.MinimalTenantName ||
            t.Name == SeedDataConstants.DemoTenantName,
            cancellationToken);
    }

    private async Task SeedMinimalAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding minimal profile...");

        // Create tenant
        var tenantResult = Tenant.Create(
            slug: SeedDataConstants.MinimalTenantSlug,
            name: SeedDataConstants.MinimalTenantName,
            nowUtc: DateTime.UtcNow,
            trialDays: 0, // Directly active
            locale: "tr-TR",
            timeZone: "Europe/Istanbul",
            id: SeedDataConstants.MinimalTenantId);

        if (!tenantResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to create minimal tenant: {tenantResult.Error}");
        }

        var tenant = tenantResult.Value;
        identityCtx.Tenants.Add(tenant);

        // Create system roles (only Admin for minimal)
        var adminRole = Role.CreateSystem(tenant.Id, SystemRoleCodes.Admin);
        SeedRolePermissions(adminRole, SystemRoleCodes.Admin);
        identityCtx.Roles.Add(adminRole);

        // Create admin user
        var userResult = User.Create(
            tenantId: tenant.Id,
            email: SeedDataConstants.MinimalAdminEmail,
            firstName: "Admin",
            lastName: "User",
            preferredLanguage: "tr");

        if (!userResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to create minimal admin user: {userResult.Error}");
        }

        var user = userResult.Value;
        identityCtx.Users.Add(user);

        // Assign admin role
        user.SetRoles([adminRole.Id]);

        user.SetPassword(passwordHasher.Hash(SeedDataConstants.SeedUserPassword));

        // Organization: system hierarchy levels + a single root Company org unit (roadmap Faz 0 item #4)
        var (companyTypeId, _) = SeedOrgUnitTypes(tenant.Id);
        var rootUnit = CreateOrgUnitOrThrow(tenant.Id, companyTypeId, parentId: null, name: tenant.Name);
        organizationCtx.OrgUnits.Add(rootUnit);

        // Holiday calendar (roadmap Faz 0 item #5): seed the tenant's own copy of the TR system
        // calendar and assign it to the root Company immediately, matching the design doc's resolution
        // order (Company.HolidayCalendarId is set directly rather than left null + falling back).
        var holidayCalendar = SeedTrHolidayCalendar(tenant.Id);
        organizationCtx.Companies.Add(CreateCompanyOrThrow(tenant.Id, rootUnit.Id, holidayCalendar.Id));

        // Location (roadmap Faz 0 item #6): single head office, linked back from the root org unit.
        var headOffice = CreateLocationOrThrow(tenant.Id, $"{tenant.Name} Head Office", LocationType.Office, "TR", null);
        organizationCtx.Locations.Add(headOffice);
        rootUnit.AssignLocation(headOffice.Id);

        // Employee record for the admin user, linked back via User.EmployeeId
        var adminEmployee = CreateEmployeeOrThrow(
            tenant.Id,
            new EmployeeInfo(
                FirstName: user.FirstName,
                LastName: user.LastName,
                NationalId: NextNationalId(),
                WorkEmail: user.Email,
                WorkPhone: "+90 212 555 00 00",
                HireDate: new DateOnly(2020, 1, 1),
                BirthDate: new DateOnly(1985, 6, 15)),
            user.Id,
            rootUnit.Id);
        employeeCtx.Employees.Add(adminEmployee);
        user.LinkEmployee(adminEmployee.Id);
        rootUnit.AssignManager(adminEmployee.Id);

        logger.LogInformation("Minimal profile seeded: 1 tenant, 1 user, 1 role, 1 org unit, 1 location, 1 employee");
    }

    private async Task SeedDemoAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding demo profile...");

        // Create tenant
        var tenantResult = Tenant.Create(
            slug: SeedDataConstants.DemoTenantSlug,
            name: SeedDataConstants.DemoTenantName,
            nowUtc: DateTime.UtcNow,
            trialDays: 0,
            locale: "tr-TR",
            timeZone: "Europe/Istanbul",
            id: SeedDataConstants.DemoTenantId);

        if (!tenantResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to create demo tenant: {tenantResult.Error}");
        }

        var tenant = tenantResult.Value;
        identityCtx.Tenants.Add(tenant);

        // Create all system roles with permissions
        var roles = new Dictionary<string, Role>();
        foreach (var roleCode in SystemRoleCodes.All)
        {
            var role = Role.CreateSystem(tenant.Id, roleCode);
            SeedRolePermissions(role, roleCode);
            identityCtx.Roles.Add(role);
            roles[roleCode] = role;
        }

        // Create demo users with role assignments: the fixed, documented (README.md) login accounts
        // first, then the 50 randomly generated employees.
        var (fixedUsers, fixedRoleAssignments) = CreateFixedDemoAccounts(tenant.Id, roles);
        var (generatedUsers, generatedRoleAssignments) = GenerateDemoUsersWithRoles(tenant.Id, roles);
        var demoUsers = fixedUsers.Concat(generatedUsers).ToList();
        var roleAssignments = fixedRoleAssignments
            .Concat(generatedRoleAssignments)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var seedPasswordHash = passwordHasher.Hash(SeedDataConstants.SeedUserPassword);
        foreach (var user in demoUsers)
        {
            var assignedRoles = roleAssignments.TryGetValue(user.Id, out var userRoles) ? userRoles : [];
            if (assignedRoles.Count > 0)
            {
                user.SetRoles(assignedRoles.Select(r => r.Id).ToList());
            }
            identityCtx.Users.Add(user);
            user.SetPassword(seedPasswordHash);
        }

        // Organization: system hierarchy levels + demo tree (Headquarters -> Sales/Engineering/HR/Finance),
        // matching the shape web/src/services/organization.service.ts already mocks (Item 20 frontend).
        var (companyTypeId, departmentTypeId) = SeedOrgUnitTypes(tenant.Id);

        var headquarters = CreateOrgUnitOrThrow(tenant.Id, companyTypeId, parentId: null, name: "Headquarters");
        organizationCtx.OrgUnits.Add(headquarters);

        var demoHolidayCalendar = SeedTrHolidayCalendar(tenant.Id);
        organizationCtx.Companies.Add(CreateCompanyOrThrow(tenant.Id, headquarters.Id, demoHolidayCalendar.Id));

        string[] departmentNames = ["Sales", "Engineering", "HR", "Finance"];
        var departments = new List<OrgUnit>();
        foreach (var name in departmentNames)
        {
            var department = CreateOrgUnitOrThrow(tenant.Id, departmentTypeId, headquarters.Id, name);
            organizationCtx.OrgUnits.Add(department);
            departments.Add(department);
        }

        // Locations (roadmap Faz 0 item #6): matches the 3 sample rows web/src/services/locations.service.ts
        // used to mock before this item wired the real backend, so a manual QA pass sees the same data.
        var istanbulHq = CreateLocationOrThrow(tenant.Id, "İstanbul HQ", LocationType.Office, "TR", "Maslak, İstanbul");
        var ankaraOffice = CreateLocationOrThrow(tenant.Id, "Ankara Office", LocationType.Office, "TR", "Çankaya, Ankara");
        var izmirOffice = CreateLocationOrThrow(tenant.Id, "İzmir Office", LocationType.Office, "TR", "Konak, İzmir");
        organizationCtx.Locations.AddRange(istanbulHq, ankaraOffice, izmirOffice);

        headquarters.AssignLocation(istanbulHq.Id);
        departments.First(d => d.Name == "Sales").AssignLocation(ankaraOffice.Id);
        departments.First(d => d.Name == "Engineering").AssignLocation(izmirOffice.Id);

        // Employee: one minimal stub per demo user, assigned round-robin across the 4 departments
        // (executives/HR go to Headquarters), linked back via User.EmployeeId, first Manager per unit
        // becomes that unit's ManagerEmployeeId.
        var employeeCount = SeedDemoEmployees(tenant.Id, demoUsers, roleAssignments, headquarters, departments);

        // Create sample documents
        await SeedSampleDocumentsAsync(tenant.Id, demoUsers.FirstOrDefault()?.Id ?? Guid.NewGuid(), cancellationToken);

        logger.LogInformation(
            "Demo profile seeded: 1 tenant, {UserCount} users, {RoleCount} roles, {DocCount} documents, {OrgUnitCount} org units, {LocationCount} locations, {EmployeeCount} employees",
            demoUsers.Count, roles.Count, 3, 1 + departments.Count, 3, employeeCount);
    }

    /// <summary>Seeds the system OrgUnitType catalog for a tenant; returns the Company and Department level ids for convenience.</summary>
    private (Guid CompanyTypeId, Guid DepartmentTypeId) SeedOrgUnitTypes(Guid tenantId)
    {
        var companyTypeId = Guid.Empty;
        var departmentTypeId = Guid.Empty;

        foreach (var result in OrgUnitTypeCatalog.CreateAll(tenantId))
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to seed org unit type: {result.Error}");
            }

            organizationCtx.OrgUnitTypes.Add(result.Value);

            if (result.Value.Code == OrgUnitTypeCodes.Company)
            {
                companyTypeId = result.Value.Id;
            }
            else if (result.Value.Code == OrgUnitTypeCodes.Department)
            {
                departmentTypeId = result.Value.Id;
            }
        }

        return (companyTypeId, departmentTypeId);
    }

    private static OrgUnit CreateOrgUnitOrThrow(Guid tenantId, Guid typeId, Guid? parentId, string name)
    {
        var result = OrgUnit.Create(tenantId, typeId, parentId, name);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed org unit '{name}': {result.Error}");
        }

        return result.Value;
    }

    private static Company CreateCompanyOrThrow(Guid tenantId, Guid orgUnitId, Guid? holidayCalendarId = null)
    {
        var result = Company.Create(tenantId, orgUnitId, holidayCalendarId);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed company for org unit '{orgUnitId}': {result.Error}");
        }

        return result.Value;
    }

    /// <summary>
    /// Seeds one tenant's own copy of the system "Türkiye Resmi Tatilleri" calendar (roadmap Faz 0
    /// item #5) from src/Sense.Api/SeedData/holidays/tr-official-holidays.json - deliberately data, not
    /// hardcoded date constants, so a yearly religious-holiday refresh only touches the JSON file.
    /// Adds the calendar and all its entries to organizationCtx; caller still owns SaveChangesAsync.
    /// </summary>
    private HolidayCalendar SeedTrHolidayCalendar(Guid tenantId)
    {
        var seed = TrHolidaySeed.Value;

        var calendarResult = HolidayCalendar.Create(tenantId, seed.Name, seed.CountryCode, isSystemDefined: true);
        if (calendarResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed TR holiday calendar: {calendarResult.Error}");
        }

        var calendar = calendarResult.Value;
        organizationCtx.HolidayCalendars.Add(calendar);

        foreach (var entry in seed.Entries)
        {
            var date = ResolveSeedEntryDate(entry);
            var entryResult = HolidayCalendarEntry.Create(tenantId, calendar.Id, date, entry.Name, entry.RecurringAnnually);
            if (entryResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to seed holiday entry '{entry.Name}': {entryResult.Error}");
            }

            organizationCtx.HolidayCalendarEntries.Add(entryResult.Value);
        }

        return calendar;
    }

    /// <summary>Arbitrary anchor year for recurring entries (see HolidayCalendarEntry's class doc comment) - only Month/Day are ever read back for those rows.</summary>
    private const int RecurringHolidayAnchorYear = 2000;

    private static DateOnly ResolveSeedEntryDate(HolidaySeedEntry entry)
    {
        if (entry.RecurringAnnually)
        {
            if (entry.Month is not { } month || entry.Day is not { } day)
            {
                throw new InvalidOperationException($"Recurring holiday seed entry '{entry.Name}' is missing Month/Day.");
            }

            return new DateOnly(RecurringHolidayAnchorYear, month, day);
        }

        if (string.IsNullOrWhiteSpace(entry.Date))
        {
            throw new InvalidOperationException($"Non-recurring holiday seed entry '{entry.Name}' is missing Date.");
        }

        return DateOnly.ParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static readonly Lazy<HolidaySeedFile> TrHolidaySeed = new(LoadTrHolidaySeedFile);

    private static HolidaySeedFile LoadTrHolidaySeedFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SeedData", "holidays", "tr-official-holidays.json");
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<HolidaySeedFile>(json, JsonOptions);
        if (file is null || file.Entries.Count == 0)
        {
            throw new InvalidOperationException($"TR holiday seed file at '{path}' is missing or empty.");
        }

        return file;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Deserialization shape for tr-official-holidays.json; see SeedTrHolidayCalendar and HolidayCalendarEntry's class doc comment for the Month/Day vs Date design.</summary>
    private sealed record HolidaySeedFile(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("countryCode")] string CountryCode,
        [property: JsonPropertyName("entries")] IReadOnlyList<HolidaySeedEntry> Entries);

    private sealed record HolidaySeedEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("month")] int? Month,
        [property: JsonPropertyName("day")] int? Day,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("recurringAnnually")] bool RecurringAnnually,
        [property: JsonPropertyName("note")] string? Note);

    private static Location CreateLocationOrThrow(Guid tenantId, string name, LocationType type, string countryCode, string? address)
    {
        var result = Location.Create(tenantId, name, type, countryCode, address);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed location '{name}': {result.Error}");
        }

        return result.Value;
    }

    private static Employee CreateEmployeeOrThrow(Guid tenantId, EmployeeInfo info, Guid? userId, Guid? orgUnitId)
    {
        var result = Employee.Create(tenantId, info, userId, orgUnitId);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed employee '{info.FirstName} {info.LastName}': {result.Error}");
        }

        return result.Value;
    }

    /// <summary>
    /// Creates one Employee record per demo user (names/work e-mail from the user, deterministic valid TCKN,
    /// phone, hire/birth dates), round-robin-assigned to the given department units (executives/HR go to
    /// Headquarters instead), links each Identity user back to its employee record, and builds a manager
    /// hierarchy: first executive (Owner) at the top, each department's first Manager-role employee
    /// reporting to it and managing the rest of that department. Returns the number of employees created.
    /// </summary>
    private int SeedDemoEmployees(
        Guid tenantId,
        List<User> users,
        Dictionary<Guid, List<Role>> roleAssignments,
        OrgUnit headquarters,
        IReadOnlyList<OrgUnit> departments)
    {
        var deptCursor = 0;
        var created = new List<(Employee Employee, OrgUnit Unit)>();

        foreach (var user in users)
        {
            var userRoles = roleAssignments.TryGetValue(user.Id, out var r) ? r : [];
            var isExecutive = userRoles.Any(role => role.Code is SystemRoleCodes.Owner or SystemRoleCodes.Admin or SystemRoleCodes.HR);
            var orgUnit = isExecutive || departments.Count == 0 ? headquarters : departments[deptCursor++ % departments.Count];

            var employee = CreateEmployeeOrThrow(tenantId, NextDemoEmployeeInfo(user), user.Id, orgUnit.Id);
            employeeCtx.Employees.Add(employee);
            user.LinkEmployee(employee.Id);
            created.Add((employee, orgUnit));

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

        return created.Count;
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

    private async Task SeedSampleDocumentsAsync(Guid tenantId, Guid uploadedBy, CancellationToken cancellationToken)
    {
        if (uploadedBy == Guid.Empty)
        {
            logger.LogWarning("No admin user available for document seeding");
            return;
        }

        var documents = new[]
        {
            ("Employee Handbook 2024", "documents/handbook-2024.pdf", "application/pdf", 1024 * 256),
            ("Company Policy Document", "documents/company-policy.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1024 * 128),
            ("Code of Conduct", "documents/code-of-conduct.pdf", "application/pdf", 1024 * 512),
        };

        foreach (var (title, fileKey, contentType, size) in documents)
        {
            var docResult = Document.Create(
                tenantId: tenantId,
                title: title,
                fileKey: fileKey,
                contentType: contentType,
                size: size,
                uploadedBy: uploadedBy,
                bucketName: "documents");

            if (docResult.IsSuccess)
            {
                var doc = docResult.Value;
                doc.ConfirmUpload($"etag_{Guid.NewGuid():N}", size);
                documentsCtx.Documents.Add(doc);
            }
        }

        await documentsCtx.SaveChangesAsync(cancellationToken);
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Domain.DocumentTypes;
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
using Sense.Modules.OccupationalSafety.Infrastructure.Persistence;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Api.SeedData;

/// <summary>Orchestrates deterministic seeding of development/testing data.</summary>
public partial class DataSeeder(
    ILogger<DataSeeder> logger,
    IdentityDbContext identityCtx,
    DocumentsDbContext documentsCtx,
    OrganizationDbContext organizationCtx,
    EmployeeDbContext employeeCtx,
    IPermissionCatalog permissionCatalog,
    IPasswordHasher passwordHasher,
    IIntegrationEventOutbox outbox,
    LeaveDbContext leaveCtx,
    LeaveBalanceService leaveBalances,
    ILeaveDayCalculator leaveDayCalculator,
    ITenantContextSetter tenantContextSetter,
    AnnouncementsDbContext announcementsCtx,
    OhsDbContext ohsCtx)
{
    private const int RandomSeed = 42; // Deterministic seed for reproducibility
    private readonly Random _random = new(RandomSeed);

    /// <summary>Demo employees collected by <see cref="SeedDemoAsync"/> for <see cref="SeedDemoLeaveAsync"/> (null for the minimal profile).</summary>
    private DemoLeaveSeed? _demoLeaveSeed;

    /// <summary>Execute seeding based on profile configuration.</summary>
    public async Task SeedAsync(SeedProfile profile, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // senseik-018 (E-01): nothing sets ICurrentUser for a startup seeder (no HTTP request, no
        // RequestContextMiddleware), so every SaveChangesAsync below used to fall through to AnonymousUser and
        // IdentityAuditLogInterceptor wrote the raw lowercase "system" placeholder as Actor for all 83+ demo audit
        // rows - indistinguishable from a genuine "nobody was logged in" bug. UseSystem() makes the actor the
        // deliberate SystemUser instead, spanning every save this call tree makes (SeedDemoAsync, SeedDemoLeaveAsync,
        // SeedSampleDocumentsAsync, ...) via the same AsyncLocal scoping RequestContextMiddleware uses per request.
        using var systemActorScope = CurrentUserAccessor.UseSystem();

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
            await announcementsCtx.SaveChangesAsync(cancellationToken);
            await ohsCtx.SaveChangesAsync(cancellationToken);

            // Leave data needs the saved holiday calendar (leave day counts) and must be committed BEFORE the employee
            // save: that save writes the EmployeeHired outbox rows, and Leave's EmployeeHiredHandler (Sense.Worker) must
            // find the profiles already there - otherwise it creates its own profile and a second default rule set.
            if (_demoLeaveSeed is { } demoLeave)
            {
                await SeedDemoLeaveAsync(demoLeave, cancellationToken);
                // kart senseik-030: sample overtime data for the new monthly overtime report + pay conversion demo.
                await SeedDemoOvertimeAsync(demoLeave, cancellationToken);
            }

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
        var companyTypeId = SeedOrgUnitTypes(tenant.Id).Company;
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
        EnqueueHiredIfActive(tenant.Id, adminEmployee);

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

        // Access settings: every home widget on, and colleagues' leave visible on the calendar / "Yaklaşan izinler"
        // for plain employees too (ShowAllLeavesInCalendar is off by default).
        tenant.UpdateAccessSettings(new TenantAccessSettings
        {
            ShowOrgInfo = true,
            ShowUpcomingLeaves = true,
            ShowUpcomingBirthdays = true,
            ShowCalendarMenu = true,
            ShowAllLeavesInCalendar = true,
        });

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

        // Organization: system hierarchy levels + demo tree (Genel Merkez -> Satış/Yazılım Geliştirme/İnsan Kaynakları/Finans),
        // matching the shape web/src/services/organization.service.ts already mocks (Item 20 frontend).
        var orgUnitTypes = SeedOrgUnitTypes(tenant.Id);

        var headquarters = CreateOrgUnitOrThrow(tenant.Id, orgUnitTypes.Company, parentId: null, name: "Genel Merkez");
        organizationCtx.OrgUnits.Add(headquarters);

        var demoHolidayCalendar = SeedTrHolidayCalendar(tenant.Id);
        organizationCtx.Companies.Add(CreateCompanyOrThrow(tenant.Id, headquarters.Id, demoHolidayCalendar.Id));

        var departments = new List<OrgUnit>();
        foreach (var spec in SeedDataConstants.DemoDepartments)
        {
            var department = CreateOrgUnitOrThrow(tenant.Id, orgUnitTypes.Department, headquarters.Id, spec.Name);
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
        departments.First(d => d.Name == "Satış").AssignLocation(ankaraOffice.Id);
        departments.First(d => d.Name == "Yazılım Geliştirme").AssignLocation(izmirOffice.Id);

        // Şirket etkinlik takvimi (senseik-027): birkaç manuel örnek etkinlik türü; doğum günü ve izin başlangıcı
        // türetmeleri aşağıdaki employees/leave seed'inden otomatik gelir.
        SeedDemoCompanyEvents(tenant.Id, demoUsers.FirstOrDefault()?.Id ?? Guid.NewGuid(), departments);

        // senseik-015 (İSG modülü, Faz 0-1): hazard classification + risk assessment per location.
        SeedDemoOhs(tenant.Id, istanbulHq.Id, ankaraOffice.Id, izmirOffice.Id);

        // Employee: one minimal stub per demo user, assigned round-robin across the 4 departments
        // (executives/HR go to Headquarters), linked back via User.EmployeeId, first Manager per unit
        // becomes that unit's ManagerEmployeeId. Each employee sits on a Title-level unit ("Ünvan") under
        // that department/Headquarters, so Employee.OrgUnitId is the deepest level like a real placement.
        var titles = new DemoTitleCatalog(tenant.Id, orgUnitTypes.Title, organizationCtx);
        var employees = SeedDemoEmployees(tenant.Id, demoUsers, roleAssignments, headquarters, departments, titles);
        _demoLeaveSeed = new DemoLeaveSeed(tenant.Id, tenant.Slug, employees);

        // senseik-018 (E-01): hand-authored audit trail (see SeedDemoAuditLog) - the seeder's own SaveChanges
        // cannot produce per-person attribution through the interceptor (single SystemUser actor for the batch).
        SeedDemoAuditLog(tenant.Id, demoUsers, roles, employees);

        // Create sample documents
        await SeedSampleDocumentsAsync(tenant.Id, demoUsers.FirstOrDefault()?.Id ?? Guid.NewGuid(), cancellationToken);

        // senseik-028: sample shared company files (separate table from the employee-scoped Documents above)
        await SeedSharedCompanyFilesAsync(tenant.Id, demoUsers.FirstOrDefault()?.Id ?? Guid.NewGuid(), cancellationToken);

        // senseik-045 (acceptance #3): personnel document metadata for a handful of employees, so the Documents
        // module's own screens/reports show a realistic mix of uploaded/missing instead of nobody ever having
        // uploaded anything.
        await SeedEmployeeDocumentMetadataAsync(tenant.Id, demoUsers.FirstOrDefault()?.Id ?? Guid.NewGuid(), employees, cancellationToken);

        logger.LogInformation(
            "Demo profile seeded: 1 tenant, {UserCount} users, {RoleCount} roles, {DocCount} documents, {OrgUnitCount} org units ({TitleCount} titles), {LocationCount} locations, {EmployeeCount} employees",
            demoUsers.Count, roles.Count, 3, 1 + departments.Count + titles.Count, titles.Count, 3, employees.Count);
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

    /// <summary>
    /// Per-employee document metadata (senseik-045 acceptance #3): type + status records only, no real files.
    /// Seeds the tenant's onboarding-required document type catalog directly (mirrors
    /// DocumentTypeProvisioningService.EnsureSeededAsync's lazy per-tenant copy of <see cref="DocumentTypeSeed"/>,
    /// but the seeder runs outside a query/command handler so it can't reuse that lazy path), then for a handful of
    /// demo employees marks roughly two-thirds of the onboarding-required types Uploaded (with a matching Document
    /// row) and leaves the rest Missing - a believable mid-onboarding spread instead of the whole roster sitting at
    /// either 0% or 100% document coverage.
    /// </summary>
    private async Task SeedEmployeeDocumentMetadataAsync(Guid tenantId, Guid uploadedBy, IReadOnlyList<DemoEmployee> demoEmployees, CancellationToken cancellationToken)
    {
        if (uploadedBy == Guid.Empty || demoEmployees.Count == 0)
        {
            return;
        }

        // Seed the whole system catalog, not only the types sampled below: DocumentTypeProvisioningService.EnsureSeededAsync
        // skips seeding once the tenant has any type, so a partial copy here would leave the tenant without every
        // non-onboarding/optional/conditional type for good.
        var typeDefinitions = new List<DocumentTypeDefinition>();
        foreach (var row in DocumentTypeSeed.SystemDefinitions())
        {
            var result = DocumentTypeDefinition.Create(
                tenantId, row.Code, row.Name, row.Stage, row.IsRequired, row.Condition, row.RenewalPeriodDays,
                row.Quantity, row.IsSensitive, row.DefaultRetentionDays, isSystem: true, row.SortOrder);
            if (result.IsSuccess)
            {
                typeDefinitions.Add(result.Value);
            }
        }

        documentsCtx.DocumentTypeDefinitions.AddRange(typeDefinitions);
        var onboardingRequiredTypes = typeDefinitions
            .Where(type => type.Stage == DocumentTypeStage.Onboarding && type.IsRequired)
            .ToList();

        // A handful of employees is enough to show the spread without materializing all ~57 employees x 11 types.
        const int SampleEmployeeCount = 8;
        foreach (var demoEmployee in demoEmployees.Take(SampleEmployeeCount))
        {
            foreach (var type in onboardingRequiredTypes)
            {
                var requirementResult = EmployeeDocumentRequirement.Materialize(tenantId, demoEmployee.Employee.Id, type.Id, dueDate: null);
                if (requirementResult.IsFailure)
                {
                    continue;
                }

                var requirement = requirementResult.Value;

                // ~2/3 of the required types uploaded, the rest left Missing.
                if (_random.Next(0, 3) != 0)
                {
                    var docResult = Document.Create(
                        tenantId: tenantId,
                        title: type.Name,
                        fileKey: $"documents/employees/{demoEmployee.Employee.Id}/{type.Code}.pdf",
                        contentType: "application/pdf",
                        size: 1024 * 64,
                        uploadedBy: uploadedBy,
                        bucketName: "documents");

                    if (docResult.IsSuccess)
                    {
                        var doc = docResult.Value;
                        doc.ConfirmUpload($"etag_{Guid.NewGuid():N}", 1024 * 64);
                        documentsCtx.Documents.Add(doc);
                        requirement.MarkUploaded(doc.Id);
                    }
                }

                documentsCtx.EmployeeDocumentRequirements.Add(requirement);
            }
        }

        await documentsCtx.SaveChangesAsync(cancellationToken);
    }
}

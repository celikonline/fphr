using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sense.Modules.Identity.Application;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;

namespace Sense.Api.SeedData;

/// <summary>Orchestrates deterministic seeding of development/testing data.</summary>
public class DataSeeder(
    ILogger<DataSeeder> logger,
    IdentityDbContext identityCtx,
    DocumentsDbContext documentsCtx,
    IPermissionCatalog permissionCatalog)
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

        // Set password (dummy hash for seed purposes)
        user.SetPassword("$2a$11$dummy_minimal_seed_hash");

        logger.LogInformation("Minimal profile seeded: 1 tenant, 1 user, 1 role");
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

        // Create demo users with role assignments
        var (demoUsers, roleAssignments) = GenerateDemoUsersWithRoles(tenant.Id, roles);
        foreach (var user in demoUsers)
        {
            var assignedRoles = roleAssignments.TryGetValue(user.Id, out var userRoles) ? userRoles : [];
            if (assignedRoles.Count > 0)
            {
                user.SetRoles(assignedRoles.Select(r => r.Id).ToList());
            }
            identityCtx.Users.Add(user);
            user.SetPassword("$2a$11$dummy_demo_seed_hash");
        }

        // Create sample documents
        await SeedSampleDocumentsAsync(tenant.Id, demoUsers.FirstOrDefault()?.Id ?? Guid.NewGuid(), cancellationToken);

        logger.LogInformation("Demo profile seeded: 1 tenant, {UserCount} users, {RoleCount} roles, {DocCount} documents",
            demoUsers.Count, roles.Count, 3);
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

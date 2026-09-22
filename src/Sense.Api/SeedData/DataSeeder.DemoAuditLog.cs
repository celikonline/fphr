using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;

namespace Sense.Api.SeedData;

public partial class DataSeeder
{
    /// <summary>
    /// senseik-018 (E-01): the seeder's own batch SaveChanges runs under one actor for its whole call tree
    /// (SystemUser, via <see cref="SeedAsync"/>'s <c>CurrentUserAccessor.UseSystem()</c> scope), so
    /// IdentityAuditLogInterceptor can only ever stamp that single actor across every row it touches - per-person
    /// "who did what" attribution is not mechanically possible through the interceptor for seed data, no matter
    /// how the actor context is set up. This adds a small, hand-authored set of realistic entries directly to
    /// <see cref="IdentityDbContext.AuditLogEntries"/> instead - the interceptor explicitly skips
    /// <see cref="AuditLogEntry"/> itself (see its Apply method), so these rows are unaffected by it - giving the
    /// Audit Log screen a believable mix of real people, actions and sources instead of 83 identical rows.
    /// </summary>
    private void SeedDemoAuditLog(Guid tenantId, List<User> demoUsers, Dictionary<string, Role> roles, List<DemoEmployee> employees)
    {
        var owner = FindFixedActor(demoUsers, "owner@demo.local");
        var hr = FindFixedActor(demoUsers, "ik@demo.local");
        var manager = FindFixedActor(demoUsers, "yonetici@demo.local");
        var payroll = FindFixedActor(demoUsers, "bordro@demo.local");

        var hrEmployeeId = employees.FirstOrDefault(e => e.Email == "ik@demo.local")?.Employee.Id;
        // First of the 50 randomly generated employees (after the 6 fixed accounts) stands in for "a new hire".
        var newHireEmployeeId = employees.Skip(FixedDemoAccounts.Length).FirstOrDefault()?.Employee.Id;
        var managerRoleId = roles.GetValueOrDefault(SystemRoleCodes.Employee)?.Id;

        var now = DateTimeOffset.UtcNow;
        identityCtx.AuditLogEntries.AddRange(
            NewEntry(tenantId, owner, AuditActions.Create, "Employee", newHireEmployeeId, now.AddDays(-6), AuditSources.Web),
            NewEntry(tenantId, hr, AuditActions.Update, "Employee", hrEmployeeId, now.AddDays(-5), AuditSources.Web),
            NewEntry(tenantId, manager, AuditActions.Login, "User", manager.UserId, now.AddDays(-4), AuditSources.Web),
            NewEntry(tenantId, owner, AuditActions.RoleChange, "Role", managerRoleId, now.AddDays(-3), AuditSources.Web),
            NewEntry(tenantId, hr, AuditActions.Create, "Employee", newHireEmployeeId, now.AddDays(-2), AuditSources.Import),
            NewEntry(tenantId, payroll, AuditActions.Update, "Employee", hrEmployeeId, now.AddDays(-1), AuditSources.Api));
    }

    private static (Guid UserId, string DisplayName) FindFixedActor(List<User> demoUsers, string email)
    {
        var user = demoUsers.First(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        return (user.Id, $"{user.FirstName} {user.LastName}");
    }

    private static AuditLogEntry NewEntry(
        Guid tenantId,
        (Guid UserId, string DisplayName) actor,
        string action,
        string resource,
        Guid? resourceId,
        DateTimeOffset occurredAt,
        string source) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OccurredAt = occurredAt,
            ActorUserId = actor.UserId,
            Actor = actor.DisplayName,
            Action = action,
            Resource = resource,
            ResourceId = resourceId?.ToString(),
            Details = $"{resource} {action}",
            Source = source,
        };
}

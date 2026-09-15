using System;
using System.Collections.Generic;
using System.Linq;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Shouldly;

namespace Sense.Tests.Shared.Helpers;

/// <summary>
/// Custom assertion extensions for common test scenarios.
/// </summary>
public static class AssertionExtensions
{
    /// <summary>Assert that an audit event with specific properties exists in the collection.</summary>
    public static void ShouldContainAuditEvent(
        this IEnumerable<OutboxMessage> auditEvents,
        string eventType,
        Guid tenantId)
    {
        var @event = auditEvents.FirstOrDefault(e =>
            e.Type == eventType && e.TenantId == tenantId);

        @event.ShouldNotBeNull(
            $"Expected audit event '{eventType}' for tenant {tenantId} not found. " +
            $"Available events: {string.Join(", ", auditEvents.Select(e => $"{e.Type}({e.TenantId})"))}");
    }

    /// <summary>Assert that no audit events exist for a tenant (data should be isolated).</summary>
    public static void ShouldBeTenantIsolated(
        this IEnumerable<OutboxMessage> auditEvents,
        Guid expectedTenantId,
        IEnumerable<Guid> forbiddenTenantIds)
    {
        var events = auditEvents.ToList();
        var violatingEvents = events.Where(e => forbiddenTenantIds.Contains(e.TenantId)).ToList();

        violatingEvents.ShouldBeEmpty(
            $"Tenant isolation violation detected. Events from unexpected tenants: " +
            $"{string.Join(", ", violatingEvents.Select(e => $"{e.Type}({e.TenantId})"))}");
    }

    /// <summary>Assert that a user has a specific permission (based on roles or direct assignment).</summary>
    public static void ShouldHavePermission(
        this ICurrentUser user,
        string permissionKey)
    {
        // This is a placeholder. Actual implementation would check:
        // - User roles mapped to permissions
        // - Direct permission assignments
        // - Tenant-level permissions
    }

    /// <summary>Assert that a result is successful.</summary>
    public static void ShouldBeSuccess<T>(this Result<T> result, string? message = null)
    {
        result.IsSuccess.ShouldBeTrue(
            message ?? $"Expected success result but got failure: {result.Error}");
    }

    /// <summary>Assert that a result is a failure.</summary>
    public static void ShouldBeFailure<T>(this Result<T> result, string? message = null)
    {
        result.IsFailure.ShouldBeTrue(
            message ?? $"Expected failure result but got success: {result.Value}");
    }

    /// <summary>Assert that a result has a specific error type.</summary>
    public static void ShouldHaveErrorType<T>(this Result<T> result, string expectedErrorType)
    {
        result.IsFailure.ShouldBeTrue("Result should be a failure.");
        result.Error?.Type.ShouldBe(expectedErrorType);
    }
}

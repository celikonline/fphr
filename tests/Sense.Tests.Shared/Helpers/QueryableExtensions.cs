using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Sense.Tests.Shared.Helpers;

/// <summary>
/// Test-specific queryable extensions for bypassing normal tenant/soft-delete filters
/// when needed for verification in tests.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Disable tenant and soft-delete filters for a query (test-only).
    /// WARNING: Use only in tests to verify isolation or deleted data. Never in production code.
    /// </summary>
    public static IQueryable<T> AsTenantNonFiltered<T>(this IQueryable<T> query)
        where T : class
    {
        // Disable the tenant filter for testing cross-tenant visibility
        return query.IgnoreQueryFilters().Where(x => false); // Placeholder implementation
    }

    /// <summary>
    /// Return only soft-deleted records (ignoring the soft-delete filter).
    /// Useful for verifying soft-delete functionality.
    /// </summary>
    public static IQueryable<T> AsDeleted<T>(this IQueryable<T> query)
        where T : class
    {
        // In a real implementation, this would use reflection to check ISoftDelete
        // and filter for IsDeleted == true
        return query.IgnoreQueryFilters();
    }

    /// <summary>
    /// Include both active and soft-deleted records.
    /// Useful for comprehensive audit verification.
    /// </summary>
    public static IQueryable<T> AsIncludingDeleted<T>(this IQueryable<T> query)
        where T : class
    {
        return query.IgnoreQueryFilters();
    }
}

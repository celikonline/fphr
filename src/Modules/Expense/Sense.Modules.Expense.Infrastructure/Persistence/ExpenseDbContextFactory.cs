using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Modules.Expense.Infrastructure.Persistence;

/// <summary>Design-time context for `dotnet ef migrations add` (connection: SENSE_DATABASE env var or local default).</summary>
public sealed class ExpenseDbContextFactory : IDesignTimeDbContextFactory<ExpenseDbContext>
{
    public ExpenseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExpenseDbContext>()
            .UseNpgsql(DesignTimeDefaults.ConnectionString, npgsql => npgsql.MigrationsHistoryTable(DesignTimeDefaults.MigrationsHistoryTable, ExpenseDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ExpenseDbContext(options, new TenantContext());
    }
}

internal static class DesignTimeDefaults
{
    public const string ConnectionStringVariable = "SENSE_DATABASE";
    public const string LocalConnectionString = "Host=127.0.0.1;Port=5432;Database=senseik;Username=postgres;Password=postgres";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } cs ? cs : LocalConnectionString;
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Modules.Onboarding.Infrastructure.Persistence;

/// <summary>Design-time context for `dotnet ef migrations add` (connection: SENSE_DATABASE env var or local default).</summary>
public sealed class OnboardingDbContextFactory : IDesignTimeDbContextFactory<OnboardingDbContext>
{
    private const string ConnectionStringVariable = "SENSE_DATABASE";
    private const string LocalConnectionString = "Host=127.0.0.1;Port=5432;Database=senseik;Username=postgres;Password=postgres";
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    public OnboardingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } cs
            ? cs
            : LocalConnectionString;
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, OnboardingDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new OnboardingDbContext(options, new TenantContext());
    }
}

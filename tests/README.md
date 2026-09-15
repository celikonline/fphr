# Test Infrastructure - SenseİK

Comprehensive test infrastructure for unit and integration tests with multi-tenant PostgreSQL support, architecture rule enforcement, and comprehensive test fixtures.

## Architecture

- **TestFixture** - Base class for all integration tests with PostgreSQL testcontainers
- **TenantDbFactory** - Factory for creating isolated DbContext instances per tenant
- **SeedDataFactory** - Deterministic seed data generation (minimal and demo profiles)
- **Architecture Rules** - Enforce onion architecture, tenant isolation, and naming conventions
- **Test Helpers** - Assertion extensions, queryable extensions, and JWT token builders

## Running Tests

### All Tests
```bash
dotnet test
```

### Unit Tests Only
```bash
dotnet test --filter Category=Unit
```

### Integration Tests Only
```bash
dotnet test --filter Category=Integration
```

### Architecture Tests Only
```bash
dotnet test tests/Sense.Tests.Architecture/Sense.Tests.Architecture.csproj
```

### With Code Coverage
```bash
dotnet test --collect:"XPlat Code Coverage" /p:CollectCoverage=true
```

### Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~Sense.Tests.Architecture.OnionArchitectureTests"
```

## Test Categories

### 1. Architecture Tests (`Sense.Tests.Architecture`)

Enforce structural rules for the entire solution:

- **Onion Architecture**
  - Domain layer depends only on Kernel
  - Application layer depends on Domain and Kernel only (not Infrastructure)
  - Infrastructure and API layers have no upward dependencies
  - Modules communicate only through Contracts

- **Tenant Isolation**
  - All database queries automatically filtered by tenant (via ModuleDbContext)
  - No cross-tenant data access possible
  - TenantId immutability enforced by interceptor

- **Naming Conventions**
  - Handlers: `{Action}Handler`, e.g. `CreateUserHandler`, `ListRolesHandler` (sealed) — not `{Action}CommandHandler`/`{Action}QueryHandler`
  - Events: `*Event` or `*DomainEvent` (Domain layer only)
  - Permissions: `{module}.{resource_plural}.{action}` (snake_case)
  - Controllers: Inherit from `ApiControllerBase`

- **No Hardcoded Secrets**
  - Scans for plaintext passwords, tokens, API keys in code
  - Enforces use of appsettings.json or environment variables

Run architecture tests before deployment:
```bash
dotnet test tests/Sense.Tests.Architecture/ --configuration Release
```

### 2. Integration Tests (`Sense.Tests.Integration`)

Test features end-to-end with real database:

```csharp
[UsesVerify]
public sealed class CreateEmployeeTests : TestFixture
{
    [Fact]
    public async Task CreateEmployee_WithValidData_Succeeds()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("ACME Corp");
        var userId = await CreateUserAsync(tenantId, "manager@acme.com", 
            new HashSet<string> { "Manager" });
        SetCurrentUser(tenantId, userId, "manager@acme.com", 
            new HashSet<string> { "Manager" });

        var command = new CreateEmployeeCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john@acme.com",
            DepartmentId: Guid.NewGuid());

        // Act
        var result = await ExecuteCommandAsync(command);

        // Assert
        result.ShouldBeSuccess();
        var auditEvents = await GetAuditEventsAsync(tenantId);
        auditEvents.ShouldContainAuditEvent("EmployeeCreated", tenantId);
    }
}
```

### 3. Contract Tests (`Sense.Tests.Contract`)

Verify API contracts and data validation.

### 4. Module-Specific Tests

Each module can have its own test project:
- `Sense.Modules.Identity.Tests`
- `Sense.Modules.Organization.Tests`
- `Sense.Modules.Employee.Tests`

## Creating a New Test

### 1. Add Test Class

```csharp
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

public sealed class CreateEmployeeTests : TestFixture
{
    [Fact]
    public async Task CreateEmployee_WithValidData_Succeeds()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Tenant");
        var userId = await CreateUserAsync(tenantId, "test@example.com");
        SetCurrentUser(tenantId, userId, "test@example.com", 
            new HashSet<string> { "User" });

        var command = new CreateEmployeeCommand(/* ... */);

        // Act
        var result = await ExecuteCommandAsync(command);

        // Assert
        result.ShouldBeSuccess();
    }
}
```

### 2. Add XUnit Collection Fixture (Optional)

For tests that need shared setup:

```csharp
[Collection("PostgreSQL")]
public sealed class EmployeeRepositoryTests : TestFixture
{
    [Fact]
    public async Task GetEmployee_WithValidId_ReturnsEmployee()
    {
        // ...
    }
}
```

### 3. Mark Test Categories (Optional)

```csharp
[Trait("Category", "Integration")]
[Fact]
public async Task MyTest() { }
```

## TestFixture Usage

### Create a Tenant
```csharp
var tenantId = await CreateTenantAsync("ACME Corp", slug: "acme");
```

### Create a User
```csharp
var userId = await CreateUserAsync(tenantId, "user@acme.com", 
    new HashSet<string> { "Admin", "Manager" });
```

### Set Current User Context
```csharp
SetCurrentUser(tenantId, userId, "user@acme.com", 
    new HashSet<string> { "Admin" });
```

### Execute Command
```csharp
var result = await ExecuteCommandAsync(new CreateEmployeeCommand(/* ... */));
result.ShouldBeSuccess();
```

### Execute Query
```csharp
var result = await ExecuteQueryAsync(new GetEmployeeByIdQuery(employeeId));
result.Value.ShouldNotBeNull();
```

### Get Audit Events
```csharp
var events = await GetAuditEventsAsync(tenantId);
events.ShouldContainAuditEvent("EmployeeCreated", tenantId);
```

### Reset Database Between Tests
```csharp
[Fact]
public async Task FirstTest() 
{
    await ResetDatabaseAsync(); // Not needed - automatic between tests
    // ...
}
```

## Seed Data

### Minimal Profile (Fast)
```csharp
var seeder = new SeedDataFactory(dbContext);
var data = await seeder.CreateMinimalAsync();
// 1 tenant, 1 admin user, 1 org unit - < 100ms
```

### Demo Profile (Complete)
```csharp
var data = await seeder.CreateDemoAsync();
// 1 tenant, 50 employees, 5 units, 17 roles - < 500ms
```

## Test Helpers

### Assertions
```csharp
// Result assertions
result.ShouldBeSuccess();
result.ShouldBeFailure();
result.ShouldHaveErrorType("ValidationError");

// Audit assertions
auditEvents.ShouldContainAuditEvent("EmployeeCreated", tenantId);
auditEvents.ShouldBeTenantIsolated(tenantId, forbiddenTenants);

// Permission assertions
user.ShouldHavePermission("organization.units.read");
```

### Token Generation
```csharp
var token = TokenBuilder.CreateForUser(tenantId, userId, "user@example.com");
var adminToken = TokenBuilder.CreateAdminToken(tenantId);

var customToken = new TokenBuilder()
    .WithTenantId(tenantId)
    .WithUserId(userId)
    .WithRole("Manager")
    .WithRole("Approver")
    .Build();
```

### Queryable Extensions
```csharp
// Bypass tenant filter (test only)
var allTenantData = query.AsTenantNonFiltered();

// Get soft-deleted records
var deleted = query.AsDeleted();

// Include both active and deleted
var all = query.AsIncludingDeleted();
```

## Configuration

### appsettings.test.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=testcontainer;Database=test_db;User Id=postgres;Password=postgres"
  }
}
```

## CI/CD Integration

### GitHub Actions Example
```yaml
name: Tests

on: [push, pull_request]

jobs:
  architecture:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      - run: dotnet test tests/Sense.Tests.Architecture/ --configuration Release

  integration:
    runs-on: ubuntu-latest
    needs: architecture
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      - run: dotnet test --configuration Release --filter Category=Integration
      - uses: codecov/codecov-action@v3
```

## Troubleshooting

### Tests Hang on testcontainers
- Check Docker is running: `docker version`
- Check disk space: `docker system df`
- Clear stopped containers: `docker container prune`

### Cross-Tenant Data Visible
- Verify `ModuleDbContext.OnModelCreating` has `HasQueryFilter` for `ITenantEntity`
- Check `TenantContext.IsResolved` before operations
- Use `TestFixture.SetCurrentUser()` to set proper context

### Audit Events Not Found
- Verify OutboxProcessor is running (in tests, may need to trigger manually)
- Check `GetAuditEventsAsync()` includes correct module schema
- Verify domain events are raised and cleared properly

### Architecture Test Failures
- Use `netarchtest.rules` package >= 1.0
- Run with `--verbosity diagnostic` for detailed output
- Review failing types and fix code to conform to rules

## Performance Tips

1. **Use Minimal Seed Profile for Unit Tests**
   - Smaller dataset = faster tests
   - Only use Demo profile for feature-specific integration tests

2. **Parallel Test Execution**
   - Tests are collection-scoped (`[Collection("PostgreSQL")]`)
   - Tests in the same collection run sequentially
   - Different collections run in parallel

3. **Respawn for Database Cleanup**
   - Faster than dropping/recreating schema
   - Respawner resets to clean state between tests
   - Single container per test class

4. **Architecture Tests in CI First**
   - Run architecture tests before integration tests
   - Fail fast on architectural violations
   - Prevents invalid tests from running

## Best Practices

1. **One Scenario Per Test**
   - Clear test name: `{Operation}_{Condition}_{Expected}`
   - Single arrange/act/assert flow
   - Example: `CreateEmployee_WithDuplicateEmail_ReturnsError`

2. **Tenant Isolation Validation**
   - Always verify audit events have correct tenantId
   - Test cross-tenant isolation with at least 2 tenants
   - Use `ShouldBeTenantIsolated()` helper

3. **User Context Setup**
   - Use `SetCurrentUser()` to simulate authenticated requests
   - Use `SetSystemUser()` for background jobs
   - Always set proper roles for authorization tests

4. **No Hard-Coded IDs**
   - Use `CreateTenantAsync()` and `CreateUserAsync()`
   - Tests must be repeatable and isolated

5. **Cleanup After Tests**
   - Database resets automatically (via Respawner)
   - ServiceProvider disposes automatically (via IAsyncLifetime)
   - No manual cleanup needed in most cases

## See Also

- [ADR 0002 - Onion Architecture](../docs/adr/0002-onion-architecture.md)
- [ADR 0003 - Per-Module DbContext](../docs/adr/0003-per-module-dbcontext.md)
- [Architecture overview](../docs/architecture.md)
- [Coding and repo conventions](../docs/conventions.md)

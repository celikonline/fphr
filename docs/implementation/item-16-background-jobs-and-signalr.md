# Item 16: Background Jobs, Redis Backplane, and SignalR Hub

## Overview

Item 16 implements background job processing via Hangfire and real-time notifications via SignalR. The solution includes:

- **Hangfire** for scheduled/background job management
- **SignalR** hub for real-time notifications
- **Redis backplane** for scaling SignalR across multiple servers
- **Tenant-scoped isolation** for all operations
- **Integration with audit outbox** for event tracking

## Architecture

### Components

#### 1. **NotificationsHub** (`Sense.Api/Hubs/NotificationsHub.cs`)
- SignalR hub for real-time communication
- Auto-joins clients to tenant-specific groups on connection
- Extracts `tenantId` from JWT claims for isolation
- Connection lifecycle: `OnConnectedAsync`, `OnDisconnectedAsync`

#### 2. **Background Job Service** (`Sense.Shared.Infrastructure/BackgroundJobs/`)
- **IBackgroundJobService** interface for job management
- **BackgroundJobService** implementation using Hangfire
- **IBackgroundJobExecutor** for job execution logic
- **BackgroundJobExecutor** implementation with tenant context setup

#### 3. **Notification Service** (`Sense.Shared.Infrastructure/Notifications/`)
- **INotificationService** interface for broadcasting
- **SignalRNotificationService** implementation
- Supports three notification types:
  - `ReceiveNotification` - General notifications
  - `ReceiveJobProgress` - Job progress updates
  - `ReceiveAuditAlert` - Security/audit events

#### 4. **Redis Configuration**
- Configured in `appsettings.json` under `ConnectionStrings:Redis`
- Used as backplane for SignalR (multi-server scaling)
- Connection string: `127.0.0.1:6379,abortConnect=false`

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Database": "Host=127.0.0.1;Port=5432;Database=senseik;Username=postgres;Password=postgres",
    "Redis": "127.0.0.1:6379,abortConnect=false"
  }
}
```

### Program.cs Setup

```csharp
// In Program.cs
builder.Services.AddHangfireServices(builder.Configuration);

// Map SignalR hub
app.MapHub<NotificationsHub>("/hubs/notifications");

// Map Hangfire dashboard
app.UseHangfireDashboard();
```

## Usage

### Enqueueing a Job

```csharp
var backgroundJobService = serviceProvider.GetRequiredService<IBackgroundJobService>();
var tenantId = Guid.Parse("...");
var jobId = await backgroundJobService.EnqueueAsync(
    JobType.SendEmail,
    tenantId,
    "{\"email\":\"user@example.com\",\"templateId\":\"welcome\"}"
);
```

### Scheduling a Delayed Job

```csharp
var delay = TimeSpan.FromMinutes(5);
var jobId = await backgroundJobService.ScheduleAsync(
    JobType.ExportReport,
    tenantId,
    "{\"reportId\":\"report-123\"}",
    delay
);
```

### Creating a Recurring Job

```csharp
await backgroundJobService.RecurringAsync(
    jobName: "DailyAuditSummary",
    tenantId: tenantId,
    cronExpression: "0 1 * * *", // 1:00 AM daily
    payload: "{\"includeFailures\":true}"
);
```

### Sending a Notification

```csharp
var notificationService = serviceProvider.GetRequiredService<INotificationService>();
var notification = NotificationDto.Create(
    title: "Deployment Complete",
    message: "Your application has been deployed successfully",
    type: NotificationType.Success,
    urgency: NotificationUrgency.Low
);
await notificationService.SendToTenantAsync(tenantId, notification);
```

## Job Types

Defined in `JobType` enum:

| Type | Description |
|------|-------------|
| `SendEmail` | Email notification job |
| `ExportReport` | Report generation and export |
| `SyncAttendance` | Attendance data synchronization |
| `ApprovalReminder` | Approval workflow reminders |
| `PayrollRun` | Payroll processing |
| `DailyAuditSummary` | Daily audit log summary |

## Tenant Isolation

All operations are tenant-scoped through:

1. **JWT Claims**: `tenantId` extracted from JWT during HTTP requests
2. **TenantContext**: Set explicitly in job handlers via `ITenantContextSetter.BeginScope()`
3. **Group Isolation**: SignalR groups named `tenant-{tenantId:N}` ensure broadcasts only reach clients from that tenant
4. **Database Queries**: Audit interceptor ensures only tenant-scoped data is queried

Example of tenant isolation in job execution:

```csharp
using (_tenantContextSetter.BeginScope(tenantId))
{
    // All code here runs in tenant context
    await _notificationService.SendToTenantAsync(tenantId, notification);
}
```

## Hangfire Dashboard

- **URL**: `http://localhost:5000/hangfire`
- **Features**:
  - Job history and status
  - Recurring job management
  - Job details and logs
  - Failed job recovery
- **Security**: Currently accessible without authentication (admin role protection should be added)

## Redis Backplane

SignalR uses Redis for horizontal scaling:

```csharp
services.AddSignalR()
    .AddStackExchangeRedis(redisConnection, options =>
    {
        options.Configuration.ChannelPrefix = "senseik-signalr-";
    });
```

Benefits:
- Multiple API instances share notifications
- No client reconnection needed when scale changes
- Automatic message routing to correct tenant groups

## Event Flow

```
User Action
    ↓
Command/Query Handler
    ↓
Business Logic
    ↓
Event (Domain/Integration)
    ↓
Outbox (persisted)
    ↓
OutboxProcessor (background)
    ↓
Job Enqueue (optional)
    ↓
Hangfire Worker
    ↓
Job Execution with TenantContext
    ↓
SignalR Notification broadcast to tenant group
    ↓
Client WebSocket receives message
```

## Testing

### Unit Tests
- `BackgroundJobServiceTests`: Hangfire job operations
- `NotificationsHubTests`: Hub connection and group management

### Running Tests
```bash
dotnet test tests/Sense.Tests.Integration/BackgroundJobServiceTests.cs
dotnet test tests/Sense.Tests.Integration/NotificationsHubTests.cs
```

### Test Infrastructure
- Uses in-memory Hangfire storage for isolation
- Mock notification service for hub testing
- Mock SignalR group manager for connection tests

## Best Practices

1. **Always include tenantId in job payload**
   - Enables proper isolation and audit logging

2. **Use correlation IDs for tracing**
   - Link jobs to originating requests
   - Useful for debugging

3. **Handle job failures gracefully**
   - Hangfire retries failed jobs (configurable)
   - Log failures for monitoring

4. **Avoid long-running jobs in request handlers**
   - Use `BackgroundJobService.EnqueueAsync()` to defer work
   - Keep response times low

5. **Test job handlers with tenant context**
   - Ensure tenant isolation is enforced
   - Use `TenantContextSetter.BeginScope()` in tests

## Configuration Options

### Hangfire Polling
- **Interval**: 15 seconds (configurable in `BackgroundJobServiceCollectionExtensions`)
- **Worker Count**: 2 (adjust based on server capacity)
- **Timeout**: 30 seconds for job completion

### Redis Backplane
- **Channel Prefix**: `senseik-signalr-` (prevents collision with other apps)
- **Connection Pool**: Managed by StackExchange.Redis
- **Eviction**: Set `maxmemory-policy allkeys-lru` on Redis server

## Security Considerations

1. **Authentication**
   - SignalR hub validates JWT token on connection
   - Hangfire dashboard should be protected with role-based authorization

2. **Tenant Isolation**
   - Jobs include tenantId in payload
   - All database queries filtered by tenant
   - Notifications sent only to tenant group

3. **Audit Logging**
   - All job executions logged with tenant/user context
   - Failed jobs tracked for investigation
   - SignalR connections include IP address in logs

## Monitoring

Key metrics to monitor:

1. **Job Metrics**
   - Total enqueued jobs
   - Succeeded vs failed jobs
   - Average job duration
   - Retry rate

2. **SignalR Metrics**
   - Connected clients per tenant
   - Message throughput
   - Connection churn rate
   - Broadcast latency

3. **Redis Metrics**
   - Connection pool usage
   - Memory usage
   - Key evictions
   - Command latency

## Troubleshooting

### Jobs Not Executing
1. Check Hangfire dashboard for queued jobs
2. Verify PostgreSQL connection string
3. Check worker logs for errors
4. Ensure `AddHangfireServer()` is called

### SignalR Connection Issues
1. Check WebSocket proxy configuration
2. Verify JWT token includes tenantId claim
3. Check Redis connection if using backplane
4. Monitor browser console for JavaScript errors

### Tenant Isolation Breaches
1. Verify group names follow pattern: `tenant-{tenantId:N}`
2. Check that all jobs set tenant context before database access
3. Audit outbox entries for cross-tenant data

## Future Enhancements

1. **Admin UI** (Item 19/20)
   - Notification preferences
   - Job history dashboard
   - Alert thresholds

2. **Advanced Scheduling**
   - Bulk job operations
   - Dynamic job routing
   - Priority queues

3. **Monitoring**
   - Prometheus metrics export
   - OpenTelemetry integration
   - Custom health checks

## Related Items

- **Item 15**: Database schema for tenant support
- **Item 17**: Comprehensive testing framework
- **Item 19**: Web UI for notifications
- **Item 23**: Logging enrichment with correlation IDs

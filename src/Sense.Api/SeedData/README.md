# Seed Data

This directory contains deterministic seed data generation for development and testing environments.

## Profiles

### Minimal Profile
- **Tenant**: "Pilot Company" (TenantId: `a1b2c3d4-e5f6-4a5b-9c8d-7e6f5a4b3c2d`)
- **User**: 1 admin user (admin@senseik.dev)
- **Roles**: the four user groups; the admin is İK Yöneticisi (+ locked Owner flag)
- **Purpose**: Quick setup for basic functionality testing
- **Generation Time**: < 100ms

### Demo Profile
- **Tenant**: "Demo HR Company" (TenantId: `b2c3d4e5-f6a7-5b6c-ad9e-8f7a6b5c4d3e`)
- **Users**: 6 fixed, memorable login accounts (`owner@demo.local`, `ik@demo.local`,
  `bordro@demo.local`, `yonetici@demo.local`, `calisan@demo.local`, `denetci@demo.local` —
  see root `README.md`) + 50 randomly generated employees with realistic Turkish names
- **Roles**: the four user groups — İK Yöneticisi (`HR`), Muhasebe (`PayrollSpecialist`), Çalışan
  (`Employee`) + the locked Owner flag carried by `owner@demo.local` (who is also İK Yöneticisi). Old role
  names (Manager, Intern, Auditor…) remain demo personas for org placement only and map to a group
  (`SystemRoleCodes.RetiredSuccessors`): e.g. `yonetici@` is a department manager but in the Çalışan
  group, `denetci@` is İK Yöneticisi
- **Organization**: Genel Merkez > 4 departments (Satış, Yazılım Geliştirme, İnsan Kaynakları,
  Finans) > ~21 Turkish job titles (Ünvan level); every employee sits on a title, so
  `Employee.OrgUnitId` is the deepest assignment like a real placement (department headcounts are
  unchanged — the department is the title's parent)
- **Access settings**: every home widget on, including `ShowAllLeavesInCalendar`, so a plain
  employee sees colleagues on the leave calendar / "Yaklaşan izinler" widget
- **Leave**: the default "Türk İş Kanunu" rule set, a leave profile with generated
  Accrual/CarryOver ledger rows for every employee, 6 requests for each fixed login account
  (approved annual in the previous and the current period, approved sick leave, approved future
  annual, rejected annual, pending excuse) and upcoming annual leave for 8 more employees, one of
  them starting today. Dates are relative to "today", so the data stays fresh
- **Documents**: 3-5 sample documents per tenant
- **Purpose**: Full-featured UI and workflow testing
- **Generation Time**: < 3s

## Configuration

Enable seeding in `appsettings.json` or `appsettings.Development.json`:

```json
{
  "Seeding": {
    "Enabled": true,
    "Profile": "Demo"
  }
}
```

**Default settings:**
- Production: Disabled
- Development: Enabled with Demo profile

## How Seeding Works

1. **On Startup**: If `Seeding.Enabled` is true, the application will check for existing seed data
2. **Idempotency**: If seed data is already present (detected by checking for "Pilot Company" or "Demo HR Company" tenants), seeding is skipped
3. **Deterministic**: Uses `Random(seed: 42)` for reproducible data generation

## Data Reset

To reset seed data and re-seed:

1. `identity.tenants` alone isn't enough: `identity`, `org`, `employee` and `documents` are
   separate schemas/DbContexts (ADR 0003) with no cross-schema FK enforcement, so deleting
   only the tenant row leaves the other schemas' rows orphaned instead of cascading. Truncate
   every seeded table across all four schemas instead:
   ```sql
   TRUNCATE TABLE
     identity.impersonation_grants, identity.inbox_messages, identity.invitations,
     identity.outbox_messages, identity.refresh_tokens, identity.role_permissions,
     identity.roles, identity.tenants, identity.unit_authorizations,
     identity.user_permission_overrides, identity.user_roles, identity.users,
     org.companies, org.holiday_calendar_entries, org.holiday_calendars, org.inbox_messages,
     org.locations, org.org_unit_approval_policy_assignments, org.org_unit_types,
     org.org_units, org.outbox_messages,
     employee.employees, employee.inbox_messages, employee.outbox_messages,
     documents.documents, documents.inbox_messages, documents.outbox_messages,
     leave.ledger_entries, leave.leave_requests, leave.employee_leave_profiles,
     leave.leave_types, leave.rule_sets, leave.inbox_messages, leave.outbox_messages
   CASCADE;
   ```

   `leave.legislation_parameters` is platform data (migrations, not the seeder) — leave it alone.

2. Restart the application with seeding enabled

## Privacy & Security Notes

All seed data is **fictional**:
- Employee names are randomly generated from Turkish name lists
- Email addresses follow the pattern: `firstname.lastname@democompany.com` (except the 6
  fixed `@demo.local` accounts above)
- All seeded users (minimal admin + every demo account) share one password,
  `SeedDataConstants.SeedUserPassword` (`"Demo123!"`), hashed for real via the app's own
  `IPasswordHasher` — dev/test-only, never used in production seeding
- No real personal data is used

## Deterministic Output

Each seed profile produces identical data across runs:
- Turkish first names: 58 names
- Turkish last names: 59 names  
- Email pattern: Always `firstname.lastname@democompany.com`
- Employment dates: Calculated relative to "today"
- Role assignments: Deterministic based on `Random(42)`

## Implementation Structure

- `SeedProfile.cs`: Enum defining available profiles
- `SeedingOptions.cs`: Configuration options (Enabled, Profile)
- `DataSeeder.cs`: Main orchestrator for seeding
- `SeedDataConstants.cs`: Hard-coded constants and name lists
- `SeedingServiceCollectionExtensions.cs`: DI registration

## Future Extensions

For phases 2+:
- Add Employee module entities (employment history, qualifications)
- Add Organization module entities (org units, hierarchies)
- Add Leave balances and allocations
- Add Department-specific role assignments
- Add Document templates and samples

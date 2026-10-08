# Ad-hoc Report Builder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and superpowers:test-driven-development. Execute tasks in order; tests must fail before implementation.

**Goal:** Add a permission- and scope-aware one-time custom Excel report builder to Reporting, using typed field descriptors and module-owned data contributions.

**Architecture:** Reporting.Contracts owns the stable selection/data contract. Employee and Leave expose their own reportable fields and rows through contributors registered at the API composition roots. Reporting validates selections, composes bounded typed rows, and runs through the existing async execution pipeline. The web app adds a compact builder step to the current Reports page and uses existing polling/history/download behavior.

**Implemented milestone:** The first slice contains employee identity/contact/status/hire date and current unit, leave requests, current annual leave balance, and overtime fields. It supports employee-grain fields plus at most one detail group per run. Custom fields, dynamic career levels, Expense/Payroll payment fields, and overtime-payment joins remain follow-up work.

**Tech stack:** .NET 10 modular monolith, CQRS/MediatR, EF Core module contexts, existing XLSX exporter, React/Vite, Mantine 9, Vitest/Testing Library.

## Global constraints

- Cross-module dependencies go only through `*.Contracts`; contributors query only their own module.
- Treat every client value as untrusted. Stable field keys and server-owned descriptors determine labels, permissions, and row grain.
- Apply requester permissions and `IDataScopeService` at request and generation time; unit selections only narrow effective visibility.
- Preserve the current report execution, email, polling, history, and download flows.
- Keep the builder hidden from unit managers and self-service roles; never add `reports.custom.build` to `UnitManagerGrants`.
- Use existing Mantine patterns, localization, keyboard behavior, and light/dark themes. No new UI library.
- Never stage the existing untracked `?? %SystemDrive%/` artifact.

## Review focus

1. No permission, tenant, field visibility, or employee-scope bypass in asynchronous execution.
2. Detail-group composition cannot form a Cartesian product; the current milestone permits one detail group per run.
3. Each contributor reads only its owning module and projects only selected fields.
4. The UI submits stable keys and cannot substitute labels, permissions, or grain.
5. Errors remain safe and localized and do not expose report row contents.

## Tasks

### Task 1: Define and test the shared custom-report contract

**Files:** `src/Modules/Reporting/Sense.Modules.Reporting.Contracts/**`; corresponding Reporting.Contracts tests.

- Add field/group descriptors, value type, row grain, typed request/row DTOs, contribution interface, and stable validation error codes.
- Add deterministic validation for duplicate/unknown fields, required permissions, selected grains, date ranges, and maximum rows.
- Add catalog filtering tests for permission-sensitive fields and stable ordering/localization metadata.
- Add `reports.custom.build` as an explicit permission and ensure it is not granted to unit managers.
- Run the focused contracts tests and verify they fail before implementation.

### Task 2: Add module-owned field and row contributions

**Files:** Employee, Organization, Leave, and Expense Contracts/Infrastructure projects, API composition roots, their tests, and narrowly required project references.

- Implement Employee and Leave contributors for supported employee details, current unit, leave requests, current balance, and overtime data.
- Enforce tenant boundaries, date/unit filters, and projection inside each owning contributor.
- Defer dynamic organization levels, employee custom-field definitions, and Expense/Payroll payment contributions.
- Cover field projection and scope/date filters in module tests; payment/overtime link tests remain follow-up work.

### Task 3: Build the Reporting catalog, validator, and bounded composer

**Files:** Reporting.Application, Reporting.Infrastructure, Reporting.Api, and tests.

- Add a permission-filtered custom field catalog query and a custom async report provider.
- Validate current selected field keys/permissions and current employee scope in the background job; intersect selected group scopes and narrow by requested org units.
- Compose employee rows, at most one detail grain, and employee-grain leave balance without Cartesian expansion.
- Enforce the 12-calendar-month detail range and 50,000-row cap before XLSX generation.
- Verify queued request, authorization revocation, stale/unknown selections, composition edge cases, and safe failure state.

### Task 4: Add the custom builder to Reports

**Files:** `web/src/services/reports.service.ts`, Reports page/components, locales, and focused tests.

- Fetch and render only the server catalog available to the caller.
- Let the user choose fields, optional allowed units, and a bounded defaulted range for detail groups.
- Disable invalid selection combinations and show localized, actionable validation/empty/error states.
- Submit stable selected keys to the async provider, then reuse existing queue polling, execution history, and download behavior.
- Keep the interface keyboard accessible and responsive within the established Reports visual language.

### Task 5: Update roadmap evidence and verify

**Files:** Relevant `docs/analysis/kolayik-video` roadmap/status documents, `design-log.md`.

- Update the report-builder roadmap status only after code evidence exists.
- Record a design-log entry if the project design log is present in the repository.
- Run focused backend and web tests during development, then the complete unfiltered repository gate and required independent review gate.
- Resolve every newly introduced failure; report any pre-existing or environment-blocked check with command and evidence.

## Self-review

- Source analysis calls for a field-catalog based builder, async Excel generation, date-range validation, access filtering, and a constrained overtime/payment join; each appears above.
- The plan keeps schema ownership inside modules and routes module contracts through Reporting.Contracts.
- Permission and row-scope checks are explicit at both catalog/request and background execution boundaries.
- The web work is scoped to the existing Reports experience and follows the active Mantine migration.
- No migration should be necessary unless discovery shows an owned module lacks an existing typed data source; if so, prefer current readers/projections and justify any schema change in the task report.

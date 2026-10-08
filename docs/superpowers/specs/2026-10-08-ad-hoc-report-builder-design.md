# Ad-hoc Report Builder Design

**Date:** 2026-10-08  
**Branch:** `takim/ad-hoc-report-builder`  
**Base:** `966c476e8238fb3f833d4a308fde1e419058c295` (`dev` == `main`)  
**Source analysis:** [11.3.12 Reporting and Analytics](../../analysis/kolayik-video/11.3.12-raporlama-analitik.md), especially “Kendin oluştur”, field catalog, row grain, unit scope, and field visibility.

## Goal

Let authorized HR users select report fields from employee, organization, leave, and payment modules and request a one-time Excel report through the existing asynchronous execution, email, and history flow.

## Users and intended scope

- Owner, Admin, HRAdmin, and HRSpecialist users with `reports.custom.build` can use the builder.
- Unit managers can run permitted prebuilt reports but never receive `reports.custom.build`; employees do not see the builder.
- Target field groups are employee general/personal fields, current organization/career fields, reportable custom fields, leave requests, leave balance, overtime, and approved period payments.
- V1 deliberately excludes saved/shared/scheduled definitions, preview, PDF, salary fields, assets, and training fields.
- A run is a one-time `ReportExecution` with a serialized, validated field selection. Result retention, queued/running state, email link, and download use the existing reporting pipeline.

## User flow

1. The user opens **Reports → Build your own**.
2. The page loads only field groups and fields allowed by the caller's current permissions.
3. The user optionally narrows to allowed organization units, selects fields, and chooses a date range when a detail group needs one. A detail range defaults to the last 12 calendar months and may not exceed 12 months.
4. The user starts the report. The UI shows queued/running state, then the same execution history/download controls as prepared reports. The existing ready-email links to that execution.
5. Empty catalog, no selected fields, incompatible detail grains, inaccessible fields/units, query failure, and row-limit errors are explicit and localized in Turkish, English, and German.

No preview or report definition is saved.

## V1 field catalog

| Group key | Owner module | Grain | Fields and constraints |
| --- | --- | --- | --- |
| `employee.general` | Employee | Employee | Identity and employment fields; sensitive identity fields require `employee.sensitive.view`. |
| `employee.career` | Organization | Employee | Tenant-defined organization levels resolved as of the report end date. |
| `employee.customFields` | Employee | Employee | Only definitions marked `IsReportable`; fields with sensitive metadata require their declared permission and tenant access setting. |
| `leave.requests` | Leave | Leave request | Requested/approved dates, type, days, status, and signatory where available. |
| `leave.balance` | Leave | Employee | Current annual-leave balance projection. |
| `leave.overtime` | Leave | Overtime request | Date, duration, decision, and conversion state. |
| `expense.payments` | Expense | Payment | Approved advance/expense and overtime payment data within the selected period; payment columns require `payment.record.read`. |

Every field has a stable key, localized label key, export type, sort order, and optional required permission. Dynamic fields are tenant-resolved by their owning module and never accepted from client-supplied labels or SQL.

## Row grain and joins

- With no detail group, output has one row per visible employee.
- A report may select employee-grain fields plus at most one detail-grain group (`leave.requests`, `leave.overtime`, or `expense.payments`). Detail rows repeat selected employee fields.
- The sole two-detail exception is `leave.overtime` + `expense.payments`. Payments join to overtime through the existing overtime reference; unmatched overtime rows remain, and payments unrelated to overtime are excluded from this paired selection.
- `leave.balance` is employee-grain and may accompany any other selection.
- Two incompatible detail grains fail before an execution is queued with a stable validation code.
- Detail reports require `from` and `to`. The maximum span is 12 calendar months; invalid, reversed, or oversized ranges fail before the job is created.

## Architecture and data flow

1. `Reporting.Contracts` defines field/group descriptors, grain, data request/row contracts, and `IReportFieldCatalogContributor`.
2. Employee, Organization, Leave, and Expense implement contributors in their own Infrastructure projects and register them from their existing API composition roots. Each contributor reads only its own schema and returns typed rows keyed by employee and, for detail rows, a stable detail key and optional overtime link.
3. Reporting exposes a permission-filtered catalog query and an asynchronous `report.custom` provider. The provider validates the selection, builds the row plan, requests data from only the owning contributors, composes employee/detail rows without Cartesian joins, and writes one XLSX table.
4. The existing `RequestReportHandler` persists the execution and queues the job. The background provider rechecks the builder permission, each selected field permission, and each selected permission's current data scope. Effective employee scope is the intersection across selected field groups; the chosen unit filter can only narrow that set.
5. The provider rejects unknown, duplicate, stale, hidden, or unauthorized field keys. It ignores client-supplied column titles and group grain. `NationalId`/sensitive fields are omitted from the catalog and result when permission is absent. The output cap is 50,000 rows; exceeding it fails the execution with a localized safe error.
6. React adds a builder mode to the existing reports experience and calls the catalog and report APIs. No UI-only visibility decision is trusted by the server.

Cross-module access is through Reporting.Contracts only. No contributor reads another module's DbContext, and no report selection can broaden the caller's `IDataScopeService` result.

## Errors and privacy

- Stable errors cover missing fields, unknown/unauthorized fields, incompatible grains, invalid date range, invalid organization units, and row-limit overflow.
- Logs contain execution ID, contributor/group key, and safe error code; they never contain field values, employee names, national IDs, or selected report rows.
- A background job uses the requester identity and current permissions. Permission revocation between request and execution fails closed.
- There is no masking mode in the custom builder: a field is either authorized and included or absent.

## Verification

- Contract/catalog unit tests cover stable keys, localization metadata, dynamic catalog contributions, and permission filtering.
- Contributor tests cover own-schema reads, field projection, unit/date filtering, tenant isolation, and dynamic custom/career fields.
- Composition tests cover employee-only rows, one detail grain, overtime-payment linking, no Cartesian expansion, and 50,000-row overflow.
- Reporting job integration tests cover stale selection, requester permission revocation, tenant isolation, and queued failure behavior.
- Web tests cover catalog loading, field selection, grain/date validation, denied/empty/error states, queue polling, and ready/download behavior.
- Run the repository's unfiltered test gate and independent reviewer gate before proposing a merge.

## Risks and decisions

- This first slice composes bounded typed result sets in memory. Each contributor must query only authorized employee IDs and required columns; row count is capped before XLSX generation.
- A joined overtime/payment report uses `PayrollAdjustment.Reference=OvertimeRequestId`, matching the source analysis. Tests pin the join and unmatched-row behavior.
- Current source comments describe existing async report execution as sync; update those stale comments while touching the affected flow.
- `UnitManagerGrants` already grants catalog, execution, and own-history permissions, while excluding custom build. The V1 role rule is preserved.

## Implemented milestone scope

The first working slice currently exposes employee identity/contact/status fields, a current organization-unit label, leave requests, current annual leave balance, and overtime. It uses the shared async Excel execution, email, and history pipeline.

- Reportable employee custom fields are deferred until Employee exposes a stable report projection for definition/value pairs and sensitivity metadata.
- Dynamic organization hierarchy levels are deferred; this slice exports the employee's current organization-unit name.
- Approved payments and the overtime-payment paired detail grain are deferred until a module-owned report contract exposes payment period, employee, approval state, and overtime reference together. The implementation rejects incompatible detail groups and does not expose payment fields.
- Only one detail group may be selected in this milestone (`leave.requests` or `leave.overtime`).
- The 50,000-row limit is enforced before workbook creation; contributors still need query-level limits if tenant sizes grow beyond current bounded usage.

# senseik-065 — configurable leave policies

## Selected behavior

- "Fiilen kullanılan iş günü" means the requested/approved leave dates that are working days under the employee's assigned work schedule and effective public-holiday calendar. This does not reconcile attendance punches; the leave module has no punch-to-leave usage workflow. Existing `CountsNonWorkingDays` and the schedule's `IncludeSaturdayWhenFullWeek` stay the two explicit controls, so no duplicate global setting is added.
- Birthday leave remains a one-day per-request leave type and can be enabled through the existing type status. The new `UseOnlyOnBirthday` policy defaults on for the seed type. Missing birth date blocks the request. Employees born on 29 February may use 28 February by default, with 1 March selectable. A birthday falling on a rest/public-holiday date follows the type's existing `CountsNonWorkingDays` setting (off by default).
- Hourly-to-half-day conversion is configured per leave type with an optional threshold and converted day fraction. The seeded hourly types start at 180 minutes and 0.5 day. Exactly 180 minutes remains the regular hourly fraction; any longer span uses the configured half-day value. A missing threshold disables conversion.
- Saved requests keep their stored `CalculatedDays` when policy settings change. A new request or a user-edited pending request is calculated under the then-current rules; approval consumes that stored value.

## Implementation plan

1. Add and test pure domain policies: birthday date resolution (including leap day) and strict hourly threshold handling.
2. Add the fields to `LeaveTypeSettings`/`LeaveType`, validate combinations and bounds, map the primitive fields, update seed defaults/synchronization, then generate a real Leave migration.
3. Apply the birthday rule in the shared submit/edit/on-behalf validation path; pass identical hourly policy inputs to validation and draft preview.
4. Add the settings to the existing leave type editor with tr/en/de copy and accessible controls; retain existing work schedule, public holiday and `CountsNonWorkingDays` controls.
5. Add domain/application/web boundary tests, run the scoped test gates, inspect the diff, and update this card to review.

## Validation

- Leave domain/request tests: `ConfigurableLeavePolicyTests` and `LeaveGenderEligibilityTests` (including birthday lookup, leap-year selection, scoped employee date lookup, and the strict hourly threshold): 33 passed, 0 failed.
- Web: `pnpm exec tsc --noEmit`, scoped ESLint, the leave type dialog Vitest suite (14 passing), and `pnpm exec vite build`: passing.
- EF: generated `20261007101514_ConfigurableLeavePolicies`; `dotnet-ef migrations has-pending-model-changes` reports no pending model changes. The migration initializes legacy system-seed Birthday/JobSearch/Breastfeeding defaults.
- `git diff --check`: clean after final documentation updates.
- The complete Leave integration suite was not run because the shared `SENSE_TEST_POSTGRES` connection is not set in this session. The targeted tests require no database.

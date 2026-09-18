# Visa Document Request Feature — Handoff Document

**Card**: senseik-001-vize-belgesi  
**Status**: Domain layer implemented; tests written (red ✓)  
**Last Updated**: 2026-09-18  
**Owner**: PC-A

## Feature Summary

Employees can request visa document processing through a dedicated workflow, with approval required from a designated tenant admin. Once approved, a template set of visa-related documents is delivered to the employee's folder for upload and processing.

## Domain Layer — COMPLETED ✓

### Entities Implemented

#### VisaDocumentRequest (aggregate root)
- **Status lifecycle**: Pending → AwaitingApproval → Approved/Rejected → Completed
- **Key properties**:
  - `EmployeeId`: Employee initiating the request
  - `VisaDocumentProcessId`: Optional reference to a predefined process
  - `TravelStart`, `TravelEnd`: Travel date range
  - `CountryCode`, `VisaType`: Destination and visa type
  - `Description`: Free-form request details
  - `ApprovalRequestId`: Workflow approval reference (null until submitted)
  - `RejectionReason`, `RejectedBy`, `RejectedAt`: Rejection tracking
  - `ApprovedAt`, `CompletedAt`: Lifecycle timestamps
- **Behaviors**:
  - `Create()`: Factory with travel date validation
  - `SubmitForApproval(approvalRequestId)`: Transitions to AwaitingApproval
  - `Approve()`: Transitions to Approved, sets `ApprovedAt`
  - `Reject(reason, rejectedBy)`: Transitions to Rejected with reason
  - `Complete()`: Transitions to Completed after approval

#### VisaDocumentProcess (aggregate root)
- **Purpose**: Predefined visa processes (e.g., "Schengen Business", "Turkish Work Permit")
- **Key properties**:
  - `Name`: Process name
  - `CountryCode`: Optional country association
  - `IsActive`: Whether available for new requests
  - `CreatedAt`, `ModifiedAt`: Audit timestamps
- **Behaviors**:
  - `Create()`: Factory method
  - `Deactivate()`, `Reactivate()`: Toggle active status
  - `Update(name, countryCode)`: Update process details

### Tests Written (Red — Ready for Green Phase)

**File**: `tests/Modules/Sense.Modules.Documents.Tests/VisaDocumentRequestTests.cs`  
**Coverage**:
- ✓ `Create_WithValidData_InitializesRequestInPendingStatus` — Factory validates and creates request
- ✓ `Create_WithInvalidTravelDates_Fails` — End date before start date rejected
- ✓ `SubmitForApproval_TransitionsToAwaitingApprovalAndSetsApprovalRequestId` — Status and approval ID
- ✓ `Approve_TransitionsToApproved` — Approval transition
- ✓ `Reject_WithReason_TransitionsToRejected` — Rejection with reason tracking

**Test Framework**: xUnit + Shouldly  
**Status**: 5 tests written; ready for `dotnet test` to turn green

### Domain Errors

**File**: `src/Modules/Documents/Sense.Modules.Documents.Domain/VisaDocuments/VisaDomainErrors.cs`

- `visa_document_process.not_found`
- `visa_document_process.name_required`
- `visa_document_process.name_too_long`
- `visa_document_request.invalid_travel_dates`
- `visa_document_request.country_code_required`
- `visa_document_request.visa_type_required`
- `visa_document_request.approver_not_configured`

## Next Steps — Application Layer

### GATE 1 Assumptions (Pending User Confirmation)
1. **VisaType** is seed-defined lookup (Turistik, İş/Schengen, Çalışma, Öğrenci, Transit)
2. **Employee manually selects process**; no auto country→process mapping in v1
3. **Delivered visa documents stay visible** even if `Access.Employee.FilesVisible=false`
4. **Missing approver**: block request creation with clear error (not silent fail)

### Application Layer Tasks (After GATE 1 Approval)

**Goal**: Build `IVisaDocumentRequestService` with approval integration

1. **Handler**: `SubmitVisaDocumentRequestHandler`
   - Input: `SubmitVisaDocumentRequestCommand {EmployeeId, ProcessId, TravelStart, ...}`
   - Validation: Tenant settings for `TenantSettings.Documents.Visa.ApproverUserId`
   - Action: Create request → Call `IWorkflowService.StartAsync("VisaDocument", requestId, approverUserId, ...)`
   - Emit: Event for approval inbox update

2. **Handler**: `ApprovalCompletedEventHandler` (workflow approval completion)
   - Trigger: Workflow approval completes for `RequestType == "VisaDocument"`
   - On **Approved**: `IVisaDocumentRequestService.ApproveAsync(requestId)` + Deliver template documents
   - On **Rejected**: `IVisaDocumentRequestService.RejectAsync(requestId, reason)`

3. **Document Delivery** (on approval)
   - Query: `TenantSettings.Documents.Visa.TemplateDocumentIds[]` (list of template document IDs)
   - Action: For each template, copy to employee folder as new `Document { SourceRef = VisaDocumentRequest }`
   - Metadata: Tag with source reference for audit trail

### API Layer Tasks

**Endpoints** (PC-A responsibility):
- `POST /api/v1/visa-requests` — Create new request (JSON body with travel dates, visa type, description)
- `GET /api/v1/visa-requests/{id}` — Fetch request status
- `GET /api/v1/visa-requests?employeeId={id}&status={status}` — List filtered requests
- `GET /api/v1/visa-processes?isActive=true` — Fetch available processes for employee selection

### Web Layer Tasks

**Pages** (PC-A responsibility):
1. **Visa Request Creation Dialog** (new)
   - Form fields: Travel dates, country, visa type, process selection, description
   - Validation: Travel end ≥ start, country not empty
   - Error handling: Show "Approver not configured" if no ApproverUserId set
   - Success: Redirect to request detail or inbox

2. **Settings Page**: `/app/settings/rules/visa-processes` (new)
   - CRUD for VisaDocumentProcess
   - List with toggle active/inactive
   - Form: Name, country code, template document selection
   - Approver picker: Single Guid picker for `TenantSettings.Documents.Visa.ApproverUserId`
   - Template assignment: Multi-select documents to deliver on approval

3. **Integration**: Employee card → Dosyalar tab
   - Already reads from `Documents` table
   - No code change needed; delivered documents appear automatically

### Database Layer

**Migrations** (one per module layer):
1. **Domain Tables**: `VisaDocumentProcess`, `VisaDocumentRequest` in Documents schema
2. **Settings**: Extend `TenantSettings.Documents` with `{ Visa: { ApproverUserId, TemplateDocumentIds } }`
3. **Audit**: Both aggregates inherit `CreatedAt`, `ModifiedAt`, `DeletedAt` tracking

## Phase Gate Summary

| Gate | Status | Details |
|------|--------|---------|
| **GATE 1: Plan Approval** | ✅ PASSED | Plan + 4 assumptions documented; user approved starting work |
| **GATE 2: TDD Red → Green** | 🟡 IN PROGRESS | Domain + 5 tests written; awaiting `dotnet test` run on PC-A |
| **GATE 3: Application Handlers** | ⏳ BLOCKED | Awaiting domain tests to turn green |
| **GATE 4: API + Web** | ⏳ BLOCKED | Awaiting application layer approval |
| **GATE 5: Review & Merge** | ⏳ BLOCKED | Awaiting all above + test coverage review |

## Build & Test Checklist

- [ ] Run: `dotnet test tests/Modules/Sense.Modules.Documents.Tests/ --filter VisaDocumentRequestTests`
- [ ] Verify: All 5 tests pass (green)
- [ ] Review: Domain error codes in error response contracts
- [ ] Update this doc with: **GATE 2 Status** = ✅ GREEN

## Deployment Notes

- **Feature Flag**: None required; domain entities are opt-in via settings
- **Rollout**: 
  1. Deploy domain + application layer
  2. Configure approver + template documents in tenant settings
  3. Enable in UI (Dosyalar tab shows visa request button once configured)

---

**Next Action**: Run tests on PC-A. Once green, proceed to application handlers.

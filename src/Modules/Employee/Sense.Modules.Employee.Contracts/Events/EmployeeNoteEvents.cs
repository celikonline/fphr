using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Employee.Contracts.Events;

/// <summary>
/// A note attachment object is no longer referenced (replaced, removed or its note deleted). Raised through the outbox so
/// the stored file is deleted only after the database change committed; carries the storage key only, never file content.
/// </summary>
public sealed record EmployeeNoteFileOrphaned(Guid TenantId, string StorageKey) : IntegrationEvent(TenantId);

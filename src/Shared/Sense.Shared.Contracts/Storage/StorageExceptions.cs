namespace Sense.Shared.Contracts.Storage;

/// <summary>
/// The object a caller tried to confirm does not exist in storage (the client never uploaded it, or the presigned
/// upload failed). Derives from <see cref="InvalidOperationException"/> so existing catch blocks keep working; callers
/// that care can map it to a client error instead of a 500.
/// </summary>
public sealed class StorageObjectNotFoundException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>The uploaded object's ETag does not match the ETag the client claimed on confirmation.</summary>
public sealed class StorageETagMismatchException(string message) : InvalidOperationException(message);

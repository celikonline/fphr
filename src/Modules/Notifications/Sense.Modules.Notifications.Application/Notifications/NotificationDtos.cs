namespace Sense.Modules.Notifications.Application;

/// <summary>
/// An in-app notification rendered for the request's UI culture; <paramref name="Args"/> lets clients re-render the
/// template in their own language (web: notifications.json "templates.{key}").
/// </summary>
public sealed record NotificationItemDto(
    Guid Id, string TemplateKey, string Category, string Title, string Body, IReadOnlyDictionary<string, string> Args, string? Link,
    DateTime CreatedAt, DateTime? ReadAt);

public sealed record UnreadCountDto(int Count);

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>A browser push notification for every subscribed device of a customer.</summary>
public sealed record PushMessage(Guid UserId, string Title, string Body, string? Url, string? Tag = null);

/// <summary>
/// Hands push notifications to the background sender (web layer). Never blocks or throws:
/// a slow push service must not slow down an order.
/// </summary>
public interface IPushSender
{
    void Enqueue(PushMessage message);
}

/// <summary>Used when web push isn't available (tests, tools).</summary>
public sealed class NullPushSender : IPushSender
{
    public void Enqueue(PushMessage message)
    {
    }
}

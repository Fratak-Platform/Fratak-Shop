using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.Services.Messaging;

/// <summary>
/// Sending verification codes and notifications to the customer's phone.
/// The default implementation only logs the message (and stores it as an in-app
/// notification) so the whole authentication flow works in development without an
/// SMS provider; production only has to register its own implementation.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Sends a short message. Returns false when the provider rejected the message.
    /// </summary>
    Task<bool> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}

/// <summary>Provider metadata used by the admin panel (which provider is active).</summary>
public class SmsSenderOptions
{
    public string Provider { get; set; } = "Logging";

    /// <summary>
    /// Real SMS is disabled by default; in development the code is also written to
    /// the log/in-app notification so the flow stays testable end to end.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Maximum number of messages sent to one phone number per hour.</summary>
    public int MaxPerHour { get; set; } = 5;

    public string? ApiKey { get; set; }

    public string? SenderNumber { get; set; }

    public string? PatternCode { get; set; }
}

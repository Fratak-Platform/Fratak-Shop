using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Core.Services.Messaging;

/// <summary>
/// Development implementation of <see cref="ISmsSender"/>: it writes the message to
/// the application log and stores it as an in-app notification, so the verification
/// and notification flows can be tested completely without an SMS provider.
/// Replace it in DI as soon as a real provider (Kavenegar, SMS.ir, ...) is available.
/// </summary>
public class LoggingSmsSender(
    ILogger<LoggingSmsSender> logger,
    INotificationService notificationService,
    IOptions<SmsSenderOptions> options) : ISmsSender
{
    public async Task<bool> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SMS to {PhoneNumber}: {Message}", phoneNumber, message);

        if (options.Value.Enabled)
        {
            // A real provider is configured but not implemented in this build:
            // failing loudly is better than silently pretending the SMS was sent.
            logger.LogWarning("SMS provider «{Provider}» is enabled but no implementation is registered.", options.Value.Provider);
            return false;
        }

        await notificationService.NotifyPhoneAsync(phoneNumber, message, NotificationType.System, cancellationToken);
        return true;
    }
}

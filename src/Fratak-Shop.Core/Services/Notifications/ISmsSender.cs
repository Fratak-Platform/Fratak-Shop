namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>
/// SMS provider abstraction (verification codes and notification texts). The storefront
/// registers a logging implementation until a real provider (Kavenegar, SMS.ir, ...) is chosen.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message);
}

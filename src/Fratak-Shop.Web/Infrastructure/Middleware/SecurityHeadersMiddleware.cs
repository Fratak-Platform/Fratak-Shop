namespace Fratak_Shop.Web.Infrastructure.Middleware;

/// <summary>
/// Security response headers for every response: a strict Content-Security-Policy
/// (no inline scripts – all behaviour lives in /assets/js), MIME sniffing protection,
/// clickjacking protection, referrer and browser-feature policies.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-src 'self' https:; " +
        "form-action 'self' https://sandbox.zarinpal.com https://payment.zarinpal.com https://www.zarinpal.com; " +
        "frame-ancestors 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!headers.ContainsKey("Content-Security-Policy"))
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
            headers.Remove("X-Powered-By");
            headers.Remove("Server");
            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}

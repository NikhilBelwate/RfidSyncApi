using Microsoft.Extensions.Options;
using RfidSyncApi.Infrastructure.Configuration;
using System.Net;

namespace RfidSyncApi.Infrastructure.Middleware;

/// <summary>
/// Validates the static X-API-TOKEN header on every inbound request.
///
/// Short-circuits with HTTP 401 if:
///   • The header is absent
///   • The header value does not match the configured token
///
/// Token is loaded from ApiSettings:StaticToken (injected via IOptions).
/// In production, set APPSETTINGS__APISTATIC__STATICTOKEN as an App Service
/// Application Setting (never store secrets in appsettings.json).
/// </summary>
public class ApiTokenMiddleware
{
    private const string TokenHeaderName = "X-API-TOKEN";

    private readonly RequestDelegate _next;
    private readonly string _expectedToken;
    private readonly ILogger<ApiTokenMiddleware> _logger;

    public ApiTokenMiddleware(
        RequestDelegate next,
        IOptions<ApiSettings> apiSettings,
        ILogger<ApiTokenMiddleware> logger)
    {
        _next = next;
        _expectedToken = apiSettings.Value.StaticToken
            ?? throw new InvalidOperationException(
                "ApiSettings:StaticToken is not configured. " +
                "Set it via environment variable or Azure App Service Application Settings.");
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // ── Allow Swagger UI and health-check endpoints without a token ─────
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(TokenHeaderName, out var incomingToken))
        {
            _logger.LogWarning(
                "Rejected request from {RemoteIp}: missing {Header} header. Path={Path}",
                context.Connection.RemoteIpAddress,
                TokenHeaderName,
                path);

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = $"Missing required header: {TokenHeaderName}"
            });
            return;
        }

        // Constant-time comparison to prevent timing attacks
        if (!CryptographicEquals(incomingToken.ToString(), _expectedToken))
        {
            _logger.LogWarning(
                "Rejected request from {RemoteIp}: invalid {Header}. Path={Path}",
                context.Connection.RemoteIpAddress,
                TokenHeaderName,
                path);

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = "Invalid API token."
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing side-channel attacks.
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];

        return diff == 0;
    }
}

/// <summary>Extension method for clean registration in Program.cs.</summary>
public static class ApiTokenMiddlewareExtensions
{
    public static IApplicationBuilder UseApiTokenAuthentication(this IApplicationBuilder app)
        => app.UseMiddleware<ApiTokenMiddleware>();
}
